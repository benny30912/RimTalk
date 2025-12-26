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
using static Verse.AI.ThingCountTracker;
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
        var gameData = CommonUtil.GetInGameData();
        var contextSettings = Settings.Get().Context;
        var allPawnTexts = new StringBuilder();

        // [NEW] 提取完整情境描述（用於 QueryText）
        string contextFromPrompt = MemoryFormatter.ExtractContextFromPrompt(request.Prompt ?? "");

        // 只在 DialogueType 不重複時加入 QueryText
        if (!string.IsNullOrEmpty(request.DialogueType) &&
            (string.IsNullOrEmpty(contextFromPrompt) || !contextFromPrompt.Contains(request.DialogueType)))
        {
            contextFromPrompt = $"\nDialogueType: {request.DialogueType}" + contextFromPrompt;
        }

        List<string> filteredLines = new List<string>();

        if (!string.IsNullOrEmpty(contextFromPrompt))
        {
            // [NEW] 過濾 Today 行（日期對記憶檢索無意義）
            filteredLines = contextFromPrompt
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith("Today:", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var ongoingEvents = ExtractOngoingEventsList(request.Prompt ?? "");

        for (int i = 0; i < pawns.Count; i++)
        {
            var pawn = pawns[i];
            if (pawn.IsPlayer()) continue;

            InfoLevel infoLevel = Settings.Get().Context.EnableContextOptimization
                                  && i != 0 ? InfoLevel.Short : InfoLevel.Normal;

            // 生成 Pawn 描述並收集動態項目 + QueryText
            var (pawnText, builder, queryText) = CreatePawnContext(pawn, infoLevel);

            // [SIMPLIFIED] 直接附加 ExtractDialogueContext（包含環境資訊）
            var fullQuerySb = new StringBuilder(queryText);
            if (filteredLines.Count > 0)
                fullQuerySb.AppendLine(string.Join("\n", filteredLines));

            // === 以下保持原有的向量收集邏輯（不改動）===

            // 時間 - 向量化用
            if (contextSettings.IncludeTimeAndDate)
                builder.CollectTime(SemanticMapper.MapTimeToSemantic(gameData.Hour12HString));

            // 季節 - 向量化用
            if (contextSettings.IncludeSeason && pawn.Map != null)
                builder.CollectSeason(GenLocalDate.Season(pawn.Map));

            // 天氣 - 向量化用
            if (contextSettings.IncludeWeather && pawn.Map?.weatherManager?.curWeather != null)
                builder.CollectWeather(pawn.Map.weatherManager.curWeather);

            // 位置 - 向量化用
            var room = pawn.GetRoom();
            if (room is { PsychologicallyOutdoors: false } && room.Role != null)
                builder.CollectText(room.Role.label);

            // 溫度 - 向量化用
            if (contextSettings.IncludeLocationAndTemperature && pawn.Map != null)
            {
                float temp = pawn.Position.GetTemperature(pawn.Map);
                builder.CollectTemperature(temp);
            }

            // Surroundings - 向量化用
            if (contextSettings.IncludeSurroundings)
                builder.CollectSurrounding(ContextHelper.CollectNearbyItems(pawn, 1));

            // StatusActivities - 向量化用
            if (request.StatusActivities != null)
            {
                foreach (var activity in request.StatusActivities)
                    builder.CollectActivity(activity);
            }

            // StatusNames - 向量化用
            if (request.StatusNames != null)
                builder.AddNames(request.StatusNames);

            // DialogueContext - 向量化用
            if (!string.IsNullOrEmpty(request.DialogueType))
                builder.CollectDialogueType(request.DialogueType);

            // OngoingEvents - 向量化用
            if (ongoingEvents.Count > 0)
                foreach (var eventText in ongoingEvents)
                    builder.CollectText(eventText);

            // === 收集 Pawn 資料到快照 ===
            // [MOD] 新增動態 QueryText 基本參數
            var pawnData = new PawnSnapshotData
            {
                PawnId = pawn.thingIDNumber,
                Items = builder.GetCollectedItems(),
                Names = builder.GetAllNames(),
                PawnText = pawnText,
                QueryText = fullQuerySb.ToString(),  // [保留] 靜態 QueryText（降級用）
                                                     // [NEW] 動態 QueryText 基本參數
                CurrentAction = pawn.jobs?.curJob?.GetReport(pawn) ?? "",
                DialogueType = request.DialogueType ?? "",
            };
            snapshot.PawnData.Add(pawnData);

            allPawnTexts.AppendLine(pawnText);
            Cache.Get(pawn).Context = pawnText;
        }

        snapshot.KnowledgeSearchText = allPawnTexts.ToString() + "\n" + (request.Prompt ?? "");
        return snapshot;
    }

    // ===============================================================
    // [NEW] 階段二：後台執行緒解析向量並注入記憶
    // ===============================================================

    /// <summary>
    /// 後台執行緒解析 Context（批次向量計算 + 記憶檢索 + 記憶注入）
    /// [MODIFY] 使用預先收集的 QueryText
    /// </summary>
    public static async Task<string> ResolveContextAsync(ContextSnapshot snapshot, List<Pawn> pawns)
    {
        var pawnContexts = new StringBuilder();
        var allKnowledge = new HashSet<string>();

        // ===================================================================
        // 階段 1：批次向量計算（為所有 Pawn 計算 Context 向量）
        // ===================================================================
        var pawnVectorData = new List<(Pawn pawn, PawnSnapshotData data, List<float[]> vectors)>();

        foreach (var pawnData in snapshot.PawnData)
        {
            var pawn = pawns.FirstOrDefault(p => p.thingIDNumber == pawnData.PawnId);
            if (pawn == null) continue;

            // 批次向量計算（異步）
            var contextVectors = await ContextVectorDatabase.Instance.GetVectorsBatchAsync(pawnData.Items, isQuery: true);
            pawnVectorData.Add((pawn, pawnData, contextVectors));
        }

        // ===================================================================
        // 階段 2：記憶檢索（雲端 Reranker 並行 / 本地 Max-Sim）
        // ===================================================================
        Dictionary<Pawn, List<MemoryRecord>> memoriesDict;

        bool useCloudReranker = Settings.Get().UseCloudVectorService &&
                                !CloudRerankerClient.Instance.IsRateLimited;

        if (useCloudReranker && pawnVectorData.Count > 0)
        {
            // [MOD] 雲端模式：傳遞完整 PawnSnapshotData 供動態 QueryText 生成
            var pawnDataList = pawnVectorData.Select(p => (
                p.pawn,
                p.vectors,
                p.data.Names,
                p.data  // [MOD] 傳遞完整 PawnSnapshotData
            )).ToList();
            memoriesDict = await MemoryRetriever.GetRelevantMemoriesForMultiplePawnsAsync(pawnDataList);
        }
        else
        {
            // 本地模式：同步 Max-Sim
            memoriesDict = new Dictionary<Pawn, List<MemoryRecord>>();
            int pawnCount = pawnVectorData.Count;  // [NEW]
            foreach (var (pawn, data, vectors) in pawnVectorData)
            {
                var memories = MemoryRetriever.GetRelevantMemoriesBySemantic(vectors, pawn, data.Names, pawnCount);  // [MOD]
                memoriesDict[pawn] = memories;
            }
        }

        // ===================================================================
        // 階段 3：記憶注入 + 組合 Context（保持原有邏輯）
        // ===================================================================
        for (int i = 0; i < pawnVectorData.Count; i++)
        {
            var (pawn, pawnData, _) = pawnVectorData[i];

            var memories = memoriesDict.TryGetValue(pawn, out var mems) ? mems : new List<MemoryRecord>();

            string knowledgeSearchText = BuildKnowledgeSearchText(snapshot.KnowledgeSearchText, memories);
            var knowledge = MemoryRetriever.GetRelevantKnowledge(knowledgeSearchText);

            string memoryBlock = "";
            if (!memories.NullOrEmpty())
            {
                memoryBlock = MemoryFormatter.FormatRecalledMemories(memories);
            }
            string resolvedPawnText = pawnData.PawnText.Replace(
                "[[MEMORY_INJECTION_POINT]]", memoryBlock.TrimEnd());

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

        var fullContext = new StringBuilder();
        fullContext.AppendLine(Constant.GetInstruction(allKnowledge.ToList())).AppendLine();
        fullContext.Append(pawnContexts);
        return fullContext.ToString();
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
    /// [MOD] 新增 queryText 輸出，同步收集 Reranker 查詢文本
    private static (string text, ContextVectorBuilder builder, string queryText) CreatePawnContext(
        Pawn pawn,
        InfoLevel infoLevel = InfoLevel.Normal)
    {
        var sb = new StringBuilder();
        var querySb = new StringBuilder();  // [NEW] Reranker 查詢文本
        var builder = new ContextVectorBuilder();

        sb.Append(CreatePawnBackstory(pawn, infoLevel));

        // Health — 對記憶檢索非常重要
        string health = ContextBuilder.GetHealthContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, health);
        AppendIfNotEmpty(querySb, health);  // [NEW]

        // [NEW] 收集 Health 向量（事件性 Hediff）
        builder.CollectEventHediffs(pawn.health?.hediffSet?.hediffs);

        var personality = Cache.Get(pawn).Personality;
        if (personality != null)
        {
            sb.AppendLine($"Personality: {personality}");
        }

        // Stop here for invaders
        if (pawn.IsEnemy())
            return (sb.ToString(), builder, querySb.ToString());

        // Mood — 對記憶檢索重要
        string mood = ContextBuilder.GetMoodContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, mood);
        AppendIfNotEmpty(querySb, mood);  // [NEW]

        // [NEW] 收集 Mood 向量
        if (pawn.needs?.mood != null)
            builder.CollectMood(pawn.needs.mood.CurLevelPercentage);

        // Thoughts — 對記憶檢索非常重要
        string thoughts = ContextBuilder.GetThoughtsContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, thoughts);
        AppendIfNotEmpty(querySb, thoughts);  // [NEW]

        // [NEW] 收集 Thoughts 向量
        builder.CollectThoughts(ContextHelper.GetThoughts(pawn)?.Keys);

        // Prisoner/Slave status — 對記憶檢索重要
        string prisonerSlave = ContextBuilder.GetPrisonerSlaveContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, prisonerSlave);
        AppendIfNotEmpty(querySb, prisonerSlave);  // [NEW]

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

        // Visitor activity — 對記憶檢索有幫助
        if (pawn.IsVisitor())
        {
            var lord = pawn.GetLord() ?? pawn.CurJob?.lord;
            if (lord?.LordJob != null)
            {
                var cleanName = lord.LordJob.GetType().Name.Replace("LordJob_", "");
                sb.AppendLine($"Activity: {cleanName}");
                querySb.AppendLine($"Activity: {cleanName}");  // [NEW]
                // [NEW] 收集 Activity 向量
                builder.CollectText(cleanName);
            }
        }

        // Relations — 對記憶檢索非常重要
        string relations = ContextBuilder.GetRelationsContext(pawn, infoLevel);
        AppendIfNotEmpty(sb, relations);
        AppendIfNotEmpty(querySb, relations);  // [NEW]
        // [NEW] 收集 Relations 人名和關係詞
        builder.CollectRelations(relations);

        // Memory injection point
        sb.AppendLine("[[MEMORY_INJECTION_POINT]]");

        if (infoLevel != InfoLevel.Short)
            AppendIfNotEmpty(sb, ContextBuilder.GetEquipmentContext(pawn, infoLevel));

        return (sb.ToString(), builder, querySb.ToString());
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
        // [MOD] 收集 dialogueType
        string dialogueType = ContextBuilder.BuildDialogueType(sb, talkRequest, pawns, shortName, mainPawn);

        // [NEW] 存入 TalkRequest 供後續使用
        talkRequest.DialogueType = dialogueType;

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
    /// 組合常識搜索文本（加入記憶摘要和關鍵詞）
    /// 擴展搜索範圍，讓常識能匹配記憶中提及的主題
    /// </summary>
    private static string BuildKnowledgeSearchText(string baseText, List<MemoryRecord> memories)
    {
        if (memories.NullOrEmpty())
            return baseText;

        var sb = new StringBuilder(baseText);
        sb.AppendLine();

        foreach (var m in memories)
        {
            // 加入記憶摘要
            if (!string.IsNullOrEmpty(m.Summary))
                sb.AppendLine(m.Summary);

            // 加入記憶關鍵詞（關鍵詞匹配的核心）
            if (!m.Keywords.NullOrEmpty())
                sb.AppendLine(string.Join(" ", m.Keywords));
        }

        return sb.ToString();
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
