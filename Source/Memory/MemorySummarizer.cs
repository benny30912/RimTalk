using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Util;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Source.Memory
{
    /// <summary>
    /// 記憶摘要服務
    /// 職責：STM→MTM→LTM 的異步摘要邏輯
    /// </summary>
    public static class MemorySummarizer
    {
        // --- DTO: 用於解析 LLM 回傳的 JSON ---

        [DataContract]
        private class MemoryGenerationDto : IJsonData
        {
            [DataMember(Name = "summary")] public string Summary = "";
            [DataMember(Name = "keywords")] public List<string> Keywords = new();
            [DataMember(Name = "importance")] public int Importance = 0;
            [DataMember(Name = "source_ids")] public List<int> SourceIds = new();

            public string GetText() => Summary;
        }

        [DataContract]
        private class MemoryListDto : IJsonData
        {
            [DataMember(Name = "memories")] public List<MemoryGenerationDto> Memories = new();
            public string GetText() => $"Generated {Memories?.Count ?? 0} memories";
        }

        // --- 核心查詢方法 ---

        /// <summary>
        /// 統一的記憶查詢入口，處理 Client 獲取與錯誤重試。
        /// </summary>
        internal static async Task<T> QueryMemory<T>(TalkRequest request) where T : class, IJsonData
        {
            try
            {
                var payload = await AIErrorHandler.HandleWithRetry(async () =>
                {
                    var client = await AIClientFactory.GetMemoryClientAsync();
                    if (client == null) return null;
                    return await client.GetChatCompletionAsync("", [(Role.User, request.Prompt)]);
                }, isMemory: true);

                if (payload == null)
                {
                    Logger.Warning($"Memory generation failed used by: {request.Initiator?.LabelShort}");
                    return null;
                }

                return JsonUtil.DeserializeFromJson<T>(payload.Response);
            }
            catch (Exception ex)
            {
                Logger.Error($"Memory Query Error: {ex.Message}");
                return null;
            }
        }

        // --- STM -> MTM: 將短期摘要合併為中期記憶 ---

        /// <summary>
        /// 將一系列 ShortTermMemories 進一步歸納為 MediumTermMemories。
        /// </summary>
        public static async Task<List<MemoryRecord>> SummarizeToMediumAsync(
            List<MemoryRecord> stmList,
            Pawn pawn,
            int currentTick)
        {
            if (stmList.NullOrEmpty()) return [];

            string stmContext = MemoryFormatter.FormatMemoriesWithIds(stmList);

            string prompt =
                $$"""
                    分析以下 {{pawn.LabelShort}} 近期的对话摘要片段（Short Term Memories）。
      
                    片段列表：
                    {{stmContext}}
      
                    任务：将这些零散片段归纳为**6-8 条**更完整的"事件记忆"（Medium Term Memories）。
      
                    1. 'summary' (简体中文)：
                        - 合并相关片段（如"A問候B"+"A與B談論天氣"→"A与B寒暄"）
                        - 保留独有细节（绰号、玩笑、承诺、激烈语气）
                        - 若事件判定为刻骨铭心 (Importance=5)，请保留「闪光灯式」的细节
                        - **禁止**相对时间（"昨天"等），允许"近期"或"5501年"
      
                    2. 'keywords'：
                        **只能**从该条综述涵盖的原始片段 keywords 中选择 3-5 个
      
                    3. 'importance' (1-5)：
                        1=琐碎 | 2=普通 | 3=值得记住 | 4=重大 | 5=刻骨铭心
      
                    4. 'source_ids'(必填)：
                        列出合并来源的原始 ID（如 [1, 2, 3]）
      
                    输出包含 'memories' 数组的 JSON 对象：
                    {
                      "memories": [
                        { "summary": "...", "keywords": ["..."], "importance": 3, "source_ids": [1, 2, 5] }
                      ]
                    }
                """;

            try
            {
                var request = new TalkRequest(prompt, pawn, currentTick);
                var result = await QueryMemory<MemoryListDto>(request);

                if (result?.Memories == null) return [];

                return result.Memories
                    .Where(m => m != null)
                    .Select(m =>
                    {
                        var (calculatedTick, avgAccess) = CalculateMergedMetadata(m.SourceIds, stmList, currentTick);

                        return new MemoryRecord
                        {
                            Summary = m.Summary,
                            Keywords = m.Keywords ?? [],
                            Importance = Mathf.Clamp(m.Importance, 1, 5),
                            CreatedTick = calculatedTick,
                            AccessCount = avgAccess
                        };
                    }).ToList();
            }
            catch (Exception ex)
            {
                Messages.Message("RimTalk.MemoryService.SummarizeFailed".Translate(ex.Message), MessageTypeDefOf.NegativeEvent, false);
                return [];
            }
        }

        // --- MTM -> LTM: 將中期記憶轉化為長期傳記 ---

        /// <summary>
        /// 將累積的中期記憶合併為高層次的長期記憶 (傳記式)。
        /// </summary>
        public static async Task<List<MemoryRecord>> ConsolidateToLongAsync(
            List<MemoryRecord> mtmList,
            Pawn pawn,
            int currentTick)
        {
            if (mtmList.NullOrEmpty()) return [];

            string mtmContext = MemoryFormatter.FormatMemoriesWithIds(mtmList);

            string prompt =
                $$"""
                  将以下 {{pawn.LabelShort}} 的中期记忆片段（Medium Term Memories）合并为**6-8 条**个高层次的传记式摘要。
                  
                  记忆片段：
                  {{mtmContext}}
        
                  任务：
                  1. 多维度归纳：找出这段时期的主要生活基调。
                  2. 去芜存菁：忽略琐事，除非它是生活基调的一部分。
                  3. 'summary' (简体中文)：以第三人称撰写传记风格的摘要。
                  4. 'keywords'：**必须且只能**从原始片段中选择 3-5 个 keywords。
                  5. 'importance'：继承高重要性事件(4-5)的分数。
                  6. 'source_ids'：**必须**列出该条记忆涵盖的原始片段。
        
                  输出包含 'memories' 数组的 JSON 对象。
                """;

            try
            {
                var request = new TalkRequest(prompt, pawn, currentTick);
                var result = await QueryMemory<MemoryListDto>(request);

                if (result?.Memories == null) return [];

                return result.Memories
                    .Where(m => m != null)
                    .Select(m =>
                    {
                        var (calculatedTick, avgAccess) = CalculateMergedMetadata(m.SourceIds, mtmList, currentTick);

                        return new MemoryRecord
                        {
                            Summary = m.Summary,
                            Keywords = m.Keywords ?? [],
                            Importance = Mathf.Clamp(m.Importance, 1, 5),
                            CreatedTick = calculatedTick,
                            AccessCount = avgAccess
                        };
                    }).ToList();
            }
            catch (Exception ex)
            {
                Messages.Message("RimTalk.MemoryService.ConsolidateFailed".Translate(ex.Message), MessageTypeDefOf.NegativeEvent, false);
                return [];
            }
        }

        /// <summary>
        /// 統一計算合併後的記憶元數據 (時間與活躍度)。
        /// </summary>
        internal static (int Tick, int AccessCount) CalculateMergedMetadata(
            List<int> sourceIds,
            List<MemoryRecord> sources,
            int fallbackTick)
        {
            if (sourceIds == null || !sourceIds.Any() || sources.NullOrEmpty())
            {
                return (fallbackTick, 0);
            }

            var validTicks = new List<long>();
            var validAccess = new List<int>();

            foreach (var id in sourceIds)
            {
                int index = id - 1;
                if (index >= 0 && index < sources.Count)
                {
                    var record = sources[index];
                    validTicks.Add(record.CreatedTick);
                    validAccess.Add(record.AccessCount);
                }
            }

            int avgTick = validTicks.Any() ? (int)validTicks.Average() : fallbackTick;
            int avgAccess = validAccess.Any() ? (int)validAccess.Average() : 0;

            return (avgTick, avgAccess);
        }

        /// <summary>
        /// 根據權重與時間衰減剔除多餘的長期記憶
        /// </summary>
        public static void PruneLongTermMemories(List<MemoryRecord> ltm, int maxCount, int currentTick)
        {
            if (ltm.Count <= maxCount) return;

            float weightImportance = Settings.Get().MemoryImportanceWeight;
            float weightAccess = 0.5f;
            float timeDecayHalfLifeDays = 60f;
            float gracePeriodDays = 15f;
            int gracePeriodTicks = (int)(gracePeriodDays * 60000);

            int removeCount = ltm.Count - maxCount;

            float CalculateRetentionScore(MemoryRecord m)
            {
                if (currentTick - m.CreatedTick < gracePeriodTicks) return 9999f;

                float elapsedDays = (currentTick - m.CreatedTick) / 60000f;
                if (elapsedDays < 0) elapsedDays = 0;
                float effectiveDecayDays = Math.Max(0, elapsedDays - gracePeriodDays);
                float rawDecay = (float)Math.Exp(-effectiveDecayDays / timeDecayHalfLifeDays);

                float minFloor = m.Importance switch
                {
                    >= 5 => 0.5f,
                    4 => 0.3f,
                    _ => 0f
                };
                float finalDecay = Math.Max(rawDecay, minFloor);

                return m.Importance * weightImportance * finalDecay
                     + m.AccessCount * weightAccess * rawDecay;
            }

            var candidates = ltm
                .Select(m => new { Memory = m, Score = CalculateRetentionScore(m) })
                .OrderBy(x => x.Score)
                .ToList();

            for (int i = 0; i < removeCount; i++)
            {
                if (i < candidates.Count)
                {
                    if (candidates[i].Score >= 9900f) break;
                    ltm.Remove(candidates[i].Memory);
                }
            }
        }

        // --- 通用異步重試執行器 ---

        /// <summary>
        /// 通用的異步重試執行器
        /// </summary>
        internal static void RunRetryableTask<T>(
            string taskName,
            Func<Task<T>> action,
            Action<T> onSuccess,
            Action<bool> onFailureOrCancel,
            CancellationToken token,
            ConcurrentQueue<Action> mainThreadQueue)
        {
            Task.Run(async () =>
            {
                int maxRetries = 5;
                int attempt = 0;

                while (attempt < maxRetries && !token.IsCancellationRequested)
                {
                    if (Current.Game == null) return;

                    try
                    {
                        var result = await action();
                        bool isValid = result != null;
                        if (result is System.Collections.ICollection collection && collection.Count == 0)
                            isValid = false;

                        if (isValid)
                        {
                            if (Current.Game != null)
                                mainThreadQueue.Enqueue(() => onSuccess(result));
                            return;
                        }
                        else
                        {
                            Logger.Warning($"Task {taskName} failed (attempt {attempt + 1}/{maxRetries}). Retrying...");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Exception in task {taskName}: {ex.Message}");
                    }

                    attempt++;
                    if (attempt < maxRetries)
                    {
                        int delay = 1000 * 30;
                        try { await Task.Delay(delay, token); } catch (TaskCanceledException) { break; }
                    }
                }

                if (Current.Game == null) return;

                bool isCancelled = token.IsCancellationRequested;
                mainThreadQueue.Enqueue(() => onFailureOrCancel(isCancelled));

                if (!isCancelled)
                {
                    Messages.Message(
                       "RimTalk.TalkHistory.TaskGiveUp".Translate(taskName, maxRetries),
                       MessageTypeDefOf.NeutralEvent,
                       false
                   );
                }

            }, token);
        }
    }
}
