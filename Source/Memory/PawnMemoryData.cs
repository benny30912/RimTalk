using System.Collections.Generic;
using Verse;

namespace RimTalk.Source.Memory
{
    /// <summary>
    /// 儲存單個 Pawn 的完整記憶資料 (STM / MTM / LTM)。
    /// 取代了舊有的 PawnMessageHistoryRecord，完全移除 Raw Text 的儲存。
    /// </summary>
    public class PawnMemoryData : IExposable
    {
        public Pawn Pawn;

        /// <summary>
        /// 短期記憶 (STM)。
        /// 儲存最近對話的即時摘要 (每輪對話生成一條)。
        /// </summary>
        public List<MemoryRecord> ShortTermMemories = [];

        /// <summary>
        /// 中期記憶 (MTM)。
        /// 由多條 STM 經異步批量處理後歸納而成。
        /// </summary>
        public List<MemoryRecord> MediumTermMemories = [];

        /// <summary>
        /// 長期記憶 (LTM)。
        /// 由 MTM 進一步合併成的高層次傳記式記憶。
        /// </summary>
        public List<MemoryRecord> LongTermMemories = [];

        /// <summary>
        /// 計數器：自上次總結後新增的 STM 數量。
        /// 用於判斷何時觸發 STM -> MTM 的批量總結 (例如滿 30 條)。
        /// </summary>
        public int NewShortMemoriesSinceSummary;

        /// <summary>
        /// 計數器：自上次歸檔後新增的 MTM 數量。
        /// 用於判斷何時觸發 MTM -> LTM 的歸檔 (例如滿 200 條)。
        /// </summary>
        public int NewMediumMemoriesSinceArchival;

        // [NEW] 進行中標記（不持久化，讀檔後自動重置為 false）
        /// <summary>
        /// STM → MTM 總結是否進行中
        /// </summary>
        public bool IsStmSummarizationInProgress;
        /// <summary>
        /// MTM → LTM 歸檔是否進行中
        /// </summary>
        public bool IsMtmConsolidationInProgress;


        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");

            // 儲存三層記憶列表
            Scribe_Collections.Look(ref ShortTermMemories, "shortTermMemories", LookMode.Deep);
            Scribe_Collections.Look(ref MediumTermMemories, "mediumTermMemories", LookMode.Deep);
            Scribe_Collections.Look(ref LongTermMemories, "longTermMemories", LookMode.Deep);

            // 儲存計數器狀態
            Scribe_Values.Look(ref NewShortMemoriesSinceSummary, "newShortMemoriesSinceSummary", 0);
            Scribe_Values.Look(ref NewMediumMemoriesSinceArchival, "newMediumMemoriesSinceArchival", 0);

            // 讀檔後防禦性初始化
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ShortTermMemories ??= [];
                MediumTermMemories ??= [];
                LongTermMemories ??= [];
            }
        }
    }
}
