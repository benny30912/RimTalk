using System.Threading;
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

    // ★ 全域重試用的 CancellationTokenSource
    private static CancellationTokenSource _retryCts = new CancellationTokenSource();

    /// <summary>
    /// 取消目前所有人格更新重試，並建立新的 Token。
    /// 在回主選單 / 讀檔時呼叫。
    /// </summary>
    public static void CancelAllRetries()
    {
        try
        {
            _retryCts.Cancel();
        }
        catch
        {
            // 忽略取消時的例外
        }

        _retryCts.Dispose();
        _retryCts = new CancellationTokenSource();
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
        if (!AutoUpdateEnabled) return;
        if (pawn == null || pawn.Dead || pawn.Destroyed) return;
        if (batch == null || batch.Count == 0) return;

        // ★ 每次進來時擷取當前的 token
        var token = _retryCts.Token;

        try
        {
            // ====== 先選好這次要用的 API client + config ======
            var settings = Settings.Get();
            ApiConfig usedConfig = null;

            // 依你現在的規則選 config：簡單模式 = 當前 config；進階模式 = 列表最後一個
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
            }
            if (usedConfig == null || !usedConfig.IsValid())
            {
                Messages.Message($"[RimTalk] {pawn.LabelShortCap} 的人格更新失败：API 设定无效。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            const int maxRetry = 10; // 若你真想「直到成功」，可設為 0，或用 int.MaxValue，比較保險建議設一個上限
            int attempt = 0;
            bool success = false;

            while (true)
            {
                // ★ 若已被取消（回主選單 / 讀檔），直接跳出
                if (token.IsCancellationRequested) break;

                if (pawn.Dead || pawn.Destroyed) break;
                if (!AutoUpdateEnabled) break;

                success = await TryUpdatePersonaFromBatch(pawn, batch, usedConfig);
                attempt++;

                if (success)
                {
                    // 顯示使用的模型
                    var modelName = usedConfig.SelectedModel;
                    if (string.IsNullOrWhiteSpace(modelName))
                        modelName = "未知模型";
                    Messages.Message( $"[RimTalk] {pawn.LabelShortCap} 的人格记忆已根据近期对话更新（模型：{modelName}）。", MessageTypeDefOf.PositiveEvent, false);
                    break;
                }
                // 若有設上限，超過就停止
                if (maxRetry > 0 && attempt >= maxRetry)
                {
                    Messages.Message($"[RimTalk] {pawn.LabelShortCap} 的人格更新失败 {maxRetry} 次，已放弃重试。", MessageTypeDefOf.RejectInput, false);
                    break;
                }
                // 失敗：失敗時提示
                Messages.Message($"[RimTalk] {pawn.LabelShortCap} 的人格更新失败，将在约 1 分钟后重试。", MessageTypeDefOf.NegativeEvent, false);
                // ★ 等候 1 分鐘，使用 token，可在 Reset 時被取消
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), token);
                }
                catch (TaskCanceledException)
                {
                    // 被回主選單 / 讀檔取消，不需要顯示錯誤，靜靜結束即可
                    break;
                }
            }
        }
        catch (TaskCanceledException)
        {
            // 另一層保險：若 delay 之外的 await 也被 token 取消
            // 一樣靜默結束，不提示玩家
        }
        catch (Exception e)
        {
            Messages.Message( $"[RimTalk] {pawn?.LabelShortCap ?? "角色"} 的人格更新失败：{e.Message}", MessageTypeDefOf.NegativeEvent, false);
        }
    }

    private static async Task<bool> TryUpdatePersonaFromBatch(Pawn pawn, List<(Role role, string message)> batch, ApiConfig usedConfig)
    {
        try
        {
            // 目前人格全文
            string currentPersona = GetPersonality(pawn);
            if (string.IsNullOrWhiteSpace(currentPersona)) return false;

            // 拆成：人格主體、長期記憶、短期記憶、尾部
            SplitPersonality(currentPersona, out var corePersonaHead, out var oldLongTerm, out var oldShortTerm, out var corePersonaTail);

            var client = AIClientFactory.GetAIClientForConfig(usedConfig);
            if (client == null) return false;

            // Step 1：舊長期記憶 + 舊短期記憶 → 新長期記憶
            string newLongTerm = oldLongTerm;
            // 只有真的有東西需要歸檔時才叫 LLM
            if (!string.IsNullOrWhiteSpace(oldLongTerm) || !string.IsNullOrWhiteSpace(oldShortTerm))
            {
                var archiveSb = new System.Text.StringBuilder();
                archiveSb.AppendLine($"下面是 {pawn.LabelShortCap} 的人格描述（不包含记忆部分）：");
                archiveSb.AppendLine(corePersonaHead);
                if (!string.IsNullOrWhiteSpace(corePersonaTail))
                {
                    archiveSb.AppendLine();
                    archiveSb.AppendLine(corePersonaTail);
                }
                archiveSb.AppendLine();
                archiveSb.AppendLine($"下面是 {pawn.LabelShortCap} 目前的「长期记忆」（可能为空）：");
                archiveSb.AppendLine(string.IsNullOrWhiteSpace(oldLongTerm)? "（目前没有长期记忆记录。）": oldLongTerm);
                archiveSb.AppendLine();
                archiveSb.AppendLine($"下面是 {pawn.LabelShortCap} 之前累积的「短期记忆」（可能为空）：");
                archiveSb.AppendLine(string.IsNullOrWhiteSpace(oldShortTerm)? "（目前没有短期记忆记录。）": oldShortTerm);
                archiveSb.AppendLine();
                archiveSb.AppendLine($"请在不改变角色人格基调的前提下，将短期记忆整合进长期记忆，生成一段更新后的「{pawn.LabelShortCap} 的长期记忆」。");
                archiveSb.AppendLine("要求：");
                archiveSb.AppendLine("1. 保留仍会持续影响角色的长期经历、关系与价值观。");
                archiveSb.AppendLine("2. 将短期记忆中，可能持续影响角色的体验与情绪浓缩进去。");
                archiveSb.AppendLine("3. 总结重要里程碑事件和转折点，合并相似经历，突出长期趋势。");
                archiveSb.AppendLine("4. 只输出更新后的长期记忆内容本身，不要加标题或任何额外说明，字数必须控制在120字以内。");

                string archiveSystemInstruction = "你是一个帮忙管理角色记忆档案的助手。根据人格描述、旧的长期记忆和旧的短期记忆，" + "生成一份更新后的长期记忆。";

                var archivePayload = await client.GetChatCompletionAsync(archiveSystemInstruction, new List<(Role role, string message)>{(Role.User, archiveSb.ToString())});

                if (archivePayload == null || string.IsNullOrWhiteSpace(archivePayload.Response)){return false;}

                newLongTerm = archivePayload.Response.Trim();
            }
            // Step 2：這一批 batch → 新「短期記憶」
            var shortSb = new System.Text.StringBuilder();
            shortSb.AppendLine($"以下是 {pawn.LabelShortCap} 最近的一些对话与互动记录。");
            shortSb.AppendLine($"请你将这些内容整理成一段简短的「{pawn.LabelShortCap} 的短期记忆」，用于描述近期发生的重要事件、情绪变化、人际关系与心态。");
            shortSb.AppendLine("要求：");
            shortSb.AppendLine("1. 提炼地点、人物、事件，将相似事件合并，标注频率。");
            shortSb.AppendLine("2. 只描述事实与心理，不要加入系统说明或指令。");
            shortSb.AppendLine("3. 只输出短期记忆内容本身，不要加任何标题或额外说明，字数必须控制在80字以内。");
            shortSb.AppendLine();
            shortSb.AppendLine("最近互动记录：");

            int lineCountBefore = shortSb.Length;

            foreach (var (role, msg) in batch)
            {
                if (string.IsNullOrWhiteSpace(msg)) continue;
                string ctx = msg;
                string prefix = "[对话]";
                if (role == Role.User) {
                    // 抽出情境：去掉模板頭、語言行、各種「Talk about ...」「be dramatic」指令
                    ctx = PromptService.ExtractContextFromPrompt(msg);
                    if (string.IsNullOrWhiteSpace(ctx)) continue;
                    prefix = "[情境]";
                }
                shortSb.AppendLine($"{prefix} {ctx.Trim()}");
            }

            // 如果完全沒有任何可用內容，就不要更新短期記憶
            if (shortSb.Length == lineCountBefore){return false;}

            string shortTermSystemInstruction = "你是一个帮忙整理角色记忆的助手。你的任务是根据提供的对话与事件记录，" + "写出一段可作为角色「短期记忆」的总结文字。";

            var shortPayload = await client.GetChatCompletionAsync(shortTermSystemInstruction, new List<(Role role, string message)>{(Role.User, shortSb.ToString())});

            if (shortPayload == null || string.IsNullOrWhiteSpace(shortPayload.Response)){return false;}

            string newShortTerm = shortPayload.Response.Trim();
            // Step 3：用「人格主體 + 新長期 + 新短期 + 尾部」重組 Personality
            string newPersonality = ComposePersonality(corePersonaHead, newLongTerm, newShortTerm, corePersonaTail);
            SetPersonality(pawn, newPersonality);

            return true;
        }
        catch (Exception e)
        {
            Messages.Message($"[RimTalk] {pawn?.LabelShortCap ?? "角色"} 的人格更新失败：{e.Message}", MessageTypeDefOf.NegativeEvent, false);
            return false;
        }
    }

    private const string MemorySeparatorLine = "---";

    private static void SplitPersonality(string personality, out string head, out string longTerm, out string shortTerm, out string tail)
    {
        head = string.Empty;
        longTerm = string.Empty;
        shortTerm = string.Empty;
        tail = string.Empty;

        if (string.IsNullOrWhiteSpace(personality))return;

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

        string JoinSection(List<string> s) => string.Join("\n", s).TrimEnd('\r', '\n');

        if (sections.Count == 1)
        {
            // 沒有分隔線 → 整個當成人格主體
            head = JoinSection(sections[0]);
            return;
        }

        if (sections.Count == 2)
        {
            // 兩段 → 人格主體 + 尾部，其餘視為空
            head = JoinSection(sections[0]);
            tail = JoinSection(sections[1]);
            return;
        }

        if (sections.Count == 3)
        {
            // 三段 → 人格主體 + 長期記憶 + 尾部，短期記憶為空
            head = JoinSection(sections[0]);
            longTerm = JoinSection(sections[1]);
            tail = JoinSection(sections[2]);
            return;
        }

        // 四段以上：第一段 = 人格主體 第二段 = 長期記憶 第三段 = 短期記憶 其餘段落合併為尾部，中間保留分隔線結構
        head = JoinSection(sections[0]);
        longTerm = JoinSection(sections[1]);
        shortTerm = JoinSection(sections[2]);

        var tailSections = new List<string>();
        for (int i = 3; i < sections.Count; i++)
        {
            if (tailSections.Count > 0)
            {
                // 保留中間的 "---" 結構
                tailSections.Add(MemorySeparatorLine);
            }
            var secText = JoinSection(sections[i]);
            if (!string.IsNullOrEmpty(secText))tailSections.Add(secText);
        }

        tail = string.Join("\n", tailSections).TrimEnd('\r', '\n');
    }

    private static string ComposePersonality(string head, string longTerm, string shortTerm, string tail)
    {
        head = head?.TrimEnd('\r', '\n') ?? string.Empty;
        longTerm = longTerm?.TrimEnd('\r', '\n') ?? string.Empty;
        shortTerm = shortTerm?.TrimEnd('\r', '\n') ?? string.Empty;
        tail = tail?.TrimEnd('\r', '\n') ?? string.Empty;

        var sb = new System.Text.StringBuilder();

        // 第一段：人格主體
        if (!string.IsNullOrEmpty(head)){sb.Append(head);}

        // 往後只要有任一記憶或尾部，就輸出標準 4 段結構
        bool hasAnyMemoryOrTail = !string.IsNullOrEmpty(longTerm) || !string.IsNullOrEmpty(shortTerm) || !string.IsNullOrEmpty(tail);

        if (hasAnyMemoryOrTail)
        {
            sb.AppendLine();
            sb.AppendLine(MemorySeparatorLine);   // head 與長期記憶之間

            sb.AppendLine(longTerm ?? string.Empty);
            sb.AppendLine(MemorySeparatorLine);   // 長期與短期之間

            sb.AppendLine(shortTerm ?? string.Empty);
            sb.AppendLine(MemorySeparatorLine);   // 短期與尾部之間

            sb.Append(tail ?? string.Empty);
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }
}