using RimTalk.Data;
using RimTalk.Vector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;

namespace RimTalk.Source.Memory
{
    /// <summary>
    /// 記憶檢索服務
    /// 職責：語意向量檢索（本地 Max-Sim） + 雲端二階段檢索（Embedding + Reranker）
    /// </summary>
    public static class MemoryRetriever
    {
        private static RimTalkWorldComponent WorldComp => Find.World?.GetComponent<RimTalkWorldComponent>();

        // 輔助類別：用於排序
        private class ScoredMemory
        {
            public MemoryRecord Memory;
            public float Score;
        }

        // ========================
        // 硬編碼參數 (Reranker 二階段)
        // ========================
        private const int RECALL_TOP_K = 20;           // 階段一召回數量
        private const float RECALL_THRESHOLD = 0.3f;   // 召回閾值
        private const float RERANK_THRESHOLD = 0.3f;   // Reranker 閾值

        // 共用權重常數
        private const float W_NAME_BONUS = 1f;
        private const float W_ACCESS = 0.3f;
        private const float TIME_DECAY_HALF_LIFE_DAYS = 60f;
        private const float GRACE_PERIOD_DAYS = 15f;

        // ========================
        // 公共方法
        // ========================

        /// <summary>
        /// [同步版本] 根據 Context 向量池檢索相關記憶
        /// 本地模式：使用 Max-Sim 語意匹配
        /// </summary>
        public static List<MemoryRecord> GetRelevantMemoriesBySemantic(
            List<float[]> contextVectors,
            Pawn pawn,
            HashSet<string> contextNames = null)
        {
            // 單人模式：配額 8 條 (STM 3 + LTM 1 + 剩餘 4)
            var quotas = GetMemoryQuotas(1);
            return GetRelevantMemoriesInternal(contextVectors, pawn, contextNames, quotas);
        }

        /// <summary>
        /// [異步版本] 多人對話場景：並行 Reranker + 動態配額
        /// 雲端模式：使用 Embedding 召回 + Reranker 精排
        /// 本地模式：降級為同步 Max-Sim
        /// </summary>
        /// <param name="pawnDataList">每個角色的資料 (pawn, contextVectors, contextNames, queryText)</param>
        /// <returns>每個角色對應的記憶列表</returns>
        public static async Task<Dictionary<Pawn, List<MemoryRecord>>> GetRelevantMemoriesForMultiplePawnsAsync(
            List<(Pawn pawn, List<float[]> contextVectors, HashSet<string> contextNames, string queryText)> pawnDataList)
        {
            var result = new Dictionary<Pawn, List<MemoryRecord>>();
            if (pawnDataList == null || pawnDataList.Count == 0)
                return result;

            int pawnCount = pawnDataList.Count;
            var quotas = GetMemoryQuotas(pawnCount);

            // 判斷是否使用雲端 Reranker
            bool useCloudReranker = Settings.Get().UseCloudVectorService &&
                                    !CloudRerankerClient.Instance.IsRateLimited;

            if (useCloudReranker)
            {
                // 雲端模式：並行呼叫 Reranker
                var tasks = pawnDataList.Select(async data =>
                {
                    var memories = await GetRelevantMemoriesWithRerankAsync(
                        data.contextVectors,
                        data.pawn,
                        data.contextNames,
                        data.queryText,
                        quotas);
                    return (data.pawn, memories);
                }).ToList();

                var results = await Task.WhenAll(tasks);
                foreach (var (pawn, memories) in results)
                {
                    result[pawn] = memories;
                }
            }
            else
            {
                // 本地模式：同步 Max-Sim
                foreach (var data in pawnDataList)
                {
                    result[data.pawn] = GetRelevantMemoriesInternal(
                        data.contextVectors,
                        data.pawn,
                        data.contextNames,
                        quotas);
                }
            }

            return result;
        }

