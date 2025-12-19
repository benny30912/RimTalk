using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimTalk.Vector
{
    /// <summary>
    /// 語意映射工具
    /// 將遊戲數據轉換為語意豐富的文本，並提供過濾/提取功能。
    /// </summary>
    public static class SemanticMapper
    {
        #region 心情轉換

        // 心情固定描述（用於快取）
        private static readonly string[] MoodDescriptions =
        {
            "崩溃边缘、绝望、痛苦",     // 0-10%
            "心情很差、焦虑、不安",     // 10-30%
            "有些沮丧、低落",           // 30-50%
            "心情一般、平静",           // 50-70%
            "心情不错、正面、开心",     // 70-90%
            "非常开心、满足、幸福"      // 90-100%
        };

        /// <summary>
        /// 將心情百分比轉換為固定語意描述
        /// </summary>
        public static string MapMoodToSemantic(float moodPercent)
        {
            int index = moodPercent switch
            {
                < 0.1f => 0,
                < 0.3f => 1,
                < 0.5f => 2,
                < 0.7f => 3,
                < 0.9f => 4,
                _ => 5
            };
            return MoodDescriptions[index];
        }

        #endregion

        public static string MapTemperatureToSemantic(float celsius)
        {
            return celsius switch
            {
                < -20 => "极度严寒、冻伤危险",
                < 0 => "寒冷刺骨",
                < 15 => "凉爽、有点冷",
                < 25 => "舒适、宜人",
                < 35 => "温暖、有点热",
                < 45 => "炎热难耐",
                _ => "酷热致命"
            };
        }

        #region 時間轉換

        /// <summary>
        /// 將 12 小時制時間字串轉換為語意描述
        /// 輸入格式範例："9:30 AM", "11:45 PM"
        /// </summary>
        public static string MapTimeToSemantic(string hour12String)
        {
            // 解析 AM/PM 並轉換為 24 小時制
            if (string.IsNullOrWhiteSpace(hour12String)) return "白天";

            bool isPM = hour12String.ToUpper().Contains("PM");
            // 提取小時數
            var parts = hour12String.Split(':');
            if (!int.TryParse(parts[0].Trim(), out int hour)) return "白天";

            // 轉換為 24 小時制
            if (isPM && hour != 12) hour += 12;
            if (!isPM && hour == 12) hour = 0;

            return MapHourToSemantic(hour);
        }

        /// <summary>
        /// 將 24 小時制小時數轉換為語意描述
        /// </summary>
        public static string MapHourToSemantic(int hour)
        {
            return hour switch
            {
                >= 5 and < 8 => "清晨",
                >= 8 and < 11 => "上午",
                >= 11 and < 14 => "中午",
                >= 14 and < 17 => "下午",
                >= 17 and < 20 => "傍晚",
                >= 20 and < 24 => "夜晚",
                _ => "深夜"
            };
        }

        #endregion

        #region Hediff 過濾

        /// <summary>
        /// 過濾出「事件性」Hediff（急性疾病/傷害/亢奮）
        /// 排除永久傷殘、仿生義肢等靜態項目
        /// </summary>
        public static List<Hediff> FilterEventHediffs(IEnumerable<Hediff> hediffs)
        {
            if (hediffs == null) return new List<Hediff>();

            return hediffs.Where(h =>
            {
                if (h == null || h.def == null) return false;

                // 排除仿生/人工部件
                if (h.def.addedPartProps != null) return false;

                // 排除永久傷疤
                if (h is Hediff_Injury injury && injury.IsPermanent()) return false;

                // 排除老化相關
                if (h.def.chronic) return false;

                // 包含：疾病、急性傷害、藥物效果、心理狀態
                return h.def.isBad || h.def.makesSickThought ||
                       h.def.hediffClass == typeof(Hediff_High) ||
                       h.def.hediffClass == typeof(Hediff_Hangover);
            }).ToList();
        }

        #endregion

        #region Relations 處理

        // 關係詞彙對照（用於提取）
        private static readonly HashSet<string> RelationWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 中文
            "朋友", "好友", "仇人", "敌人", "情人", "情侣", "伴侣", "配偶",
            "父亲", "母亲", "儿子", "女儿", "兄弟", "姊妹", "亲戚",
            "前任", "前夫", "前妻", "恋人", "爱人",
            // 英文
            "friend", "enemy", "rival", "lover", "spouse", "husband", "wife",
            "father", "mother", "son", "daughter", "brother", "sister",
            "ex-lover", "fiancé", "fiancée"
        };

        /// <summary>
        /// 從 Relations 字串中提取關係詞彙（用於語意匹配）
        /// 輸入格式範例："Alice(朋友), Bob(仇人)"
        /// 輸出範例：["朋友", "仇人"]
        /// </summary>
        public static List<string> ExtractRelationWords(string relationsText)
        {
            if (string.IsNullOrWhiteSpace(relationsText))
                return new List<string>();

            var result = new List<string>();

            // 簡單解析：找括號內的內容
            int start = 0;
            while ((start = relationsText.IndexOf('(', start)) != -1)
            {
                int end = relationsText.IndexOf(')', start);
                if (end == -1) break;

                string word = relationsText.Substring(start + 1, end - start - 1).Trim();
                if (!string.IsNullOrEmpty(word))
                    result.Add(word);

                start = end + 1;
            }

            return result;
        }

        /// <summary>
        /// 從 Relations 字串中提取人名（用於關鍵詞匹配）
        /// </summary>
        public static List<string> ExtractRelationNames(string relationsText)
        {
            if (string.IsNullOrWhiteSpace(relationsText))
                return new List<string>();

            var result = new List<string>();
            var parts = relationsText.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                int parenIndex = part.IndexOf('(');
                if (parenIndex > 0)
                {
                    string name = part.Substring(0, parenIndex).Trim();
                    if (!string.IsNullOrEmpty(name))
                        result.Add(name);
                }
            }

            return result;
        }

        #endregion

        #region Surroundings 處理

        /// <summary>
        /// 從周遭物品中隨機選一個，取得其語意文本
        /// 模擬「看到某物觸發聯想」
        /// </summary>
        public static string GetRandomSurroundingText(List<Thing> things)
        {
            if (things == null || things.Count == 0)
                return null;

            // 隨機選一個
            var random = new Random();
            var thing = things[random.Next(things.Count)];

            if (thing?.def == null) return null;

            string label = thing.def.label ?? thing.def.defName;
            string desc = thing.def.description ?? "";

            return string.IsNullOrWhiteSpace(desc) ? label : $"{label}: {desc}";
        }

        /// <summary>
        /// 從周遭物品標籤列表中選一個(Surrounding本來就是隨機的)
        /// </summary>
        public static string GetSurroundingLabel(List<string> labels)
        {
            if (labels == null || labels.Count == 0)
                return null;

            return labels[0];
        }

        #endregion

        #region 輔助方法

        /// <summary>
        /// 從 Def 取得語意文本 (label + description)
        /// </summary>
        public static string GetSemanticTextFromDef(Def def)
        {
            if (def == null) return string.Empty;
            string label = def.label ?? def.defName;
            string desc = def.description ?? "";
            return string.IsNullOrWhiteSpace(desc) ? label : $"{label}: {desc}";
        }

        #endregion
    }
}
