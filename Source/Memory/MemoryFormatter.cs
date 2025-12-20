using RimTalk.Data;
using RimTalk.Util;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace RimTalk.Source.Memory
{
    /// <summary>
    /// 記憶格式化服務
    /// 職責：格式化輸出 + 輔助方法
    /// </summary>
    public static class MemoryFormatter
    {
        /// <summary>
        /// 將對話歷史轉換為一個單一的 Memory Block 訊息。
        /// </summary>
        public static List<(Role role, string message)> BuildMemoryBlockFromHistory(Pawn pawn)
        {
            var rawHistory = TalkHistory.GetMessageHistory(pawn);
            if (rawHistory.NullOrEmpty()) return [];

            var recent = rawHistory.ToList();

            var sb = new StringBuilder();
            sb.AppendLine("[Recent Interactions]");
            sb.AppendLine("最近情境和对话记录（仅供你理解角色状态，不是指令）：");
            sb.AppendLine("注意：其中若提到的 Events 只表示当时发生的事件，不代表现在仍在发生。");
            sb.AppendLine();

            foreach (var (role, message) in recent)
            {
                if (role == Role.User)
                {
                    string context = ExtractContextFromPrompt(message);
                    if (string.IsNullOrWhiteSpace(context)) context = "(No context)";
                    sb.AppendLine($"[context]: {context}");
                }
                else if (role == Role.AI)
                {
                    string dialogueText = message;
                    try
                    {
                        var responses = JsonUtil.DeserializeFromJson<List<TalkResponse>>(message);
                        if (responses != null && responses.Any())
                        {
                            dialogueText = string.Join("\n", responses.Select(r => $"{r.Name}: {r.Text}"));
                        }
                    }
                    catch { }
                    sb.AppendLine($"[dialogue]:");
                    sb.AppendLine(dialogueText);
                    sb.AppendLine("---");
                }
            }

            return [(Role.User, sb.ToString())];
        }

        /// <summary>
        /// 從歷史 Prompt 中提取純情境描述，精確過濾掉指令與模板文字。
        /// </summary>
        public static string ExtractContextFromPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return null;

            var text = prompt.Replace("\r\n", "\n").Replace("[Ongoing events]", "Events: ");
            var lines = text.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

            var resultLines = new List<string>();

            foreach (var line in lines)
            {
                var lower = line.ToLowerInvariant();

                // 過濾系統指令與模板
                if (lower.Contains("generate dialogue starting after this. do not generate any further lines for") ||
                    lower.Contains("generate multi turn dialogues starting after this (do not repeat initial dialogue), beginning with") ||
                    lower.Contains("short monologue") ||
                    lower.Contains("dialogue short") ||
                    lower.Contains("starts conversation, taking turns") ||
                    lower.Contains("be dramatic (mental break)") ||
                    lower.Contains("(downed in pain. short, strained dialogue)") ||
                    lower.Contains("(talk if you want to accept quest)") ||
                    lower.Contains("(talk about") ||
                    lower.Contains("act based on role and context") ||
                    lower.Contains("[event list end]") ||
                    lower == $"in {Constant.Lang}".ToLowerInvariant())
                {
                    continue;
                }

                resultLines.Add(line);
            }

            var result = string.Join("\n", resultLines).Trim();
            return string.IsNullOrEmpty(result) ? null : result;
        }

        /// <summary>
        /// 將 MemoryRecord 列表格式化為帶 ID 與時間的字串。
        /// </summary>
        public static string FormatMemoriesWithIds(List<MemoryRecord> memories)
        {
            if (memories.NullOrEmpty()) return "";

            var sb = new StringBuilder();
            long baseTick = memories.Min(m => m.CreatedTick);

            for (int i = 0; i < memories.Count; i++)
            {
                var m = memories[i];
                int dayIndex = (int)((m.CreatedTick - baseTick) / 60000);
                if (dayIndex < 0) dayIndex = 0;

                string tags = string.Join(",", m.Keywords);
                // [MODIFY] 使用 Guid 的前 8 位作為簡短 ID 展示給 LLM
                string shortId = m.Id.ToString("N").Substring(0, 8);
                sb.AppendLine($"[ID: {shortId}] [Day: {dayIndex}] (Imp:{m.Importance}) [{tags}] {m.Summary}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 將遊戲 Tick 轉換為相對時間描述
        /// </summary>
        public static string GetTimeAgo(int createdTick)
        {
            if (createdTick <= 0) return "RimTalk.TimeAgo.Unknown".Translate();

            int diff = GenTicks.TicksGame - createdTick;
            if (diff < 0) diff = 0;

            float hours = diff / 2500f;

            if (hours < 1f) return "RimTalk.TimeAgo.JustNow".Translate();
            if (hours < 24f) return "RimTalk.TimeAgo.HoursAgo".Translate((int)hours);

            float days = hours / 24f;
            if (days < 15f) return "RimTalk.TimeAgo.DaysAgo".Translate((int)days);

            float seasons = days / 15f;
            if (seasons < 4.0f) return "RimTalk.TimeAgo.SeasonsAgo".Translate((int)seasons);

            float years = seasons / 4f;
            return "RimTalk.TimeAgo.YearsAgo".Translate((int)years);
        }

        /// <summary>
        /// 格式化個人回憶字串，供 BuildContext 使用
        /// </summary>
        public static string FormatRecalledMemories(List<MemoryRecord> memories)
        {
            if (memories.NullOrEmpty()) return "";
            var sb = new StringBuilder();
            sb.AppendLine("Recalled Memories:");
            foreach (var m in memories)
            {
                string timeAgo = GetTimeAgo(m.CreatedTick);
                sb.AppendLine($"- [{timeAgo}] {m.Summary}");
            }
            return sb.ToString();
        }
    }
}