        /// <summary>
        /// 根據關鍵詞檢索相關常識（保留原有邏輯）。
        /// </summary>
        public static List<MemoryRecord> GetRelevantKnowledge(string context)
        {
            if (string.IsNullOrWhiteSpace(context)) return new List<MemoryRecord>();

            var comp = WorldComp;
            var allKnowledge = comp?.CommonKnowledgeStore ?? new List<MemoryRecord>();
            if (allKnowledge.Count == 0) return new List<MemoryRecord>();

            // 權重
            float weightImportance = Settings.Get().MemoryImportanceWeight;
            const float weightKeyword = 2.0f;
            const float standardLength = 5.0f;
            const int knowledgeLimit = 10;

            var scoredKnowledge = new List<ScoredMemory>();

            foreach (var k in allKnowledge)
            {
                if (k.Keywords.NullOrEmpty()) continue;
                int matchCount = 0;
                foreach (var key in k.Keywords)
                {
                    if (context.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                        matchCount++;
                }

                if (matchCount == 0) continue;

                float keywordScore = matchCount;
                float lengthMultiplier = standardLength / Math.Max(k.Keywords.Count, 1);
                float score = keywordScore * lengthMultiplier * weightKeyword
                            + k.Importance * weightImportance;
                scoredKnowledge.Add(new ScoredMemory { Memory = k, Score = score });
            }

            var result = scoredKnowledge
                .OrderByDescending(x => x.Score)
                .Take(knowledgeLimit)
                .Select(x => x.Memory)
                .ToList();

            foreach (var k in result) k.AccessCount++;

            return result;
        }

        // ========================
        // 私有方法
        // ========================

        /// <summary>
        /// 根據對話人數計算記憶配額
        /// </summary>
        private static (int total, int stm, int ltm) GetMemoryQuotas(int pawnCount)
        {
            // 1人: 8條 (STM 3 + LTM 1)
            // 2人: 6條 (STM 2 + LTM 1)
            // 3+人: 4條 (STM 1 + LTM 1)
            return pawnCount switch
            {
                1 => (8, 3, 1),
                2 => (6, 2, 1),
                _ => (4, 1, 1)
            };
        }

        /// <summary>
        /// [異步] 使用 Embedding 召回 + Reranker 精排的記憶檢索
        /// </summary>
        private static async Task<List<MemoryRecord>> GetRelevantMemoriesWithRerankAsync(
            List<float[]> contextVectors,
            Pawn pawn,
            HashSet<string> contextNames,
            string queryText,
            (int total, int stm, int ltm) quotas)
        {
            if (contextVectors == null || contextVectors.Count == 0 || pawn == null)
                return new List<MemoryRecord>();

            // 1. 準備資料來源
            var (stmList, mtmList, ltmList) = GetMemoryLists(pawn);
            var allMemories = stmList.Concat(mtmList).Concat(ltmList).ToList();
            if (allMemories.Count == 0) return new List<MemoryRecord>();

            // 2. 階段一：Embedding 召回 Top-K 候選
            var candidates = RecallCandidates(allMemories, contextVectors, RECALL_TOP_K, RECALL_THRESHOLD);
            if (candidates.Count == 0) return new List<MemoryRecord>();

            // 3. 階段二：Reranker 精排
            var documents = candidates.Select(m => MemoryFormatter.FormatMemoryForRerank(m)).ToList();
            var rerankResults = await CloudRerankerClient.Instance.RerankAsync(queryText, documents, RECALL_TOP_K);

            // 4. 若 Reranker 失敗，降級為 Embedding-only 模式
            if (rerankResults.Count == 0)
            {
                Log.Warning("[RimTalk] Reranker 失敗，降級為 Embedding-only 模式");
                return GetRelevantMemoriesInternal(contextVectors, pawn, contextNames, quotas);
            }

            // 5. 將 Reranker 分數映射回記憶
            var rerankScoreMap = new Dictionary<MemoryRecord, float>();
            foreach (var rr in rerankResults)
            {
                if (rr.Index >= 0 && rr.Index < candidates.Count && rr.RelevanceScore >= RERANK_THRESHOLD)
                {
                    rerankScoreMap[candidates[rr.Index]] = rr.RelevanceScore;
                }
            }

            // 6. 計算最終分數並選擇（使用通用方法）
            return SelectMemoriesWithRerankScores(
                stmList, ltmList,
                stmList.Concat(mtmList).Concat(ltmList).ToList(),
                rerankScoreMap,
                contextNames,
                quotas);
        }

        /// <summary>
        /// [同步] 原有 Max-Sim 語意匹配邏輯（使用通用組件）
        /// </summary>
        private static List<MemoryRecord> GetRelevantMemoriesInternal(
            List<float[]> contextVectors,
            Pawn pawn,
            HashSet<string> contextNames,
            (int total, int stm, int ltm) quotas)
        {
            if (contextVectors == null || contextVectors.Count == 0 || pawn == null)
                return new List<MemoryRecord>();

            // 權重參數
            float W_semantic = Settings.Get().SemanticWeight;
            float W_importance = Settings.Get().MemoryImportanceWeight;
            int currentTick = GenTicks.TicksGame;

            // 1. 準備資料來源（使用通用方法）
            var (stmList, mtmList, ltmList) = GetMemoryLists(pawn);
            var allMemories = stmList.Concat(mtmList).Concat(ltmList).ToList();
            if (allMemories.Count == 0) return new List<MemoryRecord>();

            // 2. 計分函數（Max-Sim）- 使用通用組件
            float CalculateScore(MemoryRecord mem)
            {
                // 語意相似度（使用通用組件）
                float semanticScore = CalculateMaxSim(mem, contextVectors, RECALL_THRESHOLD);

                // 人名匹配加分（使用通用方法）
                float nameBonus = CalculateNameBonus(mem, contextNames, W_NAME_BONUS);

                // 必須有語意相關或人名匹配才有分數
                if (semanticScore == 0f && nameBonus == 0f) return -1f;

                // 時間衰減（使用通用方法）
                float timeDecayFactor = CalculateTimeDecay(mem, currentTick, TIME_DECAY_HALF_LIFE_DAYS, GRACE_PERIOD_DAYS);

                // 最終公式
                return semanticScore * W_semantic
                     + mem.Importance * W_importance * timeDecayFactor
                     + nameBonus
                     - mem.AccessCount * W_ACCESS;
            }

            // 3. 分層檢索（使用通用方法）
            return SelectMemoriesByScore(stmList, ltmList, allMemories, CalculateScore, quotas);
        }

        /// <summary>
        /// 獲取指定角色的三層記憶列表
        /// </summary>
        private static (List<MemoryRecord> stm, List<MemoryRecord> mtm, List<MemoryRecord> ltm) GetMemoryLists(Pawn pawn)
        {
            var comp = WorldComp;
            var stmList = new List<MemoryRecord>();
            var mtmList = new List<MemoryRecord>();
            var ltmList = new List<MemoryRecord>();

            if (comp != null)
            {
                lock (comp.PawnMemories)
                {
                    if (comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data))
                    {
                        lock (data)
                        {
                            stmList = (data.ShortTermMemories ?? new List<MemoryRecord>()).ToList();
                            mtmList = (data.MediumTermMemories ?? new List<MemoryRecord>()).ToList();
                            ltmList = (data.LongTermMemories ?? new List<MemoryRecord>()).ToList();
                        }
                    }
                }
            }

            return (stmList, mtmList, ltmList);
        }

