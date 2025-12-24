using System.Collections.Generic;
using Verse;

namespace RimTalk.Data
{
    /// <summary>
    /// Context 項目（Def 或 Text）
    /// 用於延遲向量計算，在後台執行緒批次處理
    /// </summary>
    public class ContextItem
    {
        public enum ItemType { Def, Text }

        // [NEW] 情境類別標籤
        public enum Category
        {
            Other,          // 其他/未分類
            Mood,           // 心情
            EventHediff,    // 事件性狀態（流血、感染）
            Thought,        // 想法
            Relation,       // 關係
            Time,           // 時間
            Season,         // 季節
            Weather,        // 天氣
            Temperature,    // 溫度
            Surrounding,    // 周圍環境
            Activity,       // 周圍活動
            DialogueType    // 對話類型
        }

        public ItemType Type { get; set; }
        public Def Def { get; set; }     // Type == Def 時使用
        public string Text { get; set; } // Type == Text 時使用
        public Category ContextCategory { get; set; } = Category.Other;  // [NEW]
    }

    /// <summary>
    /// 單個 Pawn 的快照資料
    /// </summary>
    public class PawnSnapshotData
    {
        public int PawnId { get; set; }                                 // Pawn.thingIDNumber
        public List<ContextItem> Items { get; set; } = new();           // 該 Pawn 的 Context 項目
        public HashSet<string> Names { get; set; } = new();             // 收集的人名（用於人名加分）
        public string PawnText { get; set; }                            // 帶 [[MEMORY_INJECTION_POINT]] 的描述
        public string QueryText { get; set; }                           // [NEW] Reranker 查詢文本
                                                                        // [NEW] 動態 QueryText 基本參數
        /// <summary>
        /// 當前動作（如「收割野生植物」）
        /// </summary>
        public string CurrentAction { get; set; }
        /// <summary>
        /// 對話類型語意描述（如「閒聊」「爭吵」）
        /// </summary>
        public string DialogueType { get; set; }
    }

    /// <summary>
    /// 儲存主執行緒收集的遊戲資料快照
    /// 供後台執行緒執行向量計算和記憶檢索
    /// </summary>
    public class ContextSnapshot
    {
        // 每個 Pawn 的獨立資料
        public List<PawnSnapshotData> PawnData { get; set; } = new();

        // 常識檢索用的搜索文本
        public string KnowledgeSearchText { get; set; }
    }
}
