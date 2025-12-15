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
        /// <param name="fallbackTick">若無法計算時間時的備案時間</param>
        public static async Task<List<MemoryRecord>> SummarizeToMediumAsync(List<MemoryRecord> stmList, Pawn pawn, int fallbackTick)
        {
            if (stmList.NullOrEmpty()) return [];

            // 1. 格式化 STM 為帶編號的文本 (ID 僅供本次 Prompt 使用)
            string stmContext = FormatMemoriesWithIds(stmList, out long minTick, out long maxTick);

            // 計算這批 STM 的平均時間，作為 fallback
            int averageTick = (int)((minTick + maxTick) / 2);
            if (averageTick <= 0) averageTick = fallbackTick;

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
                var request = new TalkRequest(prompt, pawn, averageTick);
                var result = await QueryMemory<MemoryListDto>(request);

                if (result?.Memories == null) return [];

                // 3. 將 DTO 轉換為 MemoryRecord
                return result.Memories
                    .Where(m => m != null)
                    .Select(m =>
                    {
                        // 計算加權時間：根據 source_ids 找回原始 STM 的時間做平均
                        int calculatedTick = averageTick;
                        if (m.SourceIds != null && m.SourceIds.Any())
                        {
                            var validTicks = new List<long>();
                            foreach (var id in m.SourceIds)
                            {
                                int index = id - 1; // ID 轉 Index
                                if (index >= 0 && index < stmList.Count)
                                {
                                    validTicks.Add(stmList[index].CreatedTick);
                                }
                            }
                            if (validTicks.Any()) calculatedTick = (int)validTicks.Average();
                        }

                        return new MemoryRecord
                        {
                            Summary = m.Summary,
                            Keywords = m.Keywords ?? [],
                            Importance = Mathf.Clamp(m.Importance, 1, 5),
                            CreatedTick = calculatedTick,
                            AccessCount = 0
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
            string memoryText = FormatMemoriesWithIds(mtmList, out _, out _);

            // 2. 建構 Prompt
            string prompt =
                $$"""
                  将以下 {{pawn.LabelShort}} 的中期记忆片段（Medium Term Memories）合并为 3-6 个高层次的传记式摘要。
                  
                  记忆片段：
                  {{memoryText}}
        
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
                        int calculatedTick = currentTick;
                        List<int> accessCounts = new();

                        if (m.SourceIds != null && m.SourceIds.Any())
                        {
                            var validTicks = new List<long>();
                            foreach (var id in m.SourceIds)
                            {
                                int index = id - 1;
                                if (index >= 0 && index < mtmList.Count)
                                {
                                    var source = mtmList[index];
                                    validTicks.Add(source.CreatedTick);
                                    accessCounts.Add(source.AccessCount);
                                }
                            }
                            if (validTicks.Any()) calculatedTick = (int)validTicks.Average();
                        }

                        int avgAccess = accessCounts.Any() ? (int)accessCounts.Average() : 0;

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

        /// <summary>
        /// 根據上下文檢索相關記憶 (Short + Medium + Long)。
        /// </summary>
        public static List<MemoryRecord> GetRelevantMemories(string context, Pawn pawn)
        {
            // 簡化的檢索邏輯，整合所有層級記憶
            if (string.IsNullOrWhiteSpace(context)) return [];

            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();
            if (comp == null || !comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data))
                return [];

            var allMemories = new List<MemoryRecord>();
            // 注意：這裡假設 STM 也是有價值的，如果只想檢索已歸檔記憶，可移除 STM
            allMemories.AddRange(data.ShortTermMemories);
            allMemories.AddRange(data.MediumTermMemories);
            allMemories.AddRange(data.LongTermMemories);

            // 這裡省略了複雜的 TF-IDF 與時間衰減計算代碼 (與 Legacy 相同)
            // 實作時請直接參考 legacy 的 GetRelevantMemories 邏輯，
            // 唯一的差別是將來源從 data.Medium/Long 改為上述的 allMemories
            // 或是依據您的需求決定是否包含 STM。

            // 範例：簡單實作，僅供參考架構
            return allMemories.Take(5).ToList();
        }

        // --- 輔助方法 ---

        /// <summary>
        /// 將 MemoryRecord 列表格式化為帶 ID 與時間的字串。
        /// </summary>
        private static string FormatMemoriesWithIds(List<MemoryRecord> memories, out long minTick, out long maxTick)
        {
            var sb = new StringBuilder();
            minTick = long.MaxValue;
            maxTick = long.MinValue;

            if (memories.NullOrEmpty()) return "";

            long baseTick = memories.Min(m => m.CreatedTick);

            for (int i = 0; i < memories.Count; i++)
            {
                var m = memories[i];
                if (m.CreatedTick < minTick) minTick = m.CreatedTick;
                if (m.CreatedTick > maxTick) maxTick = m.CreatedTick;

                // 計算相對天數
                int dayIndex = (int)((m.CreatedTick - baseTick) / 60000);
                if (dayIndex < 0) dayIndex = 0;

                string tags = string.Join(",", m.Keywords);
                // ID 從 1 開始
                sb.AppendLine($"[ID: {i + 1}] [Day: {dayIndex}] (Imp:{m.Importance}) [{tags}] {m.Summary}");
            }
            return sb.ToString();
        }

        // 用於從外部獲取所有關鍵詞 (Legacy 邏輯)
        public static string GetAllExistingKeywords(Pawn pawn)
        {
            // 實作邏輯同 Legacy，遍歷 MTM 與 LTM 收集關鍵詞
            return "None"; // 簡化展示
        }

        // 用於 LTM 的維護 (Legacy 邏輯)
        public static void PruneLongTermMemories(List<MemoryRecord> ltm, int maxCount, int currentTick)
        {
            // 實作邏輯同 Legacy，根據權重移除記憶
        }
    }
}
