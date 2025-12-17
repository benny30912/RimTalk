using RimTalk.Data;
using RimTalk.Util;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Verse;
using Verse.AI.Group;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Service;

/// <summary>
/// All public methods in this class are designed to be patchable with Harmony.
/// Use Prefix to replace functionality, Postfix to extend it.
/// </summary>
public static class PromptService
{
    public enum InfoLevel { Short, Normal, Full }

    // 注意：需要 TalkRequest 來獲取 Prompt 內容
    public static string BuildContext(TalkRequest request, List<Pawn> pawns)
    {
        // [REMOVED] 移除這行，先不寫入 System Instruction
        var pawnContexts = new StringBuilder();
        var allKnowledge = new HashSet<string>();

        // 1. 準備搜索上下文
        var eventTags = CoreTagMapper.GetTextTags(request.Prompt ?? ""); // [MOD] 啟用 CoreTagMapper
        // ★ 關鍵策略：直接使用已經被 DecoratePrompt + Event+ Patch 處理過的 Prompt 作為檢索源
        // 這包含了：指令 + 環境描述 + Ongoing Events
        string distinctContext = (request.Prompt ?? "") + "\n" + string.Join(", ", eventTags);

        for (int i = 0; i < pawns.Count; i++)
        {
            var pawn = pawns[i];
            if (pawn.IsPlayer()) continue;
            // [MOD] 邏輯修正：除非開啟優化，否則一律使用 Normal
            // 原本邏輯會強迫非發話人 (i != 0) 使用 Short，現在加入 EnableContextOptimization 判斷
            InfoLevel infoLevel = Settings.Get().Context.EnableContextOptimization
                                  && i != 0 ? InfoLevel.Short : InfoLevel.Normal;
            // 生成 Pawn 描述 (包含佔位符)
            var (pawnText, pawnDynamicContext) = CreatePawnContext(pawn, infoLevel);

            // 2. 檢索記憶與常識
            string searchContext = pawnDynamicContext + "\n" + distinctContext; // Pawn 自身的動態狀態 (pawnDynamicContext) + 當前對話事件 (distinctContext)
            var (memories, knowledge) = MemoryService.GetRelevantMemories(searchContext, pawn);

            // 3. 注入個人記憶到 Pawn Context (取代佔位符)
            string memoryBlock = "";
            if (!memories.NullOrEmpty())
            {
                memoryBlock = MemoryService.FormatRecalledMemories(memories);
            }
            
            // 用 MemoryBlock 取代佔位符，如果 MemoryBlock 為空，就相當於刪除佔位符
            pawnText = pawnText.Replace("[[MEMORY_INJECTION_POINT]]", memoryBlock.TrimEnd());

            // 4. 收集常識 (不注入 Pawn Context)
            if (!knowledge.NullOrEmpty())
            {
                foreach (var k in knowledge)
                    if (!string.IsNullOrEmpty(k.Summary)) allKnowledge.Add(k.Summary);
            }

            Cache.Get(pawn).Context = pawnText;

            pawnContexts.AppendLine()
                   .AppendLine($"[Person {i + 1}]")
                   .AppendLine(pawnText);
        }

        var fullContext = new StringBuilder();

        // 5. [關鍵修正] 在這裡動態生成包含常識的 System Instruction，並放在最前面
        fullContext.AppendLine(Constant.GetInstruction(allKnowledge.ToList())).AppendLine();

        // 6. 接上各個角色的 Context
        fullContext.Append(pawnContexts);

        return fullContext.ToString();
    }

    /// <summary>Creates the basic pawn backstory section.</summary>
    public static string CreatePawnBackstory(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var sb = new StringBuilder();
        var name = pawn.LabelShort;
        var title = pawn.story?.title == null ? "" : $"({pawn.story.title})";
        var genderAndAge = Regex.Replace(pawn.MainDesc(false), @"\(\d+\)", "");
        sb.AppendLine($"{name} {title} ({genderAndAge})");

        var role = pawn.GetRole(true);
        if (role != null)
            sb.AppendLine($"Role: {role}");

        // Each section can be patched independently
        AppendIfNotEmpty(sb, ContextBuilder.GetRaceContext(pawn, infoLevel));
        
        if (infoLevel != InfoLevel.Short && !pawn.IsVisitor() && !pawn.IsEnemy())
            AppendIfNotEmpty(sb, ContextBuilder.GetNotableGenesContext(pawn, infoLevel));
        
        AppendIfNotEmpty(sb, ContextBuilder.GetIdeologyContext(pawn, infoLevel));

        // Stop here for invaders and visitors
        if (pawn.IsEnemy() || pawn.IsVisitor())
            return sb.ToString();

        AppendIfNotEmpty(sb, ContextBuilder.GetBackstoryContext(pawn, infoLevel));
        AppendIfNotEmpty(sb, ContextBuilder.GetTraitsContext(pawn, infoLevel));
        
        if (infoLevel != InfoLevel.Short)
            AppendIfNotEmpty(sb, ContextBuilder.GetSkillsContext(pawn, infoLevel));

        return sb.ToString();
    }

    /// <summary>Creates the full pawn context.</summary>
    // [MOD] 修改回傳簽名，新增 dynamicContext
    private static (string text, string dynamicContext) CreatePawnContext(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var sb = new StringBuilder();
        var dynamicSb = new StringBuilder(); // [NEW] 用於收集動態上下文

        sb.Append(CreatePawnBackstory(pawn, infoLevel));

        // Each section can be patched independently

        // Health
        string health = ContextBuilder.GetHealthContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, health);
        AppendIfNotEmpty(dynamicSb, health);

        var personality = Cache.Get(pawn).Personality;
        if (personality != null)
            sb.AppendLine($"Personality: {personality}");

        // Stop here for invaders
        if (pawn.IsEnemy())
            return (sb.ToString(), dynamicSb.ToString());

        // Mood
        string mood = ContextBuilder.GetMoodContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, mood);
        AppendIfNotEmpty(dynamicSb, mood);

        // Thoughts
        string thoughts = ContextBuilder.GetThoughtsContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, thoughts);
        AppendIfNotEmpty(dynamicSb, thoughts);

        AppendIfNotEmpty(sb, ContextBuilder.GetPrisonerSlaveContext(pawn, infoLevel));
        
        // Visitor activity
        if (pawn.IsVisitor())
        {
            var lord = pawn.GetLord() ?? pawn.CurJob?.lord;
            if (lord?.LordJob != null)
            {
                var cleanName = lord.LordJob.GetType().Name.Replace("LordJob_", "");
                sb.AppendLine($"Activity: {cleanName}");
            }
        }

        // Relations
        string relations = ContextBuilder.GetRelationsContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, relations);
        AppendIfNotEmpty(dynamicSb, relations);

        // ★ [插入點] 在 Relations 之後，Equipment 之前
        sb.AppendLine("[[MEMORY_INJECTION_POINT]]");

        if (infoLevel != InfoLevel.Short)
            AppendIfNotEmpty(sb, ContextBuilder.GetEquipmentContext(pawn, infoLevel));

        // --- [New] 核心修改：注入 Abstract Tags ---
        // 使用 CoreTagMapper 分析 Pawn 狀態，生成抽象標籤 (如 "疼痛", "生病", "絕望")
        // 這些標籤專門用於增強檢索，不需要顯示在給 LLM 的 Prompt 中 (除非你想)
        var abstractTags = CoreTagMapper.GetAbstractTags(pawn);
        if (abstractTags.Any())
        {
            string tagsLine = $"{string.Join(", ", abstractTags)}";
            dynamicSb.AppendLine(tagsLine);
        }

        return (sb.ToString(), dynamicSb.ToString());
    }

    /// <summary>Decorates the prompt with dialogue type, time, weather, location, and environment.</summary>
    public static void DecoratePrompt(TalkRequest talkRequest, List<Pawn> pawns, string status)
    {
        var contextSettings = Settings.Get().Context;
        var sb = new StringBuilder();
        var gameData = CommonUtil.GetInGameData();
        var mainPawn = pawns[0];
        var shortName = $"{mainPawn.LabelShort}";

        // Dialogue type
        ContextBuilder.BuildDialogueType(sb, talkRequest, pawns, shortName, mainPawn);

        // Time and weather
        sb.Append($"\n{status}");
        if (contextSettings.IncludeTimeAndDate)
        {
            sb.Append($"\nTime: {gameData.Hour12HString}");
            sb.Append($"\nToday: {gameData.DateString}");
        }
        if (contextSettings.IncludeSeason)
            sb.Append($"\nSeason: {gameData.SeasonString}");
        if (contextSettings.IncludeWeather)
            sb.Append($"\nWeather: {gameData.WeatherString}");

        // Location
        ContextBuilder.BuildLocationContext(sb, contextSettings, mainPawn);

        // Environment
        ContextBuilder.BuildEnvironmentContext(sb, contextSettings, mainPawn);

        if (contextSettings.IncludeWealth)
            sb.Append($"\nWealth: {Describer.Wealth(mainPawn.Map.wealthWatcher.WealthTotal)}");

        if (AIService.IsFirstInstruction())
            sb.Append($"\nin {Constant.Lang}");

        talkRequest.Prompt = sb.ToString();

        // 當此方法結束時，RimTalk Event+ 的 Postfix 會自動執行
        // 並將 Ongoing Events 追加到 talkRequest.Prompt 的尾端
    }

    private static void AppendIfNotEmpty(StringBuilder sb, string text)
    {
        if (!string.IsNullOrEmpty(text))
            sb.AppendLine(text);
    }
}
