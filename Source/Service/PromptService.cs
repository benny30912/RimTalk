using RimTalk.Data;
using RimTalk.Source.Memory;
using RimTalk.Util;
using RimTalk.Vector;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;  // [NEW] 為了 Task<T>
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

    // ===============================================================
    // [NEW] 階段一：主執行緒收集資料快照（不執行向量計算）
    // ===============================================================

    /// <summary>
    /// 主執行緒收集遊戲資料快照，供後台執行緒處理
    /// </summary>
    public static ContextSnapshot BuildContextSnapshot(TalkRequest request, List<Pawn> pawns)
    {
        var snapshot = new ContextSnapshot();
        var pawnContexts = new StringBuilder();
        snapshot.InitiatorName = pawns.FirstOrDefault()?.LabelShort ?? "";

        // 準備環境資料
        var gameData = CommonUtil.GetInGameData();
        var contextSettings = Settings.Get().Context;

        for (int i = 0; i < pawns.Count; i++)
        {
            var pawn = pawns[i];
            if (pawn.IsPlayer()) continue;

            // 對第一個 Pawn（主發話者）獲取關鍵詞
            if (i == 0)
            {
                snapshot.ExistingKeywords = MemoryService.GetAllExistingKeywords(pawn);
            }

            InfoLevel infoLevel = Settings.Get().Context.EnableContextOptimization
                                  && i != 0 ? InfoLevel.Short : InfoLevel.Normal;

            // 生成 Pawn 描述並收集動態項目
            var (pawnText, pawnDynamicContext, builder) = CreatePawnContext(pawn, infoLevel);

            // === 補充環境動態項目到 builder ===
            // (雖然環境是共享的，但是記憶是基於 pawn 的，即使在迴圈外部收集後續仍然要分配給個人，在內部收集是可接受的)

            // 時間
            if (contextSettings.IncludeTimeAndDate)
                builder.CollectText(SemanticMapper.MapTimeToSemantic(gameData.Hour12HString));

            // 季節
            if (contextSettings.IncludeSeason && pawn.Map != null)
                builder.CollectSeason(GenLocalDate.Season(pawn.Map));

            // 天氣
            if (contextSettings.IncludeWeather && pawn.Map?.weatherManager?.curWeather != null)
                builder.CollectWeather(pawn.Map.weatherManager.curWeather);

            // 位置
            var room = pawn.GetRoom();
            if (room is { PsychologicallyOutdoors: false } && room.Role != null)
                builder.CollectText(room.Role.label);

            // 溫度
            if (contextSettings.IncludeLocationAndTemperature && pawn.Map != null)
            {
                float temp = pawn.Position.GetTemperature(pawn.Map);
                builder.CollectTemperature(temp);
            }

            // Surroundings 隨機選物
            if (contextSettings.IncludeSurroundings)
            {
                builder.CollectSurrounding(ContextHelper.CollectNearbyItems(pawn, 1));
            }

            // Status 動作句子（從 TalkRequest 取得）
            if (request.StatusActivities != null)
            {
                foreach (var activity in request.StatusActivities)
                    builder.CollectText(activity);
            }

            // Status 人名（從 TalkRequest 取得）
            if (request.StatusNames != null)
            {
                builder.AddNames(request.StatusNames);
            }

            // [NEW] 提取 DialogueType 的純語意部分（清理指令後）
            string cleanedDialogueContext = ExtractDialogueContext(request.Prompt ?? "");

            // 加入清理後的 DialogueType
            if (!string.IsNullOrEmpty(cleanedDialogueContext))
                builder.CollectText(cleanedDialogueContext);

            // [NEW] 解析 RimTalk Event+ 的 Ongoing Events 區塊
            var ongoingEvents = ExtractOngoingEventsList(request.Prompt ?? "");
            foreach (var eventText in ongoingEvents)
            {
                // 每個事件獨立形成一個語意向量
                builder.CollectText(eventText);
            }

            // === 收集 Pawn 資料到快照 ===
            var pawnData = new PawnSnapshotData
            {
                PawnId = pawn.thingIDNumber,
                Items = builder.GetCollectedItems(),
                Names = builder.GetAllNames(),
                PawnText = pawnText
            };
            snapshot.PawnData.Add(pawnData);

            // 常識檢索用的搜索文本
            string searchContext = pawnDynamicContext + "\n" + (request.Prompt ?? "");
            snapshot.KnowledgeSearchText = searchContext;

            // 快取 Pawn 的描述（暫時不含注入記憶）
            Cache.Get(pawn).Context = pawnText;
        }

        return snapshot;
    }

    // ===============================================================
    // [NEW] 階段二：後台執行緒解析向量並注入記憶
    // ===============================================================

    /// <summary>
    /// 後台執行緒解析 Context（批次向量計算 + 記憶檢索 + 記憶注入）
    /// </summary>
    public static async Task<string> ResolveContextAsync(ContextSnapshot snapshot, List<Pawn> pawns)
    {
        return await Task.Run(() =>
        {
            var pawnContexts = new StringBuilder();
            var allKnowledge = new HashSet<string>();

            // 處理每個 Pawn
            for (int i = 0; i < snapshot.PawnData.Count; i++)
            {
                var pawnData = snapshot.PawnData[i];
                var pawn = pawns.FirstOrDefault(p => p.thingIDNumber == pawnData.PawnId);
                if (pawn == null) continue;

                // === 批次向量計算 ===
                var contextVectors = SemanticCache.Instance.GetVectorsBatch(pawnData.Items);

                // === 記憶檢索（語意向量 Max-Sim）===
                var memories = MemoryService.GetRelevantMemoriesBySemantic(
                    contextVectors, pawn, pawnData.Names);

                // === 常識檢索（關鍵詞匹配）===
                var knowledge = MemoryService.GetRelevantKnowledge(snapshot.KnowledgeSearchText);

                // === 注入個人記憶 ===
                string memoryBlock = "";
                if (!memories.NullOrEmpty())
                {
                    memoryBlock = MemoryService.FormatRecalledMemories(memories);
                }
                string resolvedPawnText = pawnData.PawnText.Replace(
                    "[[MEMORY_INJECTION_POINT]]", memoryBlock.TrimEnd());

                // 收集常識
                if (!knowledge.NullOrEmpty())
                {
                    foreach (var k in knowledge)
                        if (!string.IsNullOrEmpty(k.Summary))
                            allKnowledge.Add(k.Summary);
                }

                pawnContexts.AppendLine()
                       .AppendLine($"[Person {i + 1}]")
                       .AppendLine(resolvedPawnText);
            }

            // === 組合完整 Context ===
            var fullContext = new StringBuilder();

            fullContext.AppendLine(Constant.GetInstruction(
                allKnowledge.ToList(),
                snapshot.ExistingKeywords,
                snapshot.InitiatorName
            )).AppendLine();

            fullContext.Append(pawnContexts);

            return fullContext.ToString();
        });
    }

    // ===============================================================
    // [COMPAT] 保留原有同步介面（供外部 Mod相容）
    // ===============================================================

    /// <summary>
    /// [COMPAT] 外部 Mod 相容介面
    /// 內部同步執行 BuildContextSnapshot + ResolveContextAsync
    /// </summary>
    public static string BuildContext(TalkRequest request, List<Pawn> pawns)
    {
        var snapshot = BuildContextSnapshot(request, pawns);
        return ResolveContextAsync(snapshot, pawns).GetAwaiter().GetResult();
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
    private static (string text, string dynamicContext, ContextVectorBuilder builder) CreatePawnContext(
    Pawn pawn,
    InfoLevel infoLevel = InfoLevel.Normal)
    {
        var sb = new StringBuilder();
        var dynamicSb = new StringBuilder();
        var builder = new ContextVectorBuilder();

        sb.Append(CreatePawnBackstory(pawn, infoLevel));

        // Health
        string health = ContextBuilder.GetHealthContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, health);
        AppendIfNotEmpty(dynamicSb, health);

        // [NEW] 收集 Health 向量（事件性 Hediff）
        builder.CollectEventHediffs(pawn.health?.hediffSet?.hediffs);

        var personality = Cache.Get(pawn).Personality;
        if (personality != null)
            sb.AppendLine($"Personality: {personality}");

        // Stop here for invaders
        if (pawn.IsEnemy())
            return (sb.ToString(), dynamicSb.ToString(), builder);

        // Mood
        string mood = ContextBuilder.GetMoodContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, mood);
        AppendIfNotEmpty(dynamicSb, mood);

        // [NEW] 收集 Mood 向量
        if (pawn.needs?.mood != null)
            builder.CollectMood(pawn.needs.mood.CurLevelPercentage);

        // Thoughts
        string thoughts = ContextBuilder.GetThoughtsContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, thoughts);
        AppendIfNotEmpty(dynamicSb, thoughts);

        // [NEW] 收集 Thoughts 向量
        builder.CollectThoughts(ContextHelper.GetThoughts(pawn)?.Keys);

        // Prisoner/Slave status
        AppendIfNotEmpty(sb, ContextBuilder.GetPrisonerSlaveContext(pawn, infoLevel));

        // [NEW] 收集 Resistance/Will/Suppression 向量
        if (pawn.IsPrisoner)
        {
            builder.CollectText(Describer.Resistance(pawn.guest.resistance));
            builder.CollectText(Describer.Will(pawn.guest.will));
        }
        else if (pawn.IsSlave)
        {
            var suppressionNeed = pawn.needs?.TryGetNeed<Need_Suppression>();
            if (suppressionNeed != null)
                builder.CollectText(Describer.Suppression(suppressionNeed.CurLevelPercentage * 100f));
        }

        // Visitor activity
        if (pawn.IsVisitor())
        {
            var lord = pawn.GetLord() ?? pawn.CurJob?.lord;
            if (lord?.LordJob != null)
            {
                var cleanName = lord.LordJob.GetType().Name.Replace("LordJob_", "");
                sb.AppendLine($"Activity: {cleanName}");
                // [NEW] 收集 Activity 向量
                builder.CollectText(cleanName);
            }
        }

        // Relations
        string relations = ContextBuilder.GetRelationsContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, relations);
        AppendIfNotEmpty(dynamicSb, relations);

        // [NEW] 收集 Relations 人名和關係詞
        builder.CollectRelations(relations);

        // Memory injection point
        sb.AppendLine("[[MEMORY_INJECTION_POINT]]");

        if (infoLevel != InfoLevel.Short)
            AppendIfNotEmpty(sb, ContextBuilder.GetEquipmentContext(pawn, infoLevel));

        return (sb.ToString(), dynamicSb.ToString(), builder);
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

    /// <summary>
    /// 從 Prompt 中提取 DialogueType 的語意部分，過濾掉 LLM 指令
    /// </summary>
    private static string ExtractDialogueContext(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return null;

        var lines = prompt.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var result = new List<string>();

        foreach (var line in lines)
        {
            var lower = line.ToLowerInvariant();

            // 過濾純 LLM 指令（精準匹配）
            if (lower.Contains("generate dialogue starting after this") ||
                lower.Contains("do not generate any further lines") ||
                lower.Contains("generate multi turn dialogues") ||
                lower.Contains("do not repeat initial dialogue") ||
                lower.Contains("starts conversation, taking turns") ||
                lower.EndsWith("short monologue") ||
                lower == $"in {Constant.Lang}".ToLowerInvariant())
            {
                continue;
            }
            // 過濾 ArchivePatch 的指令前綴，但保留方括號內的內容
            // "(Talk if you want to accept quest)" -> 過濾
            // "(Talk about quest result)" -> 過濾
            // "(Talk about incident)" -> 過濾
            if (lower == "(talk if you want to accept quest)" ||
                lower == "(talk about quest result)" ||
                lower == "(talk about incident)")
            {
                continue;
            }

            result.Add(line);
        }

        return string.Join(" ", result);
    }

    /// <summary>
    /// [NEW] 從 Prompt 中提取 RimTalk Event+ 的 Ongoing Events 區塊
    /// 回傳每個事件的完整描述（標題+描述合併）
    /// </summary>
    private static List<string> ExtractOngoingEventsList(string prompt)
    {
        var events = new List<string>();
        if (string.IsNullOrWhiteSpace(prompt)) return events;

        const string startMarker = "[Ongoing events]";
        const string endMarker = "[Event list end]";

        int startIdx = prompt.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0) return events;

        int contentStart = startIdx + startMarker.Length;
        int endIdx = prompt.IndexOf(endMarker, contentStart, StringComparison.OrdinalIgnoreCase);
        if (endIdx < 0) return events;

        string content = prompt.Substring(contentStart, endIdx - contentStart);
        var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        StringBuilder currentEvent = null;

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // 檢測新事件開始（以數字+括號開頭，如 "1) "）
            if (trimmed.Length > 2 && char.IsDigit(trimmed[0]) && trimmed[1] == ')')
            {
                // 儲存前一個事件
                if (currentEvent != null && currentEvent.Length > 0)
                {
                    events.Add(currentEvent.ToString().Trim());
                }
                // 開始新事件（移除編號前綴）
                currentEvent = new StringBuilder();
                currentEvent.Append(trimmed.Substring(2).Trim());
            }
            else if (currentEvent != null)
            {
                // 續接描述（縮排行屬於當前事件）
                currentEvent.Append(" ");
                currentEvent.Append(trimmed);
            }
        }

        // 儲存最後一個事件
        if (currentEvent != null && currentEvent.Length > 0)
        {
            events.Add(currentEvent.ToString().Trim());
        }

        return events;
    }
}
