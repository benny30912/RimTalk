using RimTalk.Data;
using RimTalk.Vector;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimTalk.Source.Memory
{
    /// <summary>
    /// 記憶檢索服務
    /// 職責：語意向量檢索 + 關鍵詞檢索
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

        /// <summary>
        /// 根據 Context 向量池檢索相關記憶（語意向量匹配）。
        /// </summary>
        public static List<MemoryRecord> GetRelevantMemoriesBySemantic(
            List<float[]> contextVectors,
            Pawn pawn,
            HashSet<string> contextNames = null)
        {
            if (contextVectors == null || contextVectors.Count == 0 || pawn == null)
                return new List<MemoryRecord>();

            // 權重參數
            float W_semantic = Settings.Get().SemanticWeight;
            float W_importance = Settings.Get().MemoryImportanceWeight;
            const float W_nameBonus = 1f;
            const float W_access = 0.3f;
            const float semanticThreshold = 0.3f;
            const float timeDecayHalfLifeDays = 60f;
            const float gracePeriodDays = 15f;
            int currentTick = GenTicks.TicksGame;

            // 檢索參數
            const int memoryLimit = 8;
            const int stmLimit = 3;
            const int ltmLimit = 1;

            // 1. 準備資料來源
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

            var allMemories = stmList.Concat(mtmList).Concat(ltmList).ToList();
            if (allMemories.Count == 0) return new List<MemoryRecord>();

            // 2. 計分函數（Max-Sim）
            float CalculateScore(MemoryRecord mem)
            {
                // 語意相似度（Max-Sim）
                float semanticScore = 0f;
                var memVector = VectorDatabase.Instance.GetVector(mem.Id);
                if (memVector != null)
                {
                    foreach (var cv in contextVectors)
                    {
                        if (cv == null) continue;
                        float sim = VectorService.CosineSimilarity(cv, memVector);
                        if (sim > semanticScore) semanticScore = sim;
                    }
                    if (semanticScore < semanticThreshold) semanticScore = 0f;
                }

                // 人名匹配加分
                float nameBonus = 0f;
                if (contextNames != null && !mem.Keywords.NullOrEmpty())
                {
                    foreach (var keyword in mem.Keywords)
                    {
                        if (contextNames.Contains(keyword))
                            nameBonus += W_nameBonus;
                    }
                }

                // 必須有語意相關或人名匹配才有分數
                if (semanticScore == 0f && nameBonus == 0f) return -1f;

                // 時間衰減
                float elapsedDays = (currentTick - mem.CreatedTick) / 60000f;
                float effectiveDecayDays = Math.Max(0, elapsedDays - gracePeriodDays);
                float rawDecay = (float)Math.Exp(-effectiveDecayDays / timeDecayHalfLifeDays);
                float minFloor = mem.Importance switch
                {
                    >= 5 => 0.5f,
                    4 => 0.3f,
                    _ => 0f
                };
                if (elapsedDays < gracePeriodDays) rawDecay = 1.0f;
                float timeDecayFactor = Math.Max(rawDecay, minFloor);

                // 最終公式
                float totalScore =
                    semanticScore * W_semantic
                    + mem.Importance * W_importance * timeDecayFactor
                    + nameBonus
                    - mem.AccessCount * W_access;

                return totalScore;
            }

            // 3. 分層檢索
            var relevantMemories = new List<MemoryRecord>();

            // STM（上限 3 條）
            var scoredStm = stmList
                .Select(m => new { Memory = m, Score = CalculateScore(m) })
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

            // LTM（上限 1 條）
            var scoredLtm = ltmList
                .Select(m => new { Memory = m, Score = CalculateScore(m) })
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
                    .Select(m => new { Memory = m, Score = CalculateScore(m) })
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

            // 4. 更新 AccessCount
            foreach (var m in relevantMemories) m.AccessCount++;

            return relevantMemories;
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

        /// <summary>
        /// 獲取所有現有關鍵詞 (用於保持標籤一致性)
        /// </summary>
        public static string GetAllExistingKeywords(Pawn pawn)
        {
            var keywords = new HashSet<string>();

            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();
            if (comp == null) return "None";

            // 從個人記憶收集 (STM + MTM + LTM)
            if (comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data) && data != null)
            {
                if (data.ShortTermMemories != null)
                    foreach (var m in data.ShortTermMemories) keywords.AddRange(m.Keywords ?? []);
                if (data.MediumTermMemories != null)
                    foreach (var m in data.MediumTermMemories) keywords.AddRange(m.Keywords ?? []);
                if (data.LongTermMemories != null)
                    foreach (var m in data.LongTermMemories) keywords.AddRange(m.Keywords ?? []);
            }

            // 從常識庫收集
            if (comp.CommonKnowledgeStore != null)
            {
                foreach (var k in comp.CommonKnowledgeStore) keywords.AddRange(k.Keywords ?? []);
            }

            if (keywords.Count == 0) return "None";

            return string.Join(", ", keywords.Take(1000));
        }
    }
}
