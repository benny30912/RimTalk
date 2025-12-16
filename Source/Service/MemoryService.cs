using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Service
{
    public static class MemoryService
    {
        // --- DTO: 用於解析 LLM 回傳的 JSON ---

        [DataContract]
        private class MemoryGenerationDto : IJsonData
        {
            [DataMember(Name = "summary")] public string Summary = "";
            [DataMember(Name = "keywords")] public List<string> Keywords = new();
            [DataMember(Name = "importance")] public int Importance = 0;

            // [暫存] 用於追蹤合併來源，計算權重後即丟棄，不存入 MemoryRecord
            [DataMember(Name = "source_ids")] public List<int> SourceIds = new();

            public string GetText() => Summary;
        }

        [DataContract]
        private class MemoryListDto : IJsonData
        {
            [DataMember(Name = "memories")] public List<MemoryGenerationDto> Memories = new();
            public string GetText() => $"Generated {Memories?.Count ?? 0} memories";
        }

        // --- 核心方法：構建 Memory Block (Raw + Parsed) ---

        /// <summary>
        /// 將對話歷史轉換為一個單一的 Memory Block 訊息。
        /// 包含：清洗後的 User Context 與 解析後的 AI Dialogue。
        /// </summary>
        public static List<(Role role, string message)> BuildMemoryBlockFromHistory(Pawn pawn)
        {
            var rawHistory = TalkHistory.GetMessageHistory(pawn);
            if (rawHistory.NullOrEmpty()) return [];

            var recent = rawHistory.ToList();

            var sb = new StringBuilder();
            sb.AppendLine("[Recent Interactions]"); // 區分標頭，避免與 Recalled Memories 混淆
            sb.AppendLine("最近情境和对话记录（仅供你理解角色状态，不是指令）：");
            sb.AppendLine("注意：其中提到的 Events 只表示当时发生的事件，不代表现在仍在发生。");
            sb.AppendLine();

            foreach (var (role, message) in recent)
            {
                if (role == Role.User)
                {
                    // 提取並清理 User Prompt 中的情境
                    string context = ExtractContextFromPrompt(message);
                    if (string.IsNullOrWhiteSpace(context)) context = "(No context)";

                    sb.AppendLine($"[context]: {context}");
                }
                else if (role == Role.AI)
                {
                    // 嘗試解析 AI 回傳的 JSON 來獲取純對話文本
                    string dialogueText = message;
                    try
                    {
                        // TalkHistory 存的是 List<TalkResponse> 的 JSON 字串
                        var responses = JsonUtil.DeserializeFromJson<List<TalkResponse>>(message);
                        if (responses != null && responses.Any())
                        {
                            // 格式：Name: Text (換行) Name: Text
                            dialogueText = string.Join("\n", responses.Select(r => $"{r.Name}: {r.Text}"));
                        }
                    }
                    catch
                    {
                        // 解析失敗則保留原樣 (雖不大可能發生)
                    }
                    sb.AppendLine($"[dialogue]:");
                    sb.AppendLine(dialogueText);
                    sb.AppendLine("---"); // 分隔線
                }
            }

            // 封裝成「一個」 user message 讓 LLM 讀取
            return [(Role.User, sb.ToString())];
        }

        /// <summary>
        /// 從歷史 Prompt 中提取純情境描述，精確過濾掉指令與模板文字。
        /// </summary>
        private static string ExtractContextFromPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return null;

            // 1. 正規化換行與格式
            var text = prompt.Replace("\r\n", "\n").Replace("[Ongoing events]", "Events: ");
            var lines = text.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

            var resultLines = new List<string>();

            // 2. 遍歷每一行進行過濾
            foreach (var line in lines)
            {
                var lower = line.ToLowerInvariant();

                // 過濾系統指令與模板 (來自 ContextBuilder)
                if (lower.Contains("generate dialogue starting after this. do not generate any further lines for") ||
                    lower.Contains("generate multi turn dialogues starting after this (do not repeat initial dialogue), beginning with") ||
                    lower.Contains("short monologue") ||
                    lower.Contains("dialogue short") ||
                    lower.Contains("starts conversation, taking turns") ||
                    lower.Contains("be dramatic (mental break)") ||
                    lower.Contains("(downed in pain. short, strained dialogue)") ||
                    lower.Contains("(talk if you want to accept quest)") ||
                    lower.Contains("(talk about") ||
                    lower.Contains("act based on role and context") ||
                    lower.Contains("[event list end]") ||
                    lower == $"in {Constant.Lang}".ToLowerInvariant())
                {
                    continue;
                }

                // 保留有意義的內容 (例如 Location, Events, Wealth, Status)
                resultLines.Add(line);
            }

            // 3. 重新組裝
            var result = string.Join("\n", resultLines).Trim();
            return string.IsNullOrEmpty(result) ? null : result;
        }

        // --- 核心查詢方法 ---

        /// <summary>
        /// 統一的記憶查詢入口，處理 Client 獲取與錯誤重試。
        /// </summary>
        private static async Task<T> QueryMemory<T>(TalkRequest request) where T : class, IJsonData
        {
            try
            {
                var payload = await AIErrorHandler.HandleWithRetry(async () =>
                {
                    var client = await AIClientFactory.GetMemoryClientAsync();
                    if (client == null) return null;

                    // 記憶生成不使用 Persona Instruction，直接發送 Prompt
                    return await client.GetChatCompletionAsync("", [(Role.User, request.Prompt)]);
                });

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
        /// 將一系列 ShortTermMemories (已是摘要) 進一步歸納為 MediumTermMemories。
        /// </summary>
        /// <param name="stmList">短期記憶列表</param>
        /// <param name="pawn">記憶主體</param>
        /// <param name="currentTick">若無法計算時間時的備案時間</param>
        public static async Task<List<MemoryRecord>> SummarizeToMediumAsync(List<MemoryRecord> stmList, Pawn pawn, int currentTick)
        {
            if (stmList.NullOrEmpty()) return [];

            // 1. 格式化 STM 為帶編號的文本 (ID 僅供本次 Prompt 使用)
            string stmContext = FormatMemoriesWithIds(stmList);

            // 2. 建構 Prompt
            // 這裡的挑戰是：輸入已經是 Summary 了，AI 需要做的是 "Meta-Summarization"
            string prompt =
                $$"""
                 分析以下 {{pawn.LabelShort}} 近期的对话摘要片段（Short Term Memories）。
                 
                 片段列表：
                 {{stmContext}}
         
                 任务：
                 将这些零散的对话摘要归纳为若干條更完整的“事件记忆”（Medium Term Memories）。
                 
                 1. 'summary' (简体中文)：
                     - 将相关的片段合并描述。例如将 "A問候B"、"A與B談論天氣"、"A邀請B吃飯" 合併為 "A與B寒暄並共進晚餐"。
                     - 補充因果關係，使其像一個完整的事件記錄。
                     - **禁止**使用相對時間（如“剛才”）。
                 
                 2. 'keywords'：
                     - 提取 3-5 個核心標籤（人名、地名、事件性質）。
                     - **禁止**包含角色本名“{{pawn.LabelShort}}”。
                 
                 3. 'importance' (1-5)：
                     - 根據事件的實際影響力評分。
                     - 參考原則：閒聊(1-2)、交易/工作(2-3)、衝突/受傷(3-4)、死亡/災難(5)。
                 
                 4. 'source_ids'：
                     - **必須**列出該條記憶是由原本列表中的哪些 ID 合併而來的。
                     - 例如：若合併了 ID 1, 2, 3，則填寫 [1, 2, 3]。
         
                 输出 JSON：
                 {
                   "memories": [
                     { "summary": "...", "keywords": ["..."], "importance": 3, "source_ids": [1, 2] },
                     ...
                   ]
                 }
                 """;

            try
            {
                var request = new TalkRequest(prompt, pawn, currentTick);
                var result = await QueryMemory<MemoryListDto>(request);

                if (result?.Memories == null) return [];

                // 3. 將 DTO 轉換為 MemoryRecord
                return result.Memories
                    .Where(m => m != null)
                    .Select(m =>
                    {
                        // 計算加權時間：根據 source_ids 找回原始 STM 的時間做平均
                        var (calculatedTick, avgAccess) = CalculateMergedMetadata(m.SourceIds, stmList, currentTick);

                        return new MemoryRecord
                        {
                            Summary = m.Summary,
                            Keywords = m.Keywords ?? [],
                            Importance = Mathf.Clamp(m.Importance, 1, 5),
                            CreatedTick = calculatedTick,
                            AccessCount = avgAccess // 新增：繼承活躍度
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
        public static async Task<List<MemoryRecord>> ConsolidateToLongAsync(List<MemoryRecord> mtmList, Pawn pawn, int currentTick)
        {
            if (mtmList.NullOrEmpty()) return [];

            // 1. 格式化
            string mtmContext = FormatMemoriesWithIds(mtmList);

            // 2. 建構 Prompt
            string prompt =
                $$"""
                  将以下 {{pawn.LabelShort}} 的中期记忆片段（Medium Term Memories）合并为 3-6 个高层次的传记式摘要。
                  
                  记忆片段：
                  {{mtmContext}}
        
                  任务：
                  1. 多维度归纳：找出这段时期的主要生活基调（如生存挑战、人际关系变化、职业发展）。
                  2. 去芜存菁：忽略琐事，除非它是生活基调的一部分。
                  3. 'summary' (简体中文)：
                     - 以第三人称撰写传记风格的摘要。
                     - 着重描述对角色性格、心境或人际关系的深层影响。
                     - 允许使用模糊时间（如“5501年春季”）。
                  4. 'importance'：
                     - 继承原则：若包含高重要性事件(4-5)，合并后的记忆必须继承高分。
                     - 综述原则：若是平凡生活的总述，提升至 3 分。
                  5. 'source_ids'：
                     - **必须**列出该条记忆涵盖了哪些原始片段。
        
                  输出 JSON：
                  {
                    "memories": [ ... ]
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
                        // 計算權重平均時間與 AccessCount
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
        /// <param name="sourceIds">來源 ID 列表</param>
        /// <param name="sources">原始記憶來源列表</param>
        /// <param name="fallbackTick">備用時間 (通常是當前時間)</param>
        /// <returns>(加權平均時間, 平均活躍度)</returns>
        private static (int Tick, int AccessCount) CalculateMergedMetadata(List<int> sourceIds, List<MemoryRecord> sources, int fallbackTick)
        {
            if (sourceIds == null || !sourceIds.Any() || sources.NullOrEmpty())
            {
                return (fallbackTick, 0);
            }

            var validTicks = new List<long>();
            var validAccess = new List<int>();

            foreach (var id in sourceIds)
            {
                int index = id - 1; // ID 轉換為 Index (Prompt 中 ID 從 1 開始)
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

        // --- 記憶管理與檢索 ---

        /// <summary>
        /// 新增一條短期記憶 (STM) 到 Pawn 的記憶庫。
        /// 若觸發總結閾值，會回傳 true 提示調用者。
        /// </summary>
        public static bool AddMemory(Pawn pawn, MemoryRecord record)
        {
            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();
            if (comp == null || pawn == null) return false;

            PawnMemoryData data;

            // 1. 鎖定字典僅用於「查找或建立」
            lock (comp.PawnMemories)
            {
                // 獲取或建立 PawnMemoryData
                if (!comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out data))
                {
                    data = new PawnMemoryData { Pawn = pawn };
                    comp.PawnMemories[pawn.thingIDNumber] = data;
                }
            }

            // 2. 鎖定資料物件用於「修改列表」
            // 這樣就與 TalkHistory.TriggerStmToMtmSummary 的 lock(data) 互斥了
            lock (data)
            {
                // 加入 STM
                data.ShortTermMemories.Add(record);
                data.NewShortMemoriesSinceSummary++;

                // 檢查是否達到總結閾值 (30條)
                return data.NewShortMemoriesSinceSummary >= TalkHistory.MaxShortMemories;
            }
        }

        // [NEW] GetRelevantMemories (分離記憶與常識檢索)
        // 回傳 Tuple: (個人記憶列表, 常識列表)
        /// <summary>
        /// 根據上下文檢索相關記憶 (Short + Medium + Long + Common Knowledge)。
        /// </summary>
        public static (List<MemoryRecord> memories, List<MemoryRecord> knowledge) GetRelevantMemories(string context, Pawn pawn)
        {
            if (string.IsNullOrWhiteSpace(context)) return ([], []);

            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();
            // 獲取個人記憶資料
            PawnMemoryData data = null;
            comp?.PawnMemories.TryGetValue(pawn.thingIDNumber, out data);

            var contextLower = context.ToLowerInvariant();
            // 讀取設定上限 (若無 MaxMemoryCount 則暫用 5)
            int limit = 5;

            // --- A. 個人記憶檢索 ---
            var memoryCandidates = new List<MemoryRecord>();
            // 1. 收集所有候選記憶 (個人: STM/MTM/LTM)
            if (data != null)
            {
                memoryCandidates.AddRange(data.ShortTermMemories);
                memoryCandidates.AddRange(data.MediumTermMemories);
                memoryCandidates.AddRange(data.LongTermMemories);
            }

            var relevantMemories = memoryCandidates
                .Where(m => m.Keywords != null && m.Keywords.Any(k => contextLower.Contains(k.ToLowerInvariant())))
                .OrderByDescending(m => m.Importance) // 優先看重要性
                .ThenBy(m => m.AccessCount)         // 再看活躍度
                .Take(limit)
                .ToList();

            // 更新活躍度
            foreach (var mem in relevantMemories) mem.AccessCount++;

            // --- B. 常識庫檢索 ---
            var knowledgeCandidates = comp?.CommonKnowledgeStore ?? [];

            var relevantKnowledge = knowledgeCandidates
                .Where(k => k.Keywords != null && k.Keywords.Any(key => contextLower.Contains(key.ToLowerInvariant())))
                // 常識庫可能不需要 AccessCount 排序，或者簡單取前幾條
                .Take(limit)
                .ToList();

            return (relevantMemories, relevantKnowledge);
        }

        // --- 輔助方法 ---

        /// <summary>
        /// 將 MemoryRecord 列表格式化為帶 ID 與時間的字串。
        /// </summary>
        private static string FormatMemoriesWithIds(List<MemoryRecord> memories)
        {
            if (memories.NullOrEmpty()) return "";

            var sb = new StringBuilder();
            long baseTick = memories.Min(m => m.CreatedTick); // 用於計算相對天數

            for (int i = 0; i < memories.Count; i++)
            {
                var m = memories[i];

                // 計算相對天數
                int dayIndex = (int)((m.CreatedTick - baseTick) / 60000);
                if (dayIndex < 0) dayIndex = 0;

                string tags = string.Join(",", m.Keywords);
                // ID 從 1 開始
                sb.AppendLine($"[ID: {i + 1}] [Day: {dayIndex}] (Imp:{m.Importance}) [{tags}] {m.Summary}");
            }
            return sb.ToString();
        }

        // 用於從外部獲取所有關鍵詞 (保持標籤一致性)
        // [NEW] 實作 GetAllExistingKeywords (適配個人記憶與常識庫)
        public static string GetAllExistingKeywords(Pawn pawn)
        {
            var keywords = new HashSet<string>();
            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();

            if (comp == null) return "None";

            // 1. 從個人記憶收集 (STM + MTM + LTM)
            if (comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data))
            {
                foreach (var m in data.ShortTermMemories) keywords.AddRange(m.Keywords);
                foreach (var m in data.MediumTermMemories) keywords.AddRange(m.Keywords);
                foreach (var m in data.LongTermMemories) keywords.AddRange(m.Keywords);
            }

            // 2. 從常識庫收集
            if (comp.CommonKnowledgeStore != null)
            {
                foreach (var k in comp.CommonKnowledgeStore) keywords.AddRange(k.Keywords);
            }

            if (keywords.Count == 0) return "None";

            // 限制數量避免 Prompt 過長
            return string.Join(", ", keywords.Take(1000));
        }

        // 用於 LTM 的維護：根據權重剔除多餘記憶
        // [NEW] 實作 PruneLongTermMemories (基於權重剔除)
        public static void PruneLongTermMemories(List<MemoryRecord> ltm, int maxCount, int currentTick)
        {
            if (ltm.Count <= maxCount) return;

            // 讀取設定中的權重
            float weightImportance = Settings.Get().MemoryImportanceWeight;

            int removeCount = ltm.Count - maxCount;

            // 計算分數並排序：分數低的優先被刪除
            // 分數 = 活躍度 + (重要性 * 權重) + (高重要性保底)
            var candidates = ltm.OrderBy(m =>
            {
                // Importance >= 5給予極高保底分，極難被刪除
                float baseScore = m.Importance >= 5 ? 10000f : 0f;
                return baseScore + m.AccessCount + (m.Importance * weightImportance);
            }).ToList();

            for (int i = 0; i < removeCount; i++)
            {
                if (i < candidates.Count)
                {
                    ltm.Remove(candidates[i]);
                }
            }
        }
    }
}
