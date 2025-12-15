using System.Collections.Generic;
using Verse;

namespace RimTalk.Data
{
    /// <summary>
    /// 代表一條記憶紀錄 (適用於短/中/長期記憶)。
    /// 包含 AI 生成的摘要、關鍵詞與重要性。
    /// </summary>
    public class MemoryRecord : IExposable
    {
        /// <summary>
        /// 記憶內容摘要 (AI 生成)。
        /// </summary>
        public string Summary;

        /// <summary>
        /// 用於檢索的關鍵詞列表。
        /// </summary>
        public List<string> Keywords = [];

        /// <summary>
        /// 重要性權重 (1-5)，影響記憶的保留時間與提取優先級。
        /// </summary>
        public int Importance;

        /// <summary>
        /// 被檢索到的次數 (活躍度)，用於計算記憶權重。
        /// </summary>
        public int AccessCount;

        /// <summary>
        /// 創建時間 (Tick)，用於計算相對時間與時間衰減。
        /// </summary>
        public int CreatedTick;

        // [可選] 若未來需要追蹤長期記憶是由哪些中期記憶合併而來，可在此擴充 SourceIds

        public void ExposeData()
        {
            Scribe_Values.Look(ref Summary, "summary");
            Scribe_Collections.Look(ref Keywords, "keywords", LookMode.Value);
            Scribe_Values.Look(ref Importance, "importance");
            Scribe_Values.Look(ref AccessCount, "accessCount");
            Scribe_Values.Look(ref CreatedTick, "createdTick");

            // 確保讀檔後 Keywords 不為 null
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Keywords ??= [];
            }
        }
    }
}
