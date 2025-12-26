using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Util;
using RimTalk.Vector;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
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
            // [MODIFY] 改為 string 列表以接收 Guid 短碼
            [DataMember(Name = "source_ids")] public List<string> SourceIds = new();

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

            // [NEW] 建立短碼 -> Guid 映射
            var idMap = stmList.ToDictionary(
                m => m.Id.ToString("N").Substring(0, 8),  // 短碼
                m => m.Id                                  // 完整 Guid
            );

            string stmContext = MemoryFormatter.FormatMemoriesWithIds(stmList);

            string prompt =
                $$"""
                    将以下 {{pawn.LabelShort}} 的短期记忆片段合并为**6-8条**事件记忆。
      
                    片段列表：
                    {{stmContext}}
      
                    任务：将这些零散片段归纳为**6-8 条**更完整的"事件记忆"。
      
                    1. 'summary' (简体中文)：
                        - 聚合：相关事件合并为一条（如襲擊+受傷+治療），用连接词（"因此"、"随后"、"导致"）串联事件
                        - 去重：重复行为归纳为模式（"吃了3次"→"吃了好几顿"），但异常事件独立记录
                        - 語意摺疊：将原始 keywords 中的地点/物品融入叙述句中
                        - 保留独有细节（绰号、玩笑、承诺、激烈语气），必须保留涉及人名
                        - 若事件判定为刻骨铭心 (Importance=5)，请保留「闪光灯式」的细节
                        - 禁止相对时间（"昨天"等）
                        - **严格长度限制**：每条 summary 不得超过120字
      
                    2. 'keywords'：
                        列出本段记忆最具有戏剧张力或标志性的具体概念标签（最多4个）
      
                    3. 'importance' (1-5)：参考来源片段
                        1=琐碎日常（闲聊、抱怨）
                        2=普通互动（正常对话、小争执、日常事件）
                        3=值得记住（明确冲突、承诺、重要发现）
                        4=重大事件（受伤、战斗、重大关系变化）
                        5=刻骨铭心（生死、背叛、重大转折）
                        只有真正重要的事件才用 3+
      
                    4. 'source_ids'(必填)：
                        **必须**列出合并来源的原始 ID

                    输出包含 'memories' 数组的 JSON 对象：
                    {
                      "memories": [
                        { "summary": "...", "keywords": ["..."], "importance": 3, "source_ids": ["..."] }
                      ]
                    }

                    [EXAMPLE]
                    输入片段：
                    [ID: a1b2c3d4] [Day: 0] (Imp:4) [矿场, 电浆炮] 机械族突袭矿场，Alice 被蜈蚣的电浆炮击中左腿。
                    [ID: e5f6g7h8] [Day: 0] (Imp:3) [医疗室] Carol 紧急为 Alice 进行手术，抱怨「这破地方连纱布都快没了」。
                    [ID: i9j0k1l2] [Day: 0] (Imp:2) [医疗室] Bob 守在床边，开玩笑说「以后叫你铁腿Alice」。
                    [ID: m3n4o5p6] [Day: 1] (Imp:2) [医疗室] Alice 向 Bob 承诺康复后请他喝酒。
                    输出：
                    {
                        "summary": "机械族蜈蚣突袭矿场，导致 Alice 被电浆炮重创左腿。Carol 在医疗室进行紧急手术时，曾因物资匮乏而抱怨「这破地方连纱布都快没了」。术后 Bob 在床边守候並戏称她为「铁腿Alice」，Alice 为此承诺康复后请他喝酒。 ",
                        "keywords": ["机械族袭击", "医疗危机", "战友情谊", "铁腿Alice"],
                        "importance": 4,
                        "source_ids": ["a1b2c3d4", "e5f6g7h8", "i9j0k1l2", "m3n4o5p6"]
                    }
                """;

            try
            {
                var request = new TalkRequest(prompt, pawn, currentTick);
                var result = await QueryMemory<MemoryListDto>(request);

                if (result?.Memories == null) return [];

                var records = result.Memories
                                    .Where(m => m != null)
                                    .Select(m =>
                                    {
                                        // [MODIFY] 將短碼轉換為完整 Guid
                                        var resolvedSourceIds = m.SourceIds?
                                            .Where(s => idMap.ContainsKey(s))
                                            .Select(s => idMap[s])
                                            .ToList() ?? [];

                                        var (calculatedTick, avgAccess) = CalculateMergedMetadata(resolvedSourceIds, stmList, currentTick);

                                        return new MemoryRecord
                                        {
                                            Summary = m.Summary,
                                            Keywords = m.Keywords ?? [],
                                            Importance = Mathf.Clamp(m.Importance, 1, 5),
                                            CreatedTick = calculatedTick,
                                            AccessCount = avgAccess,  // [FIX] 補上遺漏的 AccessCount
                                            SourceIds = resolvedSourceIds
                                        };
                                    }).ToList();

                // [MODIFY] 將向量計算加入佇列
                foreach (var record in records)
                {
                    VectorQueueService.Instance.Enqueue(record.Id, record.Summary);
                }

                return records;
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

            // [NEW] 建立短碼 -> Guid 映射
            var idMap = mtmList.ToDictionary(
                m => m.Id.ToString("N").Substring(0, 8),  // 短碼
                m => m.Id                                  // 完整 Guid
            );

            string mtmContext = MemoryFormatter.FormatMemoriesWithIds(mtmList);

            string prompt =
                $$"""
                  将以下 {{pawn.LabelShort}} 的中期记忆片段（Medium Term Memories）合并为**6-8条**高层次的传记式摘要。
                  
                  记忆片段：
                  {{mtmContext}}
        
                  任务：分析上述片段，将其重组为**6-8条**6主题鲜明的长期记忆条目。

                  1. 'summary' (简体中文)：以第三人称撰写传记风格的摘要，包含心理状态描述，必须保留涉及人名。
                  - 多维度归纳：找出这段时期的主要生活基调。
                  - 去芜存菁：忽略琐事，除非它是生活基调的一部分，保留可能影响未来的关键痛点（如「资源短缺」「某人的背叛」），並以具体事件作为例证。
                  - 语意折叠：将琐碎的细节 (如具体吃了什么) 折叠为高层次描述 (如生活困苦)，聚焦长期影响。
                  - **严格长度限制**：每条 summary 不得超过180字。
                  2. 'keywords'：列出来源片段中**最具标志性**的概念（最多5个），忽略琐碎细节，无则留空。
                  3. 'importance'：参考来源片段，若高重要性可提升分数
                    1=琐碎日常（闲聊、抱怨）
                    2=普通互动（正常对话、小争执、日常事件）
                    3=值得记住（明确冲突、承诺、重要发现）
                    4=重大事件（受伤、战斗、重大关系变化）
                    5=刻骨铭心（生死、背叛、重大转折）
                  4. 'source_ids'：**必须**列出该条记忆涵盖的原始片段。
        
                  输出包含 'memories' 数组的 JSON 对象。
                  {
                      "memories": [
                        { "summary": "...", "keywords": ["..."], "importance": 3, "source_ids": ["550e8400", "a1b2c3d4", "a1b2c3d4"] }
                      ]
                  }

                  [EXAMPLE]
                  输入片段：
                  [ID: a1b2c3d4] [Day: 0] (Imp:4) [机械族袭击, 医疗危机] 机械族蜈蚣突袭矿场，Alice 被电浆炮击中左腿，Carol 紧急手术时抱怨「这破地方连纱布都快没了」，Bob 开玩笑叫她「铁腿Alice」。
                  [ID: e5f6g7h8] [Day: 5] (Imp:3) [创伤反应] Alice 重返矿场时听到机械声惊慌失措，Bob 安慰她那只是运输机器人。
                  [ID: i9j0k1l2] [Day: 22] (Imp:4) [机械族袭击, 英勇作战] 机械族再次来袭，Alice 克服恐惧持步枪击退了一只枪骑兵，Bob 在旁掩护。
                  [ID: m3n4o5p6] [Day: 25] (Imp:3) [战后庆祝] 战后 Alice 兑现承诺请 Bob 喝酒，Bob 说「有你在真好」。
                  输出：
                  {
                      "summary": "Alice 经历了从恐惧到勇气的蜕变。矿场遭电浆炮袭击的创伤曾让她惊慌失措，但第二次机械族来袭时她克服恐惧击退枪骑兵，Bob 见证并说「有你在真好」。 「铁腿Alice」这个绰号成为她坚强的象征，而 Carol「连纱布都快没了」的抱怨让她对医疗物资短缺始终耿耿于怀。 ",
                      "keywords": ["恐惧克服", "战士蜕变", "Bob挚友", "医疗焦虑"],
                      "importance": 5,
                      "source_ids": ["a1b2c3d4", "e5f6g7h8", "i9j0k1l2", "m3n4o5p6"]
                  }
                """;

            try
            {
                var request = new TalkRequest(prompt, pawn, currentTick);
                var result = await QueryMemory<MemoryListDto>(request);

                if (result?.Memories == null) return [];

                var records = result.Memories
                                    .Where(m => m != null)
                                    .Select(m =>
                                    {
                                        // [MODIFY] 將短碼轉換為完整 Guid
                                        var resolvedSourceIds = m.SourceIds?
                                            .Where(s => idMap.ContainsKey(s))
                                            .Select(s => idMap[s])
                                            .ToList() ?? [];

                                        var (calculatedTick, avgAccess) = CalculateMergedMetadata(resolvedSourceIds, mtmList, currentTick);

                                        return new MemoryRecord
                                        {
                                            Summary = m.Summary,
                                            Keywords = m.Keywords ?? [],
                                            Importance = Mathf.Clamp(m.Importance, 1, 5),
                                            CreatedTick = calculatedTick,
                                            AccessCount = avgAccess,
                                            SourceIds = resolvedSourceIds
                                        };
                                    }).ToList();

                // [MODIFY] 將向量計算加入佇列
                foreach (var record in records)
                {
                    VectorQueueService.Instance.Enqueue(record.Id, record.Summary);
                }

                return records;
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
            List<Guid> sourceIds,  // [MODIFY] 改為 Guid
            List<MemoryRecord> sources,
            int fallbackTick)
        {
            if (sourceIds == null || !sourceIds.Any() || sources.NullOrEmpty())
            {
                return (fallbackTick, 0);
            }

            var validTicks = new List<long>();
            var validAccess = new List<int>();

            // [MODIFY] 使用 Guid 查找
            foreach (var id in sourceIds)
            {
                var record = sources.FirstOrDefault(m => m.Id == id);
                if (record != null)
                {
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
                    // [NEW] 刪除對應向量
                    MemoryVectorDatabase.Instance.RemoveVector(candidates[i].Memory.Id);
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
