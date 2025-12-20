using RimTalk.Data;
using RimTalk.Util;
using RimTalk.Vector;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Source.Memory
{
    /// <summary>
    /// 記憶服務核心
    /// 職責：生命週期管理 + CRUD + 流程編排
    /// 摘要邏輯委派給 MemorySummarizer
    /// 檢索邏輯委派給 MemoryRetriever
    /// 格式化邏輯委派給 MemoryFormatter
    /// </summary>
    public static class MemoryService
    {
        // --- 任務管理狀態 ---
        private static CancellationTokenSource _cts = new();
        private static readonly ConcurrentQueue<Action> _mainThreadActionQueue = new();

        // 閾值定義
        public const int MaxShortMemories = 30;
        public const int MaxMediumMemories = 60;
        public const int MaxLongMemories = 40;

        private static RimTalkWorldComponent WorldComp => Find.World?.GetComponent<RimTalkWorldComponent>();

        // --- 生命週期與更新 ---

        /// <summary>
        /// 主執行緒更新，處理異步任務的回調。
        /// </summary>
        public static void Update()
        {
            while (_mainThreadActionQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { Logger.Error($"MemoryService main thread action error: {ex}"); }
            }
        }

        /// <summary>
        /// 清理記憶系統狀態
        /// </summary>
        public static void Clear(bool keepSavedData = false)
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            while (_mainThreadActionQueue.TryDequeue(out _)) { }

            if (!keepSavedData)
            {
                var comp = WorldComp;
                if (comp != null)
                {
                    lock (comp.PawnMemories)
                    {
                        comp.PawnMemories.Clear();
                    }
                }
            }
        }

        // --- 流程編排 ---

        /// <summary>
        /// 處理新生成的 STM。外部對話系統將記憶交給 MemoryService 的入口。
        /// </summary>
        public static void OnShortMemoriesGenerated(Pawn pawn, MemoryRecord memory)
        {
            bool thresholdReached = AddMemoryInternal(pawn, memory);

            // 背景計算記憶向量
            if (VectorService.Instance.IsInitialized && memory.Vector == null)
            {
                Task.Run(() =>
                {
                    try
                    {
                        memory.Vector = VectorService.Instance.ComputeEmbedding(memory.Summary);
                        memory.VectorVersion = 1;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"[RimTalk] Failed to compute memory vector: {ex.Message}");
                    }
                });
            }

            if (thresholdReached)
            {
                TriggerStmToMtmSummary(pawn);
            }
        }

        private static void TriggerStmToMtmSummary(Pawn pawn)
        {
            var comp = WorldComp;
            if (comp == null) return;

            PawnMemoryData data;
            lock (comp.PawnMemories)
            {
                if (!comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out data)) return;
            }

            string pawnName = pawn.LabelShort;
            int pawnId = pawn.thingIDNumber;

            lock (data)
            {
                var stmSnapshot = data.ShortTermMemories.ToList();
                int countToRestore = data.NewShortMemoriesSinceSummary;
                data.NewShortMemoriesSinceSummary = 0;
                int currentTick = GenTicks.TicksGame;

                MemorySummarizer.RunRetryableTask(
                    taskName: $"STM->MTM for {pawnName}",
                    action: () => MemorySummarizer.SummarizeToMediumAsync(stmSnapshot, pawn, currentTick),
                    onSuccess: (newMemories) =>
                    {
                        if (!newMemories.NullOrEmpty())
                        {
                            OnMediumMemoriesGenerated(pawn, newMemories);
                            var c = WorldComp;
                            if (c != null && c.PawnMemories.TryGetValue(pawnId, out var d))
                            {
                                lock (d)
                                {
                                    while (d.ShortTermMemories.Count > MaxShortMemories)
                                    {
                                        d.ShortTermMemories.RemoveAt(0);
                                    }
                                }
                            }
                        }
                    },
                    onFailureOrCancel: (isCancelled) =>
                    {
                        if (!isCancelled)
                        {
                            var c = WorldComp;
                            if (c != null && c.PawnMemories.TryGetValue(pawnId, out var d))
                            {
                                d.NewShortMemoriesSinceSummary += countToRestore;
                            }
                        }
                    },
                    token: _cts.Token,
                    mainThreadQueue: _mainThreadActionQueue
                );
            }
        }

        private static void OnMediumMemoriesGenerated(Pawn pawn, List<MemoryRecord> newMemories)
        {
            var comp = WorldComp;
            if (comp == null || !comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data)) return;

            lock (data)
            {
                data.MediumTermMemories ??= [];
                data.MediumTermMemories.AddRange(newMemories);
                data.NewMediumMemoriesSinceArchival += newMemories.Count;

                Messages.Message("RimTalk.MemoryService.MediumMemoryCreated".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NeutralEvent, false);

                if (data.NewMediumMemoriesSinceArchival >= MaxMediumMemories)
                {
                    TriggerMtmToLtmConsolidation(pawn, data);
                }
            }
        }

        private static void TriggerMtmToLtmConsolidation(Pawn pawn, PawnMemoryData data)
        {
            string pawnName = pawn.LabelShort;
            int pawnId = pawn.thingIDNumber;

            lock (data)
            {
                var mtmSnapshot = data.MediumTermMemories.ToList();
                int countToRestore = data.NewMediumMemoriesSinceArchival;
                data.NewMediumMemoriesSinceArchival = 0;
                int currentTick = GenTicks.TicksGame;

                MemorySummarizer.RunRetryableTask(
                    taskName: $"MTM->LTM for {pawnName}",
                    action: () => MemorySummarizer.ConsolidateToLongAsync(mtmSnapshot, pawn, currentTick),
                    onSuccess: (longMemories) =>
                    {
                        if (!longMemories.NullOrEmpty())
                        {
                            OnLongMemoriesGenerated(pawn, longMemories, currentTick);
                            var c = WorldComp;
                            if (c != null && c.PawnMemories.TryGetValue(pawnId, out var d))
                            {
                                lock (d)
                                {
                                    while (d.MediumTermMemories.Count > MaxMediumMemories)
                                    {
                                        d.MediumTermMemories.RemoveAt(0);
                                    }
                                }
                            }
                        }
                    },
                    onFailureOrCancel: (isCancelled) =>
                    {
                        if (!isCancelled)
                        {
                            var c = WorldComp;
                            if (c != null && c.PawnMemories.TryGetValue(pawnId, out var d))
                            {
                                d.NewMediumMemoriesSinceArchival += countToRestore;
                            }
                        }
                    },
                    token: _cts.Token,
                    mainThreadQueue: _mainThreadActionQueue
                );
            }
        }

        private static void OnLongMemoriesGenerated(Pawn pawn, List<MemoryRecord> newMemories, int currentTick)
        {
            var comp = WorldComp;
            if (comp == null || !comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data)) return;

            lock (data)
            {
                data.LongTermMemories ??= [];
                data.LongTermMemories.AddRange(newMemories);
                MemorySummarizer.PruneLongTermMemories(data.LongTermMemories, MaxLongMemories, currentTick);
                Messages.Message("RimTalk.MemoryService.LongMemoryCreated".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NeutralEvent, false);
            }
        }

        // --- CRUD ---

        private static bool AddMemoryInternal(Pawn pawn, MemoryRecord record)
        {
            var comp = WorldComp;
            if (comp == null || pawn == null) return false;

            PawnMemoryData data;
            lock (comp.PawnMemories)
            {
                if (!comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out data))
                {
                    data = new PawnMemoryData { Pawn = pawn };
                    comp.PawnMemories[pawn.thingIDNumber] = data;
                }
            }

            lock (data)
            {
                data.ShortTermMemories.Add(record);
                data.NewShortMemoriesSinceSummary++;
                return data.NewShortMemoriesSinceSummary >= MaxShortMemories;
            }
        }

        /// <summary>
        /// 新增一條短期記憶 (STM) 到 Pawn 的記憶庫。
        /// </summary>
        public static bool AddMemory(Pawn pawn, MemoryRecord record)
        {
            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();
            if (comp == null || pawn == null) return false;

            PawnMemoryData data;
            lock (comp.PawnMemories)
            {
                if (!comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out data))
                {
                    data = new PawnMemoryData { Pawn = pawn };
                    comp.PawnMemories[pawn.thingIDNumber] = data;
                }
            }

            lock (data)
            {
                data.ShortTermMemories.Add(record);
                data.NewShortMemoriesSinceSummary++;
                return data.NewShortMemoriesSinceSummary >= MaxShortMemories;
            }
        }

        /// <summary>
        /// 編輯記憶內容
        /// </summary>
        public static void EditMemory(Pawn pawn, MemoryRecord memory, string newSummary, List<string> newKeywords, int newImportance)
        {
            if (pawn == null || memory == null) return;
            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();
            if (comp == null) return;
            lock (comp.PawnMemories)
            {
                lock (memory)
                {
                    memory.Summary = newSummary;
                    memory.Keywords = newKeywords;
                    memory.Importance = Mathf.Clamp(newImportance, 1, 5);
                }
            }
        }

        /// <summary>
        /// 刪除記憶
        /// </summary>
        public static void DeleteMemory(Pawn pawn, MemoryRecord memory)
        {
            if (pawn == null || memory == null) return;
            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();
            if (comp == null) return;
            lock (comp.PawnMemories)
            {
                if (comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data))
                {
                    lock (data)
                    {
                        if (data.ShortTermMemories?.Remove(memory) == true) return;
                        if (data.MediumTermMemories?.Remove(memory) == true) return;
                        if (data.LongTermMemories?.Remove(memory) == true) return;
                    }
                }
            }
        }

        // --- 委派方法（保持 API 相容性）---

        public static List<MemoryRecord> GetRelevantMemoriesBySemantic(
            List<float[]> contextVectors, Pawn pawn, HashSet<string> contextNames = null)
            => MemoryRetriever.GetRelevantMemoriesBySemantic(contextVectors, pawn, contextNames);

        public static List<MemoryRecord> GetRelevantKnowledge(string context)
            => MemoryRetriever.GetRelevantKnowledge(context);

        public static string GetAllExistingKeywords(Pawn pawn)
            => MemoryRetriever.GetAllExistingKeywords(pawn);

        public static List<(Role role, string message)> BuildMemoryBlockFromHistory(Pawn pawn)
            => MemoryFormatter.BuildMemoryBlockFromHistory(pawn);

        public static string FormatRecalledMemories(List<MemoryRecord> memories)
            => MemoryFormatter.FormatRecalledMemories(memories);

        public static string GetTimeAgo(int createdTick)
            => MemoryFormatter.GetTimeAgo(createdTick);
    }
}
