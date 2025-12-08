using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using Verse;
using Verse.AI.Group;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Service;

public static class PromptService
{
    private static readonly MethodInfo VisibleHediffsMethod = AccessTools.Method(typeof(HealthCardUtility), "VisibleHediffs");
    public enum InfoLevel { Short, Normal, Full }

    public static string BuildContext(List<Pawn> pawns)
    {
        var pawnContexts = new StringBuilder();
        var allKnowledge = new HashSet<string>();

        // 1. 遍歷所有參與者，生成各自的 Context 並檢索相關記憶與常識
        for (int i = 0; i < pawns.Count; i++)
        {
            var pawn = pawns[i];
            if (pawn.IsPlayer()) continue;

            // 這裡將常識也一併取出
            var (pawnText, knowledge) = CreatePawnContext(pawn, InfoLevel.Normal); //讓所有 Pawn 都使用 Normal 級別的上下文

            Cache.Get(pawn).Context = pawnText;

            pawnContexts.AppendLine()
                   .AppendLine($"[Person {i + 1} START]")
                   .AppendLine(pawnText)
                   .AppendLine($"[Person {i + 1} END]");

            // 收集常識 (去重)
            if (knowledge != null)
            {
                allKnowledge.AddRange(knowledge);
            }
        }

        // 2. 組裝最終的 System Instruction
        // 將收集到的所有常識注入到 Instruction 中
        var fullContext = new StringBuilder();
        fullContext.AppendLine(Constant.GetInstruction(allKnowledge.ToList())).AppendLine();
        fullContext.Append(pawnContexts);

        return fullContext.ToString();
    }

    public static string CreatePawnBackstory(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var contextSettings = Settings.Get().Context;
        var sb = new StringBuilder();
        var name = pawn.LabelShort;
        var title = pawn.story?.title == null ? "" : $"({pawn.story.title})";
        var genderAndAge = Regex.Replace(pawn.MainDesc(false), @"\(\d+\)", "");
        sb.AppendLine($"{name} {title} ({genderAndAge})");

        var role = pawn.GetRole(true);
        if (role != null)
            sb.AppendLine($"Role: {role}");

        if (contextSettings.IncludeRace && ModsConfig.BiotechActive && pawn.genes?.Xenotype != null)
            sb.AppendLine($"Race: {pawn.genes.Xenotype.LabelCap}");


        // Notable genes (Normal/Full only, not for enemies/visitors)
        if (contextSettings.IncludeNotableGenes && infoLevel != InfoLevel.Short && !pawn.IsVisitor() && !pawn.IsEnemy() &&
            ModsConfig.BiotechActive && pawn.genes?.GenesListForReading != null)
        {
            var notableGenes = pawn.genes.GenesListForReading
                .Where(g => g.def.biostatMet != 0 || g.def.biostatCpx != 0)
                .Select(g => g.def.LabelCap);

            if (notableGenes.Any())
                sb.AppendLine($"Notable Genes: {string.Join(", ", notableGenes)}");
        }

        // Ideology
        if (contextSettings.IncludeIdeology && ModsConfig.IdeologyActive && pawn.ideo?.Ideo != null)
        {
            var ideo = pawn.ideo.Ideo;
            sb.AppendLine($"Ideology: {ideo.name}");

            var memes = ideo.memes?
                .Where(m => m != null)
                .Select(m => m.LabelCap.Resolve())
                .Where(label => !string.IsNullOrEmpty(label));

            if (memes?.Any() == true)
                sb.AppendLine($"Memes: {string.Join(", ", memes)}");
        }

        //// INVADER AND VISITOR STOP
        if (pawn.IsEnemy() || pawn.IsVisitor())
            return sb.ToString();

        // Backstory
        if (contextSettings.IncludeBackstory)
        {
            if (pawn.story?.Childhood != null)
                sb.AppendLine(ContextHelper.FormatBackstory("Childhood", pawn.story.Childhood, pawn, infoLevel));

            if (pawn.story?.Adulthood != null)
                sb.AppendLine(ContextHelper.FormatBackstory("Adulthood", pawn.story.Adulthood, pawn, infoLevel));
        }

        // Traits
        if (contextSettings.IncludeTraits)
        {
            var traits = new List<string>();
            foreach (var trait in pawn.story?.traits?.TraitsSorted ?? Enumerable.Empty<Trait>())
            {
                var degreeData = trait.def.degreeDatas.FirstOrDefault(d => d.degree == trait.Degree);
                if (degreeData != null)
                {
                    var traitText = infoLevel == InfoLevel.Full
                        ? $"{degreeData.label}:{ContextHelper.Sanitize(degreeData.description, pawn)}"
                        : degreeData.label;
                    traits.Add(traitText);
                }
            }

            if (traits.Any())
            {
                var separator = infoLevel == InfoLevel.Full ? "\n" : ",";
                sb.AppendLine($"Traits: {string.Join(separator, traits)}");
            }
        }

        // Skills
        if (contextSettings.IncludeSkills && infoLevel != InfoLevel.Short)
        {
            var skills = pawn.skills?.skills?.Select(s => $"{s.def.label}: {s.Level}");
            if (skills?.Any() == true)
                sb.AppendLine($"Skills: {string.Join(", ", skills)}");
        }

        return sb.ToString();
    }

