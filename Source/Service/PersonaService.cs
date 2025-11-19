using RimTalk.Client;
using RimTalk.Service;
using RimTalk.Util;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace RimTalk.Data;

public static class PersonaService
{
    static PersonaService()
    {
        TalkHistory.OnHistoryBatchReady += OnHistoryBatchReady;
    }

    private static bool AutoUpdateEnabled =>
        Settings.Get().EnableAutoPersonalityUpdate; // 你可以加一個設定開關

    public static string GetPersonality(Pawn pawn)
    {
        return Hediff_Persona.GetOrAddNew(pawn).Personality;
    }

    public static void SetPersonality(Pawn pawn, string personality)
    {
        Hediff_Persona.GetOrAddNew(pawn).Personality = personality;
    }

    public static float GetTalkInitiationWeight(Pawn pawn)
    {
        return Hediff_Persona.GetOrAddNew(pawn).TalkInitiationWeight;
    }

    public static void SetTalkInitiationWeight(Pawn pawn, float frequency)
    {
        Hediff_Persona.GetOrAddNew(pawn).TalkInitiationWeight = frequency;
    }

    public static async Task<PersonalityData> GeneratePersona(Pawn pawn)
    {
        string pawnBackstory = PromptService.CreatePawnBackstory(pawn, PromptService.InfoLevel.Full);

        try
        {
            AIService.UpdateContext($"[Character]\n{pawnBackstory}");
            var request = new TalkRequest(Constant.PersonaGenInstruction, pawn);
            PersonalityData personalityData = await AIService.Query<PersonalityData>(request);

            if (personalityData?.Persona != null)
            {
                personalityData.Persona = personalityData.Persona.Replace("**", "").Trim();
            }

            return personalityData;
        }
        catch (Exception e)
        {
            Logger.Error(e.Message);
            return null;
        }
    }

