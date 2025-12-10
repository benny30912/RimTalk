using RimTalk.Client; // 需要引用 Client
using RimTalk.Data;
using RimTalk.Error;  // 需要引用 Error
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
// ★ 解決 Logger 歧義
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Service;

public static class MemoryService
{
    // 用於接收 LLM 單條記憶生成的 DTO
    [DataContract]
    private class MemoryGenerationDto : IJsonData
    {
        [DataMember(Name = "summary")] public string Summary = "";
        [DataMember(Name = "keywords")] public List<string> Keywords = new();
        [DataMember(Name = "importance")] public int Importance = 0;

        public string GetText() => Summary;
    }

    // 用於接收 LLM 批量記憶生成的 DTO
    [DataContract]
    private class MemoryListDto : IJsonData
    {
        [DataMember(Name = "memories")] public List<MemoryGenerationDto> Memories = new();

        public string GetText() => $"Generated {Memories?.Count ?? 0} memories";
    }

    // ★ 新增：私有的記憶查詢方法 (取代 AIService.Query)
    // ★ 修改：徹底移除 ApiHistory 和 Stats 的調用
    private static async Task<T> QueryMemory<T>(TalkRequest request) where T : class, IJsonData
    {
        // 移除：var apiLog = ApiHistory.AddRequest(...) 
        // 因為 ApiHistory 不是線程安全的，且我們決定不記錄背景任務

        try
        {
            // 2. 透過 Factory 取得記憶專用 Client
            // 3. 使用 AIErrorHandler 處理重試 (網路錯誤等)
            // ★ 修改：傳入 ClientType.Memory
            var payload = await AIErrorHandler.HandleWithRetry(async () =>
            {
                // ★ 呼叫獨立的 GetMemoryClientAsync
                var client = await AIClientFactory.GetMemoryClientAsync();
                if (client == null) return null;

                // 記憶生成不使用 System Instruction，直接傳 Prompt
                return await client.GetChatCompletionAsync("", [(Role.User, request.Prompt)]);
            }, isMemory: true); // ★ 指定 isMemory = true

            if (payload == null)
            {
                // 失敗時僅輸出 Log 到控制台，不寫入遊戲內歷史
                Logger.Warning($"Memory generation failed (Payload is null) for: {request.Initiator?.LabelShort}");
                return null;
            }

            // 移除：Stats.IncrementCalls();
            // 移除：Stats.IncrementTokens(payload.TokenCount);
            // Stats 類別也不是線程安全的，背景寫入會導致崩潰

            var jsonData = JsonUtil.DeserializeFromJson<T>(payload.Response);

            // 移除：ApiHistory.AddResponse(...)

            return jsonData;
        }
        catch (Exception ex)
        {
            Logger.Error($"Memory Query Error: {ex.Message}");
            return null;
        }
    }

    // ★ 修改：新增參數 existingKeywords 和 currentTick，移除內部的獲取邏輯
    /// <summary>
    /// 將短期對話歷史(30條=15組)總結為多條中期記憶(15條)
    /// </summary>
    public static async Task<List<MemoryRecord>> SummarizeToMediumAsync(List<TalkMessageEntry> messages, Pawn pawn, string existingKeywords, int currentTick)
    {
        if (messages.NullOrEmpty()) return [];

        // 移除：string existingKeywords = GetAllExistingKeywords(pawn); (改由參數傳入)
        string conversationText = FormatConversation(messages);

        string prompt =
            $$"""
             分析以下对话历史（context + dialogue）。
             目标：{{pawn.LabelShort}}

             历史：
             {{conversationText}}
             
             任务：
             为每组 [context]+[dialogue] 生成相应的记忆记录。
             1. 'summary'：简明扼要地总结发生了什么和说了什么（1 句话）。
             2. 'keywords'：**必须且只能**从以下两个来源中选择总共 3-5 个标签：
                A. **本次[context]或[dialogue]中实际出现的词汇**（如地名、物品名、状态名）。
                B. **现有的参考标签列表**：{{existingKeywords}}。
                **禁止创造文本中不存在的新词汇。**
                **绝对不要包含角色名“{{pawn.LabelShort}}”**
             3. 'importance'：根据以下标准严格评分（1-5）：
                - 1 (琐碎)：日常闲聊、天气、吃饭、无意义的抱怨。
                - 2 (普通)：工作交流、轻微的不适、一般的交易。
                - 3 (值得记住)：建立友谊/仇恨、轻伤、社交争吵、完成任务。
                - 4 (重大)：精神崩溃、袭击战斗、确立恋爱关系、重伤/疾病。
                - 5 (刻骨铭心)：死亡、结婚、永久性残疾、家园毁灭。
                **警告：对于大多数日常对话，评分不应超过 2。只有真正危及生命或改变关系的事件才能评为 4 或 5。**

             重要：summary 字段必须使用简体中文。
             
             输出包含 'memories' 数组的 JSON 对象：
             {
               "memories": [
                 { "summary": "...", "keywords": ["..."], "importance": 3 },
                 ...
               ]
             }
             """;

        try
        {
            // ★ 修改：使用新的建構函數，傳入 currentTick
            var request = new TalkRequest(prompt, pawn, currentTick);
            // ★ 修改：呼叫自己的 QueryMemory
            var result = await QueryMemory<MemoryListDto>(request);

            if (result?.Memories == null) return [];

            // ★ 修改：增加 Where(m => m != null) 過濾，並處理 Summary 為 null 的情況
            return result.Memories
                .Where(m => m != null)
                .Select(m => new MemoryRecord
                {
                    Summary = m.Summary ?? "...", // 防止 Summary 為 null
                    Keywords = m.Keywords ?? [],  // 防止 Keywords 為 null
                    Importance = Mathf.Clamp(m.Importance, 1, 5),
                    AccessCount = 0,
                    CreatedTick = currentTick // 使用傳入的 Tick，避免後台存取 GenTicks
                }).ToList();
        }
        catch (Exception ex)
        {
            // 保持使用 Messages 提示玩家，這是安全的（Unity 主線程會處理 UI 隊列）
            // 但為了保險，建議將 historical 設為 false
            Messages.Message("RimTalk.MemoryService.SummarizeFailed".Translate(ex.Message), MessageTypeDefOf.NegativeEvent, false);
            return [];
        }
    }

    // ★ 修改：新增參數 currentTick
    /// <summary>
    /// 將大量中期記憶合併為數條長期記憶
    /// </summary>
    public static async Task<List<MemoryRecord>> ConsolidateToLongAsync(List<MemoryRecord> memories, Pawn pawn, int currentTick)
    {
        if (memories.NullOrEmpty()) return [];

        string memoryText = FormatMemories(memories);

        string prompt =
            $$"""
             将以下记忆片段合并为几个独特的高层次事件摘要。
             目标：{{pawn.LabelShort}}

             记忆：
             {{memoryText}}
             
             任务：
             1. 将相关事件合并为连贯的长期记忆。
             2. 丢弃过于琐碎的细节。
             3. 为重大事件保留高重要性。
             4. 'keywords'：**必须且只能**从上方提供的“记忆片段”原有的 keywords 中选择最核心的 3-5 个。
                **严禁发明新的关键词。**
                **绝对不要包含角色名“{{pawn.LabelShort}}”**

             重要：summary 字段必须使用简体中文。
             
             输出包含 'memories' 数组的 JSON 对象：
             {
               "memories": [
                 { "summary": "...", "keywords": ["..."], "importance": 4 },
                 ...
               ]
             }
             """;

        try
        {
            // ★ 修改：使用新的建構函數，傳入 currentTick
            var request = new TalkRequest(prompt, pawn, currentTick);
            // ★ 修改：呼叫自己的 QueryMemory
            var result = await QueryMemory<MemoryListDto>(request);

            if (result?.Memories == null) return [];

            // ★ 修改：同樣增加安全過濾
            return result.Memories
                .Where(m => m != null)
                .Select(m => new MemoryRecord
                {
                    Summary = m.Summary ?? "...",
                    Keywords = m.Keywords ?? [],
                    Importance = Mathf.Clamp(m.Importance, 1, 5),
                    AccessCount = 0,
                    CreatedTick = currentTick // 使用傳入的 Tick
                }).ToList();
        }
        catch (Exception ex)
        {
            // ★ 修改：使用 Messages.Message 並暴露翻譯 Key
            Messages.Message("RimTalk.MemoryService.ConsolidateFailed".Translate(ex.Message), MessageTypeDefOf.NegativeEvent, false);
            return [];
        }
    }

    /// <summary>
    /// 根據權重剔除多餘的長期記憶
    /// </summary>
    public static void PruneLongTermMemories(List<MemoryRecord> ltm, int maxCount)
    {
        if (ltm.Count <= maxCount) return;

        float weightImportance = Settings.Get().MemoryImportanceWeight;

        int removeCount = ltm.Count - maxCount;

        // 計算保留分數：分數越低越容易被移除
        // 核心保護：Importance >= 5 的記憶給予極高分數 (不參與移除)
        var candidates = ltm.OrderBy(m =>
        {
            float baseScore = m.Importance >= 5 ? 10000f : 0f;
            return baseScore + m.AccessCount + (m.Importance * weightImportance);
        }).ToList();

        // 移除分數最低的
        for (int i = 0; i < removeCount; i++)
        {
            if (i < candidates.Count)
            {
                ltm.Remove(candidates[i]);
        }
    }
    }

    private class ScoredMemory
    {
        public MemoryRecord Memory;
        public float Score;
    }

    /// <summary>
    /// 根據當前 Context 檢索相關記憶與常識
    /// </summary>
    // ★ 修正：加入空值檢查
    // 修改方法簽名，增加不同的 limit 參數或直接在內部調整
    public static (List<MemoryRecord> memories, List<CommonKnowledgeData> knowledge) GetRelevantMemories(string context, Pawn pawn)
    {
        if (string.IsNullOrWhiteSpace(context)) return ([], []);

        var comp = Find.World.GetComponent<RimTalkWorldComponent>();
        var history = comp?.SavedTalkHistories.FirstOrDefault(x => x.Pawn == pawn);

        var allMemories = new List<MemoryRecord>();
        if (history != null)
        {
            // ★ 安全地加入列表，防止 NullReferenceException
            if (history.MediumTermMemories != null) allMemories.AddRange(history.MediumTermMemories);
            if (history.LongTermMemories != null) allMemories.AddRange(history.LongTermMemories);
        }

        // 優化關鍵字匹配邏輯 (Case-insensitive)
        var contextLower = context.ToLowerInvariant();

        // 設定不同的限制
        int memoryLimit = 5;   // 個人記憶維持 5 條 (避免搶戲)
        int knowledgeLimit = 10; // 常識擴充到 10 條 (確保名詞解釋完整)

        // ★ 讀取設定權重
        float weightKeyword = Settings.Get().KeywordWeight;
        float weightImportance = Settings.Get().MemoryImportanceWeight;
        const float weightAccess = 0.5f;     // 依照您的要求固定為 0.5 (微擾)

        var scoredMemories = new List<ScoredMemory>();

        foreach (var mem in allMemories)
        {
            // 1. 計算匹配數
            int matchCount = mem.Keywords?.Count(k => contextLower.Contains(k.ToLowerInvariant())) ?? 0;

            // ★ 規則：無論如何至少要命中一個關鍵字
            if (matchCount == 0) continue;

            // 2. 計算分數
            // 公式：(重要性 * W_imp) + (關鍵字數 * W_key) - (提及次數 * 0.5)
            float score = (mem.Importance * weightImportance) +
                          (matchCount * weightKeyword) -
                          (mem.AccessCount * weightAccess);

            scoredMemories.Add(new ScoredMemory { Memory = mem, Score = score });
        }

        // 3. 排序與選取
        var relevantMemories = scoredMemories
            .OrderByDescending(x => x.Score) // 分數高的優先
            .ThenBy(x => x.Memory.AccessCount) // 同分時，選講過最少次的 (再次確保新鮮度)
            .Take(memoryLimit)
            .Select(x => x.Memory)
            .ToList();

        // 更新被選中記憶的 AccessCount
        foreach (var mem in relevantMemories)
        {
            mem.AccessCount++;
        }

        // 檢索常識：計算匹配數 -> 優先取匹配度高的
        var allKnowledge = comp?.CommonKnowledgeStore ?? [];
        var relevantKnowledge = allKnowledge
            .Select(k => new
            {
                Knowledge = k,
                MatchCount = k.Keywords?.Count(key => contextLower.Contains(key.ToLowerInvariant())) ?? 0
            })
            .Where(x => x.MatchCount > 0)
            .OrderByDescending(x => x.MatchCount) // 優先顯示匹配關鍵字最多的常識
            .Take(knowledgeLimit)
            .Select(x => x.Knowledge)
            .ToList();

        return (relevantMemories, relevantKnowledge);
    }

    private static string FormatConversation(List<TalkMessageEntry> messages)
    {
        var sb = new StringBuilder();

        // 假設 messages 順序是 User, AI, User, AI...
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];

            if (msg.Role == Role.User)
            {
                // 提取並清理 User Prompt 中的情境
                string context = PromptService.ExtractContextFromPrompt(msg.Text);
                if (string.IsNullOrWhiteSpace(context)) context = "(No context)";
                sb.AppendLine($"[context]: {context.Trim()}");
            }
            else if (msg.Role == Role.AI)
            {
                // 嘗試解析 AI 回傳的 JSON 來獲取純對話文本
                string dialogueText = msg.Text;
                try
                {
                    // TalkHistory 存的是 List<TalkResponse> 的 JSON
                    var responses = JsonUtil.DeserializeFromJson<List<TalkResponse>>(msg.Text);
                    if (responses != null && responses.Any())
                    {
                        // ★ 建議這裡也改成包含名字，讓 LLM 總結時知道是誰說的
                        dialogueText = string.Join("\n", responses.Select(r => $"{r.Name}: {r.Text}"));
                    }
                }
                catch
                {
                    // 如果解析失敗（可能是舊格式或單純文字），就保留原樣
                }

                sb.AppendLine($"[dialogue]: {dialogueText.Trim()}");
                sb.AppendLine("---"); // 分隔線，讓 LLM 知道這是一組結束
            }
        }
        return sb.ToString();
    }

    private static string FormatMemories(List<MemoryRecord> memories)
    {
        var sb = new StringBuilder();
        foreach (var m in memories)
        {
            string tags = string.Join(",", m.Keywords);
            sb.AppendLine($"- [{tags}] (Imp:{m.Importance}) {m.Summary}");
        }
        return sb.ToString();
    }

    // ★ 修改：改為 public，供外部在主線程調用
    public static string GetAllExistingKeywords(Pawn pawn)
    {
        var keywords = new HashSet<string>();

        // 這裡插入核心關鍵詞庫 (CoreMemoryTags)
        foreach (var coreTag in Constant.CoreMemoryTags)
        {
            keywords.Add(coreTag);
        }

        var comp = Find.World.GetComponent<RimTalkWorldComponent>();
        var history = comp?.SavedTalkHistories.FirstOrDefault(x => x.Pawn == pawn);

        if (history != null)
        {
            // 同樣加入空值檢查
            if (history.MediumTermMemories != null)
                foreach (var m in history.MediumTermMemories) keywords.AddRange(m.Keywords);

            if (history.LongTermMemories != null)
                foreach (var m in history.LongTermMemories) keywords.AddRange(m.Keywords);
        }

        if (comp?.CommonKnowledgeStore != null)
        {
            foreach (var k in comp.CommonKnowledgeStore) keywords.AddRange(k.Keywords);
        }

        if (keywords.Count == 0) return "None";

        // 返回時
        return string.Join(", ", keywords.Take(1000)); // 增加上限以容納核心詞 + 動態詞
    }
}