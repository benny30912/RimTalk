using RimTalk.Data;
using RimTalk.Vector;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        public const int MemoryBuffer = 5;  // [NEW] 共用緩衝

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
                        VectorDatabase.Instance.Clear();  // [NEW] 在 lock 內清空向量
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

            // [REMOVE] 不在這裡 Enqueue，由 TalkService 批次處理

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
                // [NEW] 檢查是否已有進行中的總結
                if (data.IsStmSummarizationInProgress) return;
                data.IsStmSummarizationInProgress = true;  // [FIX] 立即設置標記
                var stmSnapshot = data.ShortTermMemories.ToList();
                int snapshotCount = data.NewShortMemoriesSinceSummary;  // [MOD] 記錄快照時的數量
                // [REMOVE] 不再這裡清零計數器
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
                                    // [MOD] 成功才扣：減去快照時的數量
                                    d.NewShortMemoriesSinceSummary = Math.Max(0,
                                        d.NewShortMemoriesSinceSummary - snapshotCount);
                                    d.IsStmSummarizationInProgress = false;  // [NEW] 重置標記
                                    CleanupExcessMemories(d.ShortTermMemories, MaxShortMemories, d.NewShortMemoriesSinceSummary, forceFull: true); // 強制清理到 30
                                }
                            }
                        }
                        else
                        {
                            // [NEW] 空結果也要重置標記
                            var c = WorldComp;
                            if (c != null && c.PawnMemories.TryGetValue(pawnId, out var d))
                            {
                                lock (d) { d.IsStmSummarizationInProgress = false; }
                            }
                        }
                    },
                    onFailureOrCancel: (isCancelled) =>
                    {
                        // [MOD] 失敗時重置標記，計數器不動
                        var c = WorldComp;
                        if (c != null && c.PawnMemories.TryGetValue(pawnId, out var d))
                        {
                            lock (d) { d.IsStmSummarizationInProgress = false; }
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

                // [NEW] 即時清理超額的已歸檔記憶
                CleanupExcessMemories(
                    data.MediumTermMemories,
                    MaxMediumMemories,
                    data.NewMediumMemoriesSinceArchival
                );

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
                // [NEW] 檢查是否已有進行中的歸檔
                if (data.IsMtmConsolidationInProgress) return;
                data.IsMtmConsolidationInProgress = true;  // [FIX] 立即設置標記
                var mtmSnapshot = data.MediumTermMemories.ToList();
                int snapshotCount = data.NewMediumMemoriesSinceArchival;  // [MOD] 記錄快照時的數量
                // [REMOVE] 不再這裡清零計數器
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
                                    // [MOD] 成功才扣：減去快照時的數量
                                    d.NewMediumMemoriesSinceArchival = Math.Max(0,
                                        d.NewMediumMemoriesSinceArchival - snapshotCount);
                                    d.IsMtmConsolidationInProgress = false;  // [NEW] 重置標記
                                    CleanupExcessMemories(d.MediumTermMemories, MaxMediumMemories, d.NewMediumMemoriesSinceArchival, forceFull: true);
                                }
                            }
                        }
                        else
                        {
                            // [NEW] 空結果也要重置標記
                            var c = WorldComp;
                            if (c != null && c.PawnMemories.TryGetValue(pawnId, out var d))
                            {
                                lock (d) { d.IsMtmConsolidationInProgress = false; }
                            }
                        }
                    },
                    onFailureOrCancel: (isCancelled) =>
                    {
                        // [MOD] 失敗時重置標記，計數器不動
                        var c = WorldComp;
                        if (c != null && c.PawnMemories.TryGetValue(pawnId, out var d))
                        {
                            lock (d) { d.IsMtmConsolidationInProgress = false; }
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

        /// <summary>
        /// 清理超額記憶，保護未處理的新記憶
        /// </summary>
        private static void CleanupExcessMemories(List<MemoryRecord> memories, int maxCount, int newSinceProcess, bool forceFull = false)
        {
            int limit = forceFull ? maxCount : (maxCount + MemoryBuffer);
            int excess = memories.Count - limit;
            if (excess <= 0) return;

            // 可安全刪除的數量 = 已處理的記憶數
            int alreadyProcessed = memories.Count - newSinceProcess;
            int safeToDelete = Math.Min(excess, Math.Max(0, alreadyProcessed));

            for (int i = 0; i < safeToDelete; i++)
            {
                VectorDatabase.Instance.RemoveVector(memories[0].Id);
                memories.RemoveAt(0);
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

                // [NEW] 即時清理超額的已總結記憶
                CleanupExcessMemories(
                    data.ShortTermMemories,
                    MaxShortMemories,
                    data.NewShortMemoriesSinceSummary
                );

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
                        // [FIX] 先嘗試移除記憶，成功後再刪除向量
                        bool removed = data.ShortTermMemories?.Remove(memory) == true ||
                                       data.MediumTermMemories?.Remove(memory) == true ||
                                       data.LongTermMemories?.Remove(memory) == true;

                        if (removed)
                        {
                            VectorDatabase.Instance.RemoveVector(memory.Id);
                        }
                    }
                }
            }
        }
    }
}
