using RimTalk.Data;
using RimTalk.Util;
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
        [DataMember(Name = "summary")] public string Summary;
        [DataMember(Name = "keywords")] public List<string> Keywords;
        [DataMember(Name = "importance")] public int Importance;

        public string GetText() => Summary;
    }

    // 用於接收 LLM 批量記憶生成的 DTO
    [DataContract]
    private class MemoryListDto : IJsonData
    {
        [DataMember(Name = "memories")] public List<MemoryGenerationDto> Memories;

        public string GetText() => $"Generated {Memories?.Count ?? 0} memories";
    }

    /// <summary>
    /// 將短期對話歷史(30條=15組)總結為多條中期記憶(15條)
    /// </summary>
    public static async Task<List<MemoryRecord>> SummarizeToMediumAsync(List<TalkMessageEntry> messages, Pawn pawn)
    {
        if (messages.NullOrEmpty()) return [];

        string existingKeywords = GetAllExistingKeywords(pawn);
        string conversationText = FormatConversation(messages);

        // 使用 $$""" 語法
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
            var result = await AIService.Query<MemoryListDto>(request);

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
            Logger.Error($"Failed to summarize memories: {ex.Message}");
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
            var result = await AIService.Query<MemoryListDto>(request);

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
            Logger.Error($"Failed to consolidate memories: {ex.Message}");
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

        var candidates = ltm.OrderBy(m =>
        {
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

    /// <summary>
    /// 根據當前 Context 檢索相關記憶與常識
    /// </summary>
    public static (List<MemoryRecord> memories, List<CommonKnowledgeData> knowledge) GetRelevantMemories(string context, Pawn pawn, int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(context)) return ([], []);

        var comp = Find.World.GetComponent<RimTalkWorldComponent>();
        var history = comp?.SavedTalkHistories.FirstOrDefault(x => x.Pawn == pawn);

        var allMemories = new List<MemoryRecord>();
        if (history != null)
        {
            allMemories.AddRange(history.MediumTermMemories);
            allMemories.AddRange(history.LongTermMemories);
        }

        var contextLower = context.ToLowerInvariant();

        // 1. 檢索記憶
        var relevantMemories = allMemories
            .Where(m => m.Keywords != null && m.Keywords.Any(k => contextLower.Contains(k.ToLowerInvariant())))
            .OrderByDescending(m => m.Importance)
            .ThenBy(m => m.AccessCount)
            .Take(limit)
            .ToList();

        foreach (var mem in relevantMemories)
        {
            mem.AccessCount++;
        }

        // 2. 檢索常識
        var allKnowledge = comp?.CommonKnowledgeStore ?? [];
        var relevantKnowledge = allKnowledge
            .Where(k => k.Keywords != null && k.Keywords.Any(key => contextLower.Contains(key.ToLowerInvariant())))
            .Take(limit)
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
                        dialogueText = string.Join(" ", responses.Select(r => r.Text));
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
            foreach (var m in history.MediumTermMemories) keywords.AddRange(m.Keywords);
            foreach (var m in history.LongTermMemories) keywords.AddRange(m.Keywords);
        }

        if (comp?.CommonKnowledgeStore != null)
        {
            foreach (var k in comp.CommonKnowledgeStore) keywords.AddRange(k.Keywords);
        }

        if (keywords.Count == 0) return "None";

        return string.Join(", ", keywords.Take(50));
    }
}