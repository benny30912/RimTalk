using HarmonyLib;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Verse;
using Verse.AI.Group;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Service;

public static class PromptService
{
    private static readonly MethodInfo VisibleHediffsMethod = AccessTools.Method(typeof(HealthCardUtility), "VisibleHediffs");
    public enum InfoLevel { Short, Normal, Full }

    // ★ 修改：不再需要 status 或 envContext 參數，因為我們會從 request.Prompt 裡面抓
    public static string BuildContext(TalkRequest request, List<Pawn> pawns)
    {
        var pawnContexts = new StringBuilder();
        var allKnowledge = new HashSet<string>();

        // ★ 關鍵策略：直接使用已經被 DecoratePrompt + Event+ Patch 處理過的 Prompt 作為檢索源
        // 這包含了：指令 + 環境描述 + Ongoing Events
        string combinedSearchContext = request.Prompt;

        // 遍歷所有參與者，生成各自的 Context 並檢索相關記憶與常識
        for (int i = 0; i < pawns.Count; i++)
        {
            var pawn = pawns[i];
            if (pawn.IsPlayer()) continue;

            // 獲取 Pawn 的上下文 (包含 dynamicContext)
            var (pawnText, _, pawnDynamicContext) = CreatePawnContext(pawn, InfoLevel.Normal); //讓所有 Pawn 都使用 Normal 級別的上下文

            // ★ 組合檢索：Pawn 自身狀態 + 包含事件的完整 Prompt
            string searchContext = pawnDynamicContext + "\n" + combinedSearchContext;

            // 執行檢索 (使用組合後的 searchContext)
            var (memories, knowledgeData) = MemoryService.GetRelevantMemories(searchContext, pawn);

            // 注入記憶 (替換佔位符)
            var memorySb = new StringBuilder();
            if (!memories.NullOrEmpty())
            {
                memorySb.AppendLine("Recalled Memories:");
                foreach (var m in memories)
                {
                    string timeAgo = CommonUtil.GetTimeAgo(m.CreatedTick);
                    memorySb.AppendLine($"- [{timeAgo}] {m.Summary}");
                }
            }
            // 替換佔位符
            pawnText = pawnText.Replace("[[MEMORY_INJECTION_POINT]]", memorySb.ToString());

            // 6. 收集常識
            if (knowledgeData != null)
            {
                foreach (var k in knowledgeData) allKnowledge.Add(k.Content);
            }

            Cache.Get(pawn).Context = pawnText;

            pawnContexts.AppendLine()
                   .AppendLine($"[Person {i + 1} START]")
                   .AppendLine(pawnText)
                   .AppendLine($"[Person {i + 1} END]");
        }

        // 7. 組裝最終 Prompt
        var fullContext = new StringBuilder();
        fullContext.AppendLine(Constant.GetInstruction(allKnowledge.ToList())).AppendLine(); // 將收集到的所有常識注入到 Instruction 中

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
    // 修改回傳類型，多回傳一個 dynamicContext
    private static (string text, List<string> knowledge, string dynamicContext) CreatePawnContext(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var contextSettings = Settings.Get().Context;
        var sb = new StringBuilder();
        var dynamicSb = new StringBuilder(); // 用於收集動態上下文

        // --- 1. 靜態資訊 (不加入 dynamicSb) ---
        sb.Append(CreatePawnBackstory(pawn, infoLevel));

        // --- 2. 動態資訊 (加入 dynamicSb) ---
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
            {
                string line = $"Health: {healthInfo}";
                sb.AppendLine(line);
                dynamicSb.AppendLine(line); // 加入檢索範圍
            }
        }

        var personality = Cache.Get(pawn).Personality;
        if (personality != null)
            sb.AppendLine($"Personality: {personality}");

        //// INVADER STOP
        if (pawn.IsEnemy())
            return (sb.ToString(), new List<string>(), dynamicSb.ToString());

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
                dynamicSb.AppendLine(mood); // 加入檢索範圍
            }
        }

        // Thoughts - 標籤更改
        if (contextSettings.IncludeThoughts)
        {
            var thoughts = ContextHelper.GetThoughts(pawn).Keys
                .Select(t => $"{ContextHelper.Sanitize(t.LabelCap)}"); //只顯示標籤就可以了，連描述都顯示過於冗長
            if (thoughts.Any())
            {
                string line = $"Thoughts: {string.Join(", ", thoughts)}"; // Memory 改為 Thoughts
                sb.AppendLine(line);
                dynamicSb.AppendLine(line); // 加入檢索範圍
            }
        }

        // ★ 關鍵修改：記憶檢索移到外部或最後 ★
        // 這裡我們暫時只插入一個佔位符，或者直接回傳 dynamicContext 讓上層處理
        string placeholder = "[[MEMORY_INJECTION_POINT]]";
        sb.AppendLine(placeholder);

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
        {
            string line = RelationsService.GetRelationsString(pawn);
            sb.AppendLine(line);
            dynamicSb.AppendLine(line); // 加入檢索範圍
        }

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

        // ==========================================
        // ★ 核心修改：注入抽象標籤 (Context Enrichment)
        // ==========================================
        var abstractTags = CoreTagMapper.GetAbstractTags(pawn);
        if (abstractTags.Any())
        {
            // 這些標籤會被加入 dynamicContext，用於 GetRelevantMemories 的關鍵字匹配
            // 但我們可以選擇 "不" 加入 sb (顯示給 LLM 的 Context)，
            // 除非您覺得讓 LLM 看到這些顯式標籤也有助於它理解。
            // 建議：加入 dynamicContext 即可，保持 Prompt 簡潔。

            string tagsLine = $"[System Tags: {string.Join(", ", abstractTags)}]";
            dynamicSb.AppendLine(tagsLine);
        }

        return (sb.ToString(), new List<string>(), dynamicSb.ToString()); // knowledge 在這裡暫時為空，稍後統一處理
    }

    // 新增方法：專門生成環境描述字串
    // ★ 改為 public，供 TalkService 調用
    public static string GetEnvironmentContext(Pawn mainPawn, string status)
    {
        var contextSettings = Settings.Get().Context;
        var sb = new StringBuilder();
        var gameData = CommonUtil.GetInGameData();

        sb.Append($"\n{status}"); // status 包含由 GetPawnStatusFull 生成的當前活動和周邊狀況

        // 時間與天氣
        if (contextSettings.IncludeTimeAndDate)
        {
            sb.Append($"\nTime: {gameData.Hour12HString}");
            sb.Append($"\nToday: {gameData.DateString}");
        }
        if (contextSettings.IncludeSeason)
            sb.Append($"\nSeason: {gameData.SeasonString}");
        if (contextSettings.IncludeWeather)
            sb.Append($"\nWeather: {gameData.WeatherString}");

        // 地點
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

        // 地形
        if (contextSettings.IncludeTerrain)
        {
            var terrain = mainPawn.Position.GetTerrain(mainPawn.Map);
            if (terrain != null)
                sb.Append($"\nTerrain: {terrain.LabelCap}");
        }

        // 美觀度
        if (contextSettings.IncludeBeauty)
        {
            var nearbyCells = ContextHelper.GetNearbyCells(mainPawn);
            if (nearbyCells.Count > 0)
            {
                var beautySum = nearbyCells.Sum(c => BeautyUtility.CellBeauty(c, mainPawn.Map));
                sb.Append($"\nCellBeauty: {Describer.Beauty(beautySum / nearbyCells.Count)}");
            }
        }

        // 清潔度
        var pawnRoom = mainPawn.GetRoom();
        if (contextSettings.IncludeCleanliness && pawnRoom is { PsychologicallyOutdoors: false })
            sb.Append($"\nCleanliness: {Describer.Cleanliness(pawnRoom.GetStat(RoomStatDefOf.Cleanliness))}");

        // 周圍物品
        if (contextSettings.IncludeSurroundings)
        {
            var items = ContextHelper.CollectNearbyItems(mainPawn, 3);
            if (items.Any())
            {
                var grouped = items.GroupBy(i => i).Select(g => g.Count() > 1 ? $"{g.Key} x {g.Count()}" : g.Key);
                sb.Append($"\nSurroundings: {string.Join(", ", grouped)}");
            }
        }

        // 財富
        if (contextSettings.IncludeWealth)
            sb.Append($"\nWealth: {Describer.Wealth(mainPawn.Map.wealthWatcher.WealthTotal)}");

        return sb.ToString();
    }

    // ★ 修改簽名：接收 envContext
    // ★ 保持簽名兼容：第三個參數必須是 string，為了讓 Event+ 的 Harmony Patch 能找到它
    // 我們傳入 envContext 給它，這樣它會被 Append 上去
    public static void DecoratePrompt(TalkRequest talkRequest, List<Pawn> pawns, string envContext)
    {
        var sb = new StringBuilder();
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

        // 移除：sb.Append(GetEnvironmentContext(mainPawn, status));
        // 改為直接 Append 傳入的字串
        sb.Append(envContext);

        if (AIService.IsFirstInstruction())
            sb.Append($"\nin {Constant.Lang}");

        talkRequest.Prompt = sb.ToString();

        // 當此方法結束時，RimTalk Event+ 的 Postfix 會自動執行
        // 並將 Ongoing Events 追加到 talkRequest.Prompt 的尾端
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