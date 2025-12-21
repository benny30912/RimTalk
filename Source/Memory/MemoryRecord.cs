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

        // [MODIFY] 暫存用途，不序列化
        public List<Guid> SourceIds = [];

        // [NEW] Guid 序列化用的臨時字串
        private string _idString;

        /// <summary>
        /// 建立獨立副本（新 ID）
        /// </summary>
        public MemoryRecord Clone()
        {
            return new MemoryRecord
            {
                Id = Guid.NewGuid(),  // 新 ID
                Summary = this.Summary,
                Keywords = new List<string>(this.Keywords ?? new List<string>()),
                Importance = this.Importance,
                AccessCount = 0,  // 新記憶從 0 開始
                CreatedTick = this.CreatedTick,
                SourceIds = []
            };
        }

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

            // [REMOVE] SourceIds 不再序列化
            // 載入後初始化為空列表
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Keywords ??= [];
                SourceIds = [];
            }
        }
    }
}