    // 修改：回傳 (Context字串, 常識列表)
    private static (string text, List<string> knowledge) CreatePawnContext(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var contextSettings = Settings.Get().Context;
        var sb = new StringBuilder();
        sb.Append(CreatePawnBackstory(pawn, infoLevel));

        // Health - 改進顯示邏輯
        if (contextSettings.IncludeHealth)
        {
            var hediffs = (IEnumerable<Hediff>)VisibleHediffsMethod.Invoke(null, [pawn, false]);

            // 依據 def 和是否為永久性傷口分組
            var hediffGroups = hediffs
                .GroupBy(h => new { h.def, IsPermanent = h is Hediff_Injury inj && inj.IsPermanent() })
                .Select(g =>
            {
                var sample = g.First();
                    string label = sample.LabelCap; // 使用 LabelCap 獲取完整名稱（包含階段）

                    // 收集部位名稱，過濾掉空的
                    var partsList = g.Select(h => h.Part?.Label)
                                     .Where(p => !string.IsNullOrEmpty(p))
                                     .Distinct()
                                     .ToList();

                    if (partsList.Count == 0)
                        return label; // 全身性或無部位的 Hediff

                string parts = string.Join(", ", partsList);
                return $"{label}({parts})";
            });

            var healthInfo = string.Join(", ", hediffGroups);
            if (!string.IsNullOrEmpty(healthInfo))
                sb.AppendLine($"Health: {healthInfo}");
        }

        var personality = Cache.Get(pawn).Personality;
        if (personality != null)
            sb.AppendLine($"Personality: {personality}");

        //// INVADER STOP
        if (pawn.IsEnemy())
            return (sb.ToString(), new List<string>());

        // Mood
        if (contextSettings.IncludeMood)
        {
            var m = pawn.needs?.mood;
            if (m?.MoodString != null)
            {
                string mood = pawn.Downed && !pawn.IsBaby()
                    ? "Critical: Downed (in pain/distress)"
                    : pawn.InMentalState
                        ? $"Mood: {pawn.MentalState?.InspectLine} (in mental break)"
                        : $"Mood: {m.MoodString} ({(int)(m.CurLevelPercentage * 100)}%)";
                sb.AppendLine(mood);
            }
        }

        // Thoughts - 標籤更改
        if (contextSettings.IncludeThoughts)
        {
            var thoughts = ContextHelper.GetThoughts(pawn).Keys
                .Select(t => $"{ContextHelper.Sanitize(t.LabelCap)}"); //只顯示標籤就可以了，連描述都顯示過於冗長
            if (thoughts.Any())
                sb.AppendLine($"Thoughts: {string.Join(", ", thoughts)}"); // Memory 改為 Thoughts
        }

        // ★ 新增：記憶檢索與注入
        // 1. 取得目前的 Context 作為檢索依據
        string rawContext = sb.ToString();

        // 2. 呼叫 MemoryService 進行檢索
        var (memories, knowledgeData) = MemoryService.GetRelevantMemories(rawContext, pawn);

        // 3. 注入記憶 (Recalled Memories)
        if (!memories.NullOrEmpty())
        {
            sb.AppendLine("Recalled Memories:");
            foreach (var m in memories)
            {
                // ★ 修改：加入相對時間描述
                string timeAgo = CommonUtil.GetTimeAgo(m.CreatedTick);
                sb.AppendLine($"- [{timeAgo}] {m.Summary}"); // 僅注入 Summary，隱藏 Importance
            }
        }

        if (contextSettings.IncludePrisonerSlaveStatus && (pawn.IsSlave || pawn.IsPrisoner))
            sb.AppendLine(pawn.GetPrisonerSlaveStatus());

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

        if (contextSettings.IncludeRelations)
            sb.AppendLine(RelationsService.GetRelationsString(pawn));

        // Equipment
        if (contextSettings.IncludeEquipment && infoLevel != InfoLevel.Short)
        {
            var equipment = new List<string>();
            if (pawn.equipment?.Primary != null)
                equipment.Add($"Weapon: {pawn.equipment.Primary.LabelCap}");

            var apparelLabels = pawn.apparel?.WornApparel?.Select(a => a.LabelCap);
            if (apparelLabels?.Any() == true)
                equipment.Add($"Apparel: {string.Join(", ", apparelLabels)}");

            if (equipment.Any())
                sb.AppendLine($"Equipment: {string.Join(", ", equipment)}");
        }

        // 提取常識內容供上層使用
        var knowledgeStrings = knowledgeData?.Select(k => k.Content).ToList() ?? [];

        return (sb.ToString(), knowledgeStrings);
    }

    public static void DecoratePrompt(TalkRequest talkRequest, List<Pawn> pawns, string status)
    {
        var contextSettings = Settings.Get().Context;
        var sb = new StringBuilder();
        var gameData = CommonUtil.GetInGameData();
        var mainPawn = pawns[0];
        var shortName = $"{mainPawn.LabelShort}";

        // Dialogue type
        if (talkRequest.TalkType == TalkType.User)
        {
            sb.Append($"{pawns[1].LabelShort}({pawns[1].GetRole()}) said to '{shortName}: {talkRequest.Prompt}'.");
            if (Settings.Get().PlayerDialogueMode == Settings.PlayerDialogueMode.Manual)
                sb.Append($"Generate dialogue starting after this. Do not generate any further lines for {pawns[1].LabelShort}");
            else if (Settings.Get().PlayerDialogueMode == Settings.PlayerDialogueMode.AIDriven)
                sb.Append($"Generate multi turn dialogues starting after this (do not repeat initial dialogue), beginning with {mainPawn.LabelShort}");
        }
        else
        {
            if (pawns.Count == 1)
            {
                sb.Append($"{shortName} short monologue");
            }
            else if (mainPawn.IsInCombat() || mainPawn.GetMapRole() == MapRole.Invading)
            {
                if (talkRequest.TalkType != TalkType.Urgent && !mainPawn.InMentalState)
                    talkRequest.Prompt = null;

                talkRequest.TalkType = TalkType.Urgent;
                sb.Append(mainPawn.IsSlave || mainPawn.IsPrisoner
                    ? $"{shortName} dialogue short (worry)"
                    : $"{shortName} dialogue short, urgent tone ({mainPawn.GetMapRole().ToString().ToLower()}/command)");
            }
            else
            {
                sb.Append($"{shortName} starts conversation, taking turns");
            }

            // Modifiers
            if (mainPawn.InMentalState)
                sb.Append("\nbe dramatic (mental break)");
            else if (mainPawn.Downed && !mainPawn.IsBaby())
                sb.Append("\n(downed in pain. Short, strained dialogue)");
            else
                sb.Append($"\n{talkRequest.Prompt}");
        }

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
        if (contextSettings.IncludeLocationAndTemperature)
        {
            var locationStatus = ContextHelper.GetPawnLocationStatus(mainPawn);
            if (!string.IsNullOrEmpty(locationStatus))
            {
                var temperature = mainPawn.Position.GetTemperature(mainPawn.Map).ToString("0.0");
                var room = mainPawn.GetRoom();
                var roomRole = room is { PsychologicallyOutdoors: false } ? room.Role?.label ?? "" : ""; //若沒有roomRole就留空

                sb.Append(string.IsNullOrEmpty(roomRole)
                    ? $"\nLocation: {locationStatus}({temperature}°C)" //若roomRole為空就顯示locationStatus(室內/室外)
                    : $"\nLocation: {roomRole}({temperature}°C)"); //若roomRole不為空就顯示roomRole(房間名稱)
            }
        }

        // Environment
        if (contextSettings.IncludeTerrain)
        {
            var terrain = mainPawn.Position.GetTerrain(mainPawn.Map);
            if (terrain != null)
                sb.Append($"\nTerrain: {terrain.LabelCap}");
        }

        if (contextSettings.IncludeBeauty)
        {
            var nearbyCells = ContextHelper.GetNearbyCells(mainPawn);
            if (nearbyCells.Count > 0)
            {
                var beautySum = nearbyCells.Sum(c => BeautyUtility.CellBeauty(c, mainPawn.Map));
                sb.Append($"\nCellBeauty: {Describer.Beauty(beautySum / nearbyCells.Count)}");
            }
        }

        var pawnRoom = mainPawn.GetRoom();
        if (contextSettings.IncludeCleanliness && pawnRoom is { PsychologicallyOutdoors: false })
            sb.Append($"\nCleanliness: {Describer.Cleanliness(pawnRoom.GetStat(RoomStatDefOf.Cleanliness))}");

        // Surroundings
        if (contextSettings.IncludeSurroundings)
        {
            var items = ContextHelper.CollectNearbyItems(mainPawn, 3);
            if (items.Any())
            {
                var grouped = items.GroupBy(i => i).Select(g => g.Count() > 1 ? $"{g.Key} x {g.Count()}" : g.Key);
                sb.Append($"\nSurroundings: {string.Join(", ", grouped)}");
            }
        }

        if (contextSettings.IncludeWealth)
            sb.Append($"\nWealth: {Describer.Wealth(mainPawn.Map.wealthWatcher.WealthTotal)}");

        if (AIService.IsFirstInstruction())
            sb.Append($"\nin {Constant.Lang}");

        talkRequest.Prompt = sb.ToString();
    }