        /// <summary>
        /// 階段一：Embedding 召回候選記憶
        /// </summary>
        private static List<MemoryRecord> RecallCandidates(
            List<MemoryRecord> allMemories,
            List<float[]> contextVectors,
            int topK,
            float threshold)
        {
            var scored = new List<(MemoryRecord mem, float sim)>();

            foreach (var mem in allMemories)
            {
                float maxSim = CalculateMaxSim(mem, contextVectors, threshold);
                if (maxSim > 0)
                {
                    scored.Add((mem, maxSim));
                }
            }

            // 返回 Top-K 候選
            return scored
                .OrderByDescending(x => x.sim)
                .Take(topK)
                .Select(x => x.mem)
                .ToList();
        }

        /// <summary>
        /// 計算記憶與 Context 向量池的 Max-Sim
        /// </summary>
        private static float CalculateMaxSim(MemoryRecord mem, List<float[]> contextVectors, float threshold)
        {
            var memVector = VectorDatabase.Instance.GetVector(mem.Id);

            // 缺失向量：加入佇列補算
            if (memVector == null)
            {
                if (!string.IsNullOrEmpty(mem.Summary))
                    VectorQueueService.Instance.Enqueue(mem.Id, mem.Summary);
                return 0f;
            }

            // Max-Sim 計算
            float maxSim = 0f;
            foreach (var cv in contextVectors)
            {
                if (cv == null) continue;
                float sim = VectorService.CosineSimilarity(cv, memVector);
                if (sim > maxSim) maxSim = sim;
            }

            return maxSim >= threshold ? maxSim : 0f;
        }

        /// <summary>
        /// 根據 Reranker 分數選擇記憶（分層配額）
        /// </summary>
        private static List<MemoryRecord> SelectMemoriesWithRerankScores(
            List<MemoryRecord> stmList,
            List<MemoryRecord> ltmList,
            List<MemoryRecord> allMemories,
            Dictionary<MemoryRecord, float> rerankScoreMap,
            HashSet<string> contextNames,
            (int total, int stm, int ltm) quotas)
        {
            // 權重參數
            float W_rerank = Settings.Get().SemanticWeight; // 沿用 SemanticWeight
            float W_importance = Settings.Get().MemoryImportanceWeight;
            int currentTick = GenTicks.TicksGame;

            // 計分函數
            float CalculateFinalScore(MemoryRecord mem)
            {
                if (!rerankScoreMap.TryGetValue(mem, out float rerankScore))
                    return -1f;

                float nameBonus = CalculateNameBonus(mem, contextNames, W_NAME_BONUS);
                float timeDecayFactor = CalculateTimeDecay(mem, currentTick, TIME_DECAY_HALF_LIFE_DAYS, GRACE_PERIOD_DAYS);

                return rerankScore * W_rerank
                     + mem.Importance * W_importance * timeDecayFactor
                     + nameBonus
                     - mem.AccessCount * W_ACCESS;
            }

            return SelectMemoriesByScore(stmList, ltmList, allMemories, CalculateFinalScore, quotas);
        }

