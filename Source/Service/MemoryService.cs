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
    private static async Task<T> QueryMemory<T>(TalkRequest request) where T : class, IJsonData
    {
        // 1. 記錄請求 (Optional: 為了 Debug 方便，還是記一下)
        var apiLog = ApiHistory.AddRequest(request, "Memory Task");

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
                apiLog.Response = "Failed";
                return null;
            }

            // 4. 統計 Token
            Stats.IncrementCalls();
            Stats.IncrementTokens(payload.TokenCount);

            // 5. 解析與記錄
            var jsonData = JsonUtil.DeserializeFromJson<T>(payload.Response);
            ApiHistory.AddResponse(apiLog.Id, jsonData.GetText(), null, null, payload: payload);

            return jsonData;
        }
        catch (Exception ex)
        {
            Logger.Error($"Memory Query Failed: {ex.Message}");
            apiLog.Response = $"Error: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// 將短期對話歷史(30條=15組)總結為多條中期記憶(15條)
    /// </summary>
    public static async Task<List<MemoryRecord>> SummarizeToMediumAsync(List<TalkMessageEntry> messages, Pawn pawn)
    {
        if (messages.NullOrEmpty()) return [];

        string existingKeywords = GetAllExistingKeywords(pawn);
        string conversationText = FormatConversation(messages);

        string prompt =
            $$"""
             Analyze the following conversation history (context + dialogue pairs).
             Target: {{pawn.LabelShort}}
             
             History:
             {{conversationText}}
             
             Task:
             For EACH [context]+[dialogue] pair, generate a corresponding memory record.
             1. 'summary': A concise summary of what happened and what was said (1 sentence).
             2. 'keywords': Extract 3-5 tags (Use existing tags if applicable: {{existingKeywords}}).
             3. 'importance': Rate from 1 (trivial) to 5 (life-changing).
             
             Output JSON object with a 'memories' array containing one object per exchange:
             {
               "memories": [
                 { "summary": "...", "keywords": ["..."], "importance": 3 },
                 ...
               ]
             }
             """;

        try
        {
            var request = new TalkRequest(prompt, pawn);
            // ★ 修改：呼叫自己的 QueryMemory
            var result = await QueryMemory<MemoryListDto>(request);

            if (result?.Memories == null) return [];

            return result.Memories.Select(m => new MemoryRecord
            {
                Summary = m.Summary,
                Keywords = m.Keywords ?? [],
                Importance = Mathf.Clamp(m.Importance, 1, 5),
                AccessCount = 0,
                CreatedTick = GenTicks.TicksGame
            }).ToList();
        }
        catch (Exception ex)
        {
            // ★ 修改：使用 Messages.Message 並暴露翻譯 Key
            Messages.Message("RimTalk.MemoryService.SummarizeFailed".Translate(ex.Message), MessageTypeDefOf.NegativeEvent, false);
            return [];
        }
    }

    /// <summary>
    /// 將大量中期記憶合併為數條長期記憶
    /// </summary>
    public static async Task<List<MemoryRecord>> ConsolidateToLongAsync(List<MemoryRecord> memories, Pawn pawn)
    {
        if (memories.NullOrEmpty()) return [];

        string memoryText = FormatMemories(memories);

        string prompt =
            $$"""
             Consolidate the following memory fragments into distinct, high-level event summaries.
             Target: {{pawn.LabelShort}}
             
             Memories:
             {{memoryText}}
             
             Task:
             1. Merge related events into coherent long-term memories.
             2. Discard overly trivial details.
             3. Keep importance high for significant events.
             
             Output JSON object with a 'memories' array:
             {
               "memories": [
                 { "summary": "...", "keywords": ["..."], "importance": 4 },
                 ...
               ]
             }
             """;

        try
        {
            var request = new TalkRequest(prompt, pawn);
            // ★ 修改：呼叫自己的 QueryMemory
            var result = await QueryMemory<MemoryListDto>(request);

            if (result?.Memories == null) return [];

            return result.Memories.Select(m => new MemoryRecord
            {
                Summary = m.Summary,
                Keywords = m.Keywords ?? [],
                Importance = Mathf.Clamp(m.Importance, 1, 5),
                AccessCount = 0,
                CreatedTick = GenTicks.TicksGame
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

    /// <summary>
    /// 根據當前 Context 檢索相關記憶與常識
    /// </summary>
    // ★ 修正：加入空值檢查
    public static (List<MemoryRecord> memories, List<CommonKnowledgeData> knowledge) GetRelevantMemories(string context, Pawn pawn, int limit = 5)
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

        // 1. 檢索記憶：計算匹配數 -> 重要性 -> 匹配數 -> 提取次數
        var relevantMemories = allMemories
            .Select(m => new
            {
                Memory = m,
                MatchCount = m.Keywords?.Count(k => contextLower.Contains(k.ToLowerInvariant())) ?? 0
            })
            .Where(x => x.MatchCount > 0) // 只取有關聯的
            .OrderByDescending(x => x.Memory.Importance) // 1. 重要性優先
            .ThenByDescending(x => x.MatchCount)         // 2. 匹配度次之 (越相關越好)
            .ThenBy(x => x.Memory.AccessCount)           // 3. 提取次數少者優先 (增加多樣性)
            .Take(limit)
            .Select(x => x.Memory)
            .ToList();

        // 更新被選中記憶的 AccessCount
        foreach (var mem in relevantMemories)
        {
            mem.AccessCount++;
        }

        // 2. 檢索常識：計算匹配數 -> 優先取匹配度高的
        var allKnowledge = comp?.CommonKnowledgeStore ?? [];
        var relevantKnowledge = allKnowledge
            .Select(k => new
            {
                Knowledge = k,
                MatchCount = k.Keywords?.Count(key => contextLower.Contains(key.ToLowerInvariant())) ?? 0
            })
            .Where(x => x.MatchCount > 0)
            .OrderByDescending(x => x.MatchCount) // 優先顯示匹配關鍵字最多的常識
            .Take(limit)
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

    private static string GetAllExistingKeywords(Pawn pawn)
    {
        var keywords = new HashSet<string>();

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

        // 先加入核心關鍵詞
        foreach (var coreTag in Constant.CoreMemoryTags)
        {
            keywords.Add(coreTag);
        }

        // ...原有邏輯 (從 history 中獲取動態生成的關鍵詞)...

        // 返回時
        return string.Join(", ", keywords.Take(1000)); // 增加上限以容納核心詞 + 動態詞
    }
}