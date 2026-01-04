using RimTalk.Data;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace RimTalk.Util
{
    /// <summary>
    /// 集中處理 Overlay 與 DebugWindow 的文字格式化邏輯
    /// </summary>
    public static class DisplayFormatter
    {
        // 匹配動作/心理活動：(內容), （內容）, **內容**, 【內容】, [內容]
        private static readonly Regex ActionRegex = new Regex(@"(\(.*?\)|（.*?）|\*\*.*?\*\*|【.*?】|\[.*?\])", RegexOptions.Compiled);

        // [OPTIMIZED] 人名-顏色快取 (只增不減)
        private static readonly Dictionary<string, Color> _nameColorCache = new();
        private static bool _cacheInitialized = false;
        private static Regex _nameRegex = null;

        /// <summary>
        /// 格式化整條訊息 (包含人名上色、動作格式化、流向符號)
        /// </summary>
        public static string FormatMessage(ApiLog log, bool isLastInConversation, bool showSymbols)
        {
            if (log == null) return "";

            string text = log.Response ?? "";

            // [NEW] 人名上色 (使用快取 + Regex 優化)
            text = ColorizeAllKnownNames(text);

            // 1. 動作/心理活動格式化 (變灰 + 斜體)
            text = FormatActionText(text);

            if (showSymbols && log.TalkRequest != null)
            {
                // 2. 符號顏色 = 發起者 (Initiator) 的顏色
                // 若 Initiate 為 null，回退到白色
                Color symbolColor = GetPawnColor(log.TalkRequest.Initiator);
                string symbolHtmlColor = ColorUtility.ToHtmlStringRGB(symbolColor);

                // 3. 符號選擇 (最後一句為實心 ●，其他為空心 ○)
                string symbol = isLastInConversation ? "●" : "○";

                // 4. 組合: <color=...>●</color> 內容...
                // [FIX] 移除符號與內容間的空格，避免換行
                text = $"<color=#{symbolHtmlColor}>{symbol}</color>{text}";
            }

            return text;
        }

        /// <summary>
        /// 僅格式化動作描述 (用於 DebugWindow 的 Response 欄位)
        /// </summary>
        public static string FormatActionText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // 灰色 (#808080) + 斜體
            return ActionRegex.Replace(text, "<color=#808080><i>$1</i></color>");
        }

        /// <summary>
        /// 獲取 Pawn 的專屬顏色 (Ideology 最愛顏色 或 隨機專屬色)
        /// </summary>
        public static Color GetPawnColor(Pawn pawn)
        {
            if (pawn == null) return Color.white;

            // [NEW] 只有殖民者使用專屬顏色，其他身分使用原有邏輯
            if (!pawn.IsColonist)
            {
                return PawnNameColorUtility.PawnNameColorOf(pawn);
            }

            // 1. 優先使用 Ideology DLC 的最愛顏色
            // favoriteColor 是 ColorDef 類型，需用 .color 取得實際顏色
            if (ModsConfig.IdeologyActive && pawn.story?.favoriteColor != null)
            {
                return pawn.story.favoriteColor.color;
            }

            // 2. 否則使用 Name/ThingID 作為種子生成固定顏色
            int seed = pawn.Name?.ToStringFull.GetHashCode() ?? pawn.thingIDNumber;
            return GetRandomColorFromSeed(seed);
        }

        /// <summary>
        /// 動態新增單一角色到快取 (用於新生成的角色)
        /// </summary>
        public static void RegisterPawn(Pawn pawn)
        {
            if (pawn?.Name == null) return;
            string name = pawn.LabelShort;
            if (!string.IsNullOrEmpty(name) && !_nameColorCache.ContainsKey(name))
            {
                _nameColorCache[name] = GetPawnColor(pawn);
                _nameRegex = null; // 標記 Regex 需重建
            }
        }

        /// <summary>
        /// 重置快取 (遊戲重新載入時呼叫)
        /// </summary>
        public static void ResetCache()
        {
            _nameColorCache.Clear();
            _nameRegex = null;
            _cacheInitialized = false;
        }

        /// <summary>
        /// 確保快取已初始化
        /// </summary>
        private static void EnsureCacheInitialized()
        {
            if (_cacheInitialized) return;

            // 從 WorldPawns 載入所有歷史角色 (包含死亡、離開的角色)
            if (Find.WorldPawns?.AllPawnsAliveOrDead != null)
            {
                foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead)
                {
                    if (pawn?.Name == null) continue;
                    string name = pawn.LabelShort;
                    if (!string.IsNullOrEmpty(name) && !_nameColorCache.ContainsKey(name))
                        _nameColorCache[name] = GetPawnColor(pawn);
                }
            }

            // 從當前地圖載入
            if (Find.CurrentMap?.mapPawns?.AllPawns != null)
            {
                foreach (var pawn in Find.CurrentMap.mapPawns.AllPawns)
                {
                    if (pawn?.Name == null) continue;
                    string name = pawn.LabelShort;
                    if (!string.IsNullOrEmpty(name) && !_nameColorCache.ContainsKey(name))
                        _nameColorCache[name] = GetPawnColor(pawn);
                }
            }

            _cacheInitialized = true;
        }

        /// <summary>
        /// 建立人名匹配的正則表達式 (長名優先)
        /// </summary>
        private static void BuildNameRegexIfNeeded()
        {
            if (_nameRegex != null) return;
            if (!_nameColorCache.Any()) return;

            // 用 | 連接所有名字，長名優先避免部分匹配
            var pattern = string.Join("|",
                _nameColorCache.Keys
                    .OrderByDescending(n => n.Length)
                    .Select(Regex.Escape));

            _nameRegex = new Regex(pattern, RegexOptions.Compiled);
        }

        /// <summary>
        /// 將文字中所有已知人名上色 (使用 Regex 優化)
        /// </summary>
        private static string ColorizeAllKnownNames(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            EnsureCacheInitialized();
            BuildNameRegexIfNeeded();

            if (_nameRegex == null) return text;

            return _nameRegex.Replace(text, match =>
            {
                if (_nameColorCache.TryGetValue(match.Value, out Color color))
                {
                    string colorHex = ColorUtility.ToHtmlStringRGB(color);
                    return $"<color=#{colorHex}>{match.Value}</color>";
                }
                return match.Value;
            });
        }

        /// <summary>
        /// 使用種子生成固定的隨機顏色
        /// </summary>
        private static Color GetRandomColorFromSeed(int seed)
        {
            Random.InitState(seed);
            // H: 0-1 (全色系)
            // S: 0.3-1 (避免太淡)
            // V: 0.6-1 (避免太暗，保證文字可讀性)
            return Random.ColorHSV(0f, 1f, 0.3f, 1f, 0.6f, 1f);
        }
    }
}