    private static async void OnHistoryBatchReady(Pawn pawn, List<(Role role, string message)> batch)
    {
        try
        {
            if (!AutoUpdateEnabled) return;
            if (pawn == null || pawn.Dead || pawn.Destroyed) return;
            if (batch == null || batch.Count == 0) return;

            // 目前人格全文
            string currentPersona = GetPersonality(pawn);
            if (string.IsNullOrWhiteSpace(currentPersona)) return;

            // 拆成「人格主體」＋「最後一段長期記憶（可空）」
            SplitPersonality(currentPersona, out var corePersonaHead, out var longTermMemory, out var corePersonaTail);

            // ====== 先選好這次要用的 API client + config ======
            var settings = Settings.Get();
            IAIClient client;
            ApiConfig usedConfig = null;

            if (settings.UseSimpleConfig)
            {
                // 基本模式：複製主系統現在的選擇邏輯
                if (settings.UseCloudProviders && settings.CloudConfigs != null && settings.CloudConfigs.Count > 0)
                {
                    var idx = settings.CurrentCloudConfigIndex;
                    if (idx < 0 || idx >= settings.CloudConfigs.Count)
                    {
                        idx = 0;
                        settings.CurrentCloudConfigIndex = 0;
                    }

                    usedConfig = settings.CloudConfigs[idx];
                }
                else
                {
                    usedConfig = settings.LocalConfig;
                }

                client = AIClientFactory.GetAIClient();
            }
            else
            {
                // 進階模式：使用列表中的最後一個 API
                if (settings.UseCloudProviders && settings.CloudConfigs != null && settings.CloudConfigs.Count > 0)
                {
                    usedConfig = settings.CloudConfigs[settings.CloudConfigs.Count - 1];
                }
                else
                {
                    usedConfig = settings.LocalConfig;
                }

                if (usedConfig == null || !usedConfig.IsValid())
                {
                    return;
                }

                client = AIClientFactory.GetAIClientForConfig(usedConfig);
            }

            if (client == null) return;

            // ======================================================
            // Step 1：把這批 MessageHistory 濃縮成「短期記憶」
            // ======================================================
            var shortSb = new System.Text.StringBuilder();
            shortSb.AppendLine($"以下是 {pawn.LabelShortCap} 最近的一些对话与互动记录。");
            shortSb.AppendLine("请你将这些内容整理成一段简短的「短期记忆」，用于描述近期发生的重要事件、情绪变化、人际关系与心态。");
            shortSb.AppendLine("要求：");
            shortSb.AppendLine("1. 提炼地点、人物、事件，将相似事件合并，标注频率。");
            shortSb.AppendLine("2. 只描述事实与心理，不要加入系统说明或指令。");
            shortSb.AppendLine($"3. 请始终以 {pawn.LabelShortCap} 的主观视角来理解。");
            shortSb.AppendLine("4. 控制在100字以内。");
            shortSb.AppendLine("5. 只输出短期记忆内容本身，不要加任何标题或额外说明。");
            shortSb.AppendLine();
            shortSb.AppendLine("最近互动记录：");

            foreach (var (role, msg) in batch)
            {
                if (string.IsNullOrWhiteSpace(msg)) continue;
                string prefix = role == Role.User ? "[情境]" : "[对话]";
                shortSb.AppendLine($"{prefix} {msg}");
            }

            string shortTermSystemInstruction =
                "你是一个帮忙整理角色记忆的助手。你的任务是根据提供的对话与事件记录，" +
                "写出一段可作为角色「短期记忆」的总结文字。";

            var shortPayload = await client.GetChatCompletionAsync(
                shortTermSystemInstruction,
                new List<(Role role, string message)>
                {
                (Role.User, shortSb.ToString())
                });

            if (shortPayload == null || string.IsNullOrWhiteSpace(shortPayload.Response))
            {
                Messages.Message(
                    $"[RimTalk] {pawn.LabelShortCap} 的人格更新失败：短期记忆生成失败。",
                    MessageTypeDefOf.RejectInput);
                return;
            }

            // 短期記憶用完即丟，不另存，只傳給下一步
            string shortMemory = shortPayload.Response.Trim();

            // ======================================================
            // Step 2：用「短期記憶 + 舊的長期記憶」生成新的長期記憶
            // ======================================================
            var archiveSb = new System.Text.StringBuilder();
            archiveSb.AppendLine($"下面是 {pawn.LabelShortCap} 的人格描述（不包含记忆部分）：");
            archiveSb.AppendLine(corePersonaHead);
            archiveSb.AppendLine();
            archiveSb.AppendLine(corePersonaTail);
            archiveSb.AppendLine();
            archiveSb.AppendLine($"下面是 {pawn.LabelShortCap} 目前的「长期记忆」摘要（可能为空）：");
            archiveSb.AppendLine(string.IsNullOrWhiteSpace(longTermMemory)
                ? "（目前没有长期记忆记录。）"
                : longTermMemory);
            archiveSb.AppendLine();
            archiveSb.AppendLine($"下面是 {pawn.LabelShortCap} 近期的「短期记忆」：");
            archiveSb.AppendLine(shortMemory);
            archiveSb.AppendLine();
            archiveSb.AppendLine("请在不改变角色人格基调的前提下，将新的短期记忆整合进长期记忆，生成一段更新后的「长期记忆」。");
            archiveSb.AppendLine("要求：");
            archiveSb.AppendLine("1. 保留以往长期记忆中仍然重要的部分。");
            archiveSb.AppendLine("2. 将这次短期记忆中，可能持续影响角色的体验与情绪浓缩进去。");
            archiveSb.AppendLine("3. 总结重要里程碑事件和转折点，合并相似经历，突出长期趋势。");
            archiveSb.AppendLine($"4. 请始终以 {pawn.LabelShortCap} 的主观视角来理解。");
            archiveSb.AppendLine("5. 控制在80字以内。");
            archiveSb.AppendLine("6. 只输出更新后的长期记忆内容本身，不要加标题或任何额外说明。");

            string archiveSystemInstruction =
                "你是一个帮忙管理角色记忆档案的助手。根据人格描述、旧的长期记忆和新的短期记忆，" +
                "生成一份更新后的长期记忆摘要。";

            var archivePayload = await client.GetChatCompletionAsync(
                archiveSystemInstruction,
                new List<(Role role, string message)>
                {
                (Role.User, archiveSb.ToString())
                });

            if (archivePayload == null || string.IsNullOrWhiteSpace(archivePayload.Response))
            {
                Messages.Message(
                    $"[RimTalk] {pawn.LabelShortCap} 的人格更新失败：长期记忆归档失败。",
                    MessageTypeDefOf.RejectInput);
                return;
            }

            string newLongTermMemory = archivePayload.Response.Trim();

            // 用「人格主體 + 新長期記憶」重組 Personality，短期記憶不保留
            string newPersonality = ComposePersonality(corePersonaHead, newLongTermMemory, corePersonaTail);
            SetPersonality(pawn, newPersonality);

            // 顯示使用的模型
            var modelName = usedConfig?.SelectedModel;
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = "未知模型";
            }

            Messages.Message(
                $"[RimTalk] {pawn.LabelShortCap} 的人格记忆已根据近期对话更新（模型：{modelName}）。",
                MessageTypeDefOf.PositiveEvent);
        }
        catch (Exception e)
        {
            Logger.Warning($"Auto persona update failed for {pawn}: {e}");
            Messages.Message(
                $"[RimTalk] {pawn?.LabelShortCap ?? "角色"} 的人格更新失败：{e.Message}",
                MessageTypeDefOf.NegativeEvent);
        }
    }

    private const string MemorySeparatorLine = "---";

    private static void SplitPersonality(string personality, out string head, out string longTerm, out string tail)
    {
        head = string.Empty;
        longTerm = string.Empty;
        tail = string.Empty;

        if (string.IsNullOrWhiteSpace(personality))
            return;

        // 正規化換行
        var text = personality.Replace("\r\n", "\n");
        var lines = text.Split('\n');

        var sections = new List<List<string>>();
        var current = new List<string>();

        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();

            // 遇到分隔線 "---" 就切一段
            if (trimmed == MemorySeparatorLine)
            {
                sections.Add(current);
                current = new List<string>();
            }
            else
            {
                current.Add(raw);
            }
        }
        // 收尾
        sections.Add(current);

        string JoinSection(List<string> s) =>
            string.Join("\n", s).TrimEnd('\r', '\n');

        if (sections.Count == 1)
        {
            // 沒有分隔線 → 整個當成人格主體
            head = JoinSection(sections[0]);
            return;
        }

        if (sections.Count == 2)
        {
            // 只有一條 "---"
            head = JoinSection(sections[0]);
            longTerm = JoinSection(sections[1]);
            return;
        }

        // 三段以上：第一段 = 人格小傳；第二段 = 長期記憶；剩下全部合成 tail（其他資訊）
        head = JoinSection(sections[0]);
        longTerm = JoinSection(sections[1]);

        var tailSections = new List<string>();
        for (int i = 2; i < sections.Count; i++)
        {
            if (tailSections.Count > 0)
            {
                // 保留中間的 "---" 結構
                tailSections.Add(MemorySeparatorLine);
            }
            var secText = JoinSection(sections[i]);
            if (!string.IsNullOrEmpty(secText))
                tailSections.Add(secText);
        }

        tail = string.Join("\n", tailSections).TrimEnd('\r', '\n');
    }

    private static string ComposePersonality(string head, string longTerm, string tail)
    {
        head = head?.TrimEnd('\r', '\n') ?? string.Empty;
        longTerm = longTerm?.TrimEnd('\r', '\n') ?? string.Empty;
        tail = tail?.TrimEnd('\r', '\n') ?? string.Empty;

        var sb = new System.Text.StringBuilder();

        // 第一段：人格小傳（head）
        if (!string.IsNullOrEmpty(head))
        {
            sb.Append(head);
        }

        // 中間和後面只要有任一段有東西，就插入分隔線
        bool hasMiddleOrTail = !string.IsNullOrEmpty(longTerm) || !string.IsNullOrEmpty(tail);

        if (hasMiddleOrTail)
        {
            if (sb.Length > 0)
                sb.Append('\n');

            // 第一條分隔線
            sb.AppendLine(MemorySeparatorLine);

            // 第二段：長期記憶（可以是空，空的話就讓 "---" 上下貼著）
            if (!string.IsNullOrEmpty(longTerm))
            {
                sb.AppendLine(longTerm);
            }

            // 如果有 tail，補上第二條分隔線 + 其他資訊
            if (!string.IsNullOrEmpty(tail))
            {
                sb.AppendLine(MemorySeparatorLine);
                sb.Append(tail);
            }
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }
}