using System;
using System.Collections.Generic;
using Verse;

// [MODIFY] 命名空間變更
namespace RimTalk.Source.Memory
{
    /// <summary>
    /// 代表一條記憶紀錄 (適用於短/中/長期記憶)。
    /// 包含 AI 生成的摘要、關鍵詞與重要性。
    /// </summary>
    public class MemoryRecord : IExposable
    {
        // [NEW] 永久唯一識別碼
        public Guid Id = Guid.NewGuid();

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

        // [NEW] 來源記憶 ID 列表 (用於追蹤合併來源)
        public List<Guid> SourceIds = [];

        // [NEW] Guid 序列化用的臨時字串
        private string _idString;
        private List<string> _sourceIdStrings;

        public void ExposeData()
        {
            // [NEW] 序列化 Guid Id
            if (Scribe.mode == LoadSaveMode.Saving)
                _idString = Id.ToString();
            Scribe_Values.Look(ref _idString, "id");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && !string.IsNullOrEmpty(_idString))
                Id = Guid.Parse(_idString);

            Scribe_Values.Look(ref Summary, "summary");
            Scribe_Collections.Look(ref Keywords, "keywords", LookMode.Value);
            Scribe_Values.Look(ref Importance, "importance");
            Scribe_Values.Look(ref AccessCount, "accessCount");
            Scribe_Values.Look(ref CreatedTick, "createdTick");

            // [NEW] 序列化 SourceIds
            if (Scribe.mode == LoadSaveMode.Saving)
                _sourceIdStrings = SourceIds?.ConvertAll(g => g.ToString()) ?? [];
            Scribe_Collections.Look(ref _sourceIdStrings, "sourceIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                SourceIds = _sourceIdStrings?.ConvertAll(s => Guid.Parse(s)) ?? [];
                Keywords ??= [];
            }
        }
    }
}