        /// <summary>
        /// 通用：按分數分層選擇記憶
        /// </summary>
        private static List<MemoryRecord> SelectMemoriesByScore(
            List<MemoryRecord> stmList,
            List<MemoryRecord> ltmList,
            List<MemoryRecord> allMemories,
            Func<MemoryRecord, float> scoreFunc,
            (int total, int stm, int ltm) quotas)
        {
            var relevantMemories = new List<MemoryRecord>();
            int memoryLimit = quotas.total;
            int stmLimit = quotas.stm;
            int ltmLimit = quotas.ltm;

            // STM 優先
            var scoredStm = stmList
                .Select(m => new { Memory = m, Score = scoreFunc(m) })
                .Where(x => x.Score >= 0)
                .OrderByDescending(x => x.Score)
                .ToList();
            if (scoredStm.Any())
            {
                float stmMaxScore = scoredStm[0].Score;
                float stmThreshold = stmMaxScore * 0.5f;
                var selectedStm = scoredStm
                    .Where(x => x.Score >= stmThreshold)
                    .Take(stmLimit)
                    .Select(x => x.Memory)
                    .ToList();
                relevantMemories.AddRange(selectedStm);
            }

            // LTM 優先
            var scoredLtm = ltmList
                .Select(m => new { Memory = m, Score = scoreFunc(m) })
                .Where(x => x.Score >= 0)
                .OrderByDescending(x => x.Score)
                .ToList();
            if (scoredLtm.Any())
            {
                var selectedLtm = scoredLtm.Take(ltmLimit).Select(x => x.Memory).ToList();
                relevantMemories.AddRange(selectedLtm);
            }

            // 剩餘名額
            int remainingSlots = memoryLimit - relevantMemories.Count;
            if (remainingSlots > 0)
            {
                var alreadySelected = new HashSet<MemoryRecord>(relevantMemories);
                var remainingCandidates = allMemories
                    .Where(m => !alreadySelected.Contains(m))
                    .Select(m => new { Memory = m, Score = scoreFunc(m) })
                    .Where(x => x.Score >= 0)
                    .OrderByDescending(x => x.Score)
                    .ToList();
                if (remainingCandidates.Any())
                {
                    float remainingMaxScore = remainingCandidates[0].Score;
                    float remainingThreshold = remainingMaxScore * 0.5f;
                    var selectedRemaining = remainingCandidates
                        .Where(x => x.Score >= remainingThreshold)
                        .Take(remainingSlots)
                        .Select(x => x.Memory)
                        .ToList();
                    relevantMemories.AddRange(selectedRemaining);
                }
            }

            // 更新 AccessCount
            foreach (var m in relevantMemories) m.AccessCount++;

            return relevantMemories;
        }

        /// <summary>
        /// 計算人名匹配加分
        /// </summary>
        private static float CalculateNameBonus(MemoryRecord mem, HashSet<string> contextNames, float weight)
        {
            float nameBonus = 0f;
            if (contextNames != null && !string.IsNullOrEmpty(mem.Summary))
            {
                foreach (var name in contextNames)
                {
                    if (mem.Summary.Contains(name))
                        nameBonus += weight;
                }
            }
            return nameBonus;
        }

        /// <summary>
        /// 計算時間衰減因子
        /// </summary>
        private static float CalculateTimeDecay(MemoryRecord mem, int currentTick, float halfLifeDays, float gracePeriodDays)
        {
            float elapsedDays = (currentTick - mem.CreatedTick) / 60000f;
            float effectiveDecayDays = Math.Max(0, elapsedDays - gracePeriodDays);
            float rawDecay = (float)Math.Exp(-effectiveDecayDays / halfLifeDays);
            float minFloor = mem.Importance switch
            {
                >= 5 => 0.5f,
                4 => 0.3f,
                _ => 0f
            };
            if (elapsedDays < gracePeriodDays) rawDecay = 1.0f;
            return Math.Max(rawDecay, minFloor);
        }
    }
}