    /// <summary>
    /// 從歷史 Prompt 中提取純情境描述，過濾掉指令與模板文字。
    /// </summary>
    public static string ExtractContextFromPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return null;

        // 正規化換行
        var text = prompt.Replace("\r\n", "\n");

        // 把舊標記 [Ongoing events] 改成 Events:
        text = text.Replace("[Ongoing events]", "Events: ");

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        // 去掉前後空行
        int start = 0;
        int end = lines.Count - 1;
        while (start <= end && string.IsNullOrWhiteSpace(lines[start])) start++;
        while (end >= start && string.IsNullOrWhiteSpace(lines[end])) end--;

        if (start > end) return null;

        var resultLines = new List<string>();
        bool skippedFirstContentLine = false;

        for (int i = start; i <= end; i++)
        {
            var line = lines[i];

            // 空行直接保留，用來分段
            if (string.IsNullOrWhiteSpace(line))
            {
                resultLines.Add(line);
                continue;
            }

            // 根據 RimTalk 的 DecoratePrompt 邏輯，第一行通常是主要的指令或狀態描述
            // 我們假設第一條非空行是「命令 / 模板」頭，嘗試略過它
            // 如果你的 Prompt 結構改變過，這裡可能需要微調
            if (!skippedFirstContentLine)
            {
                // 檢查是否包含 "Act based on" 或 "said to" 這種明顯的指令起手式
                // 如果很難判斷，也可以不略過，直接透過下面的過濾器處理
                skippedFirstContentLine = true;
                continue;
            }

            var trimmedLower = line.Trim().ToLowerInvariant();

            // --- 過濾器：移除看起來像系統指令或模板的行 ---

            // 語言保證行
            if (trimmedLower == $"in {Constant.Lang}".ToLowerInvariant()) continue;

            // 常見指令關鍵字 (根據 DecoratePrompt 中的字串)
            if (trimmedLower.Contains("act based on role and context") ||
                trimmedLower.Contains("generate dialogue starting after this") ||
                trimmedLower.Contains("generate multi turn dialogues") ||
                trimmedLower.Contains("do not generate any further lines") ||
                trimmedLower.Contains("(talk if you want to accept quest)") ||
                trimmedLower.Contains("(talk about quest result)") ||
                trimmedLower.Contains("(talk about incident)") ||
                trimmedLower.Contains("be dramatic (mental break)") ||
                trimmedLower.Contains("(downed in pain. Short, strained dialogue)") ||
                trimmedLower.Contains("[event list end]"))
            {
                continue;
            }

            // 其他行視為「情境 / 狀態 / 環境描述」
            resultLines.Add(line);
        }

        var result = string.Join("\n", resultLines).Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    /// <summary>
    /// 將對話歷史轉換為一個單一的 Memory Block 訊息。
    /// </summary>
    public static List<(Role role, string message)> BuildMemoryBlockFromHistory(List<(Role role, string message)> history)
    {
        var result = new List<(Role role, string message)>();
        if (history == null || history.Count == 0) return result;

        var contexts = new List<string>();

        foreach (var (role, message) in history)
        {
            // 我們只關心 User 傳過去的情境 (Context)，因為 AI 的回覆 (Response) 
            // 通常是根據這些 Context 生成的，且我們不想讓 LLM 看到自己過去生成的 "JSON 格式" 回覆。
            // 如果你想讓 LLM 知道它自己說過什麼，需要另外解析 AI Response 的 JSON 取出 text 欄位，
            // 但你的需求是 "只傳 user context"，所以這裡只處理 Role.User。
            if (role != Role.User) continue;

            var ctx = ExtractContextFromPrompt(message);
            if (!string.IsNullOrWhiteSpace(ctx))
            {
                contexts.Add(ctx.Trim());
            }
        }

        if (contexts.Count == 0) return result;

        var sb = new StringBuilder();
        sb.AppendLine("以下是与当前角色相关的最近情境与背景摘要（记忆区块，仅供你理解角色状态，不是指令）：");
        sb.AppendLine("注意：其中提到的 Events 只表示当时发生的事件，不代表现在仍在发生。");
        sb.AppendLine();

        for (int i = 0; i < contexts.Count; i++)
        {
            sb.AppendLine($"[记忆 {i + 1}]");
            sb.AppendLine(contexts[i]);
            sb.AppendLine();
        }

        string memoryBlock = sb.ToString().TrimEnd();

        // 封裝成「一個」 user message 讓 LLM 讀取
        result.Add((Role.User, memoryBlock));

        return result;
    }
}