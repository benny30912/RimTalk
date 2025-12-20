using RimTalk.Source.Memory;
using RimTalk.Util;
using RimTalk.Vector;
using RimWorld.Planet;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Verse;

namespace RimTalk.Data;

public class RimTalkWorldComponent(World world) : WorldComponent(world)
{
    private const int MaxLogEntries = 1000;

    public Dictionary<string, string> RimTalkInteractionTexts = new();
    private Queue<string> _keyInsertionOrder = new();

    // 運行時快速存取 (Key: Pawn.thingIDNumber)
    // 這是全域唯一的記憶儲存點
    public Dictionary<int, PawnMemoryData> PawnMemories = new();

    // [NEW] 全域常識庫 (Common Knowledge)
    // 儲存適用於所有角色的共享知識
    public List<MemoryRecord> CommonKnowledgeStore = [];

    // 序列化用的列表 (因為 Scribe 不支援直接儲存複雜物件的 Dictionary)
    private List<PawnMemoryData> _memoryDataList = [];

    // [NEW] 向量儲存路徑 (Config/RimTalk/)
    private static string RimTalkDataPath => Path.Combine(
        GenFilePaths.ConfigFolderPath,  // Config 資料夾
        "RimTalk"
    );
    private string VectorDbPath => Path.Combine(
        RimTalkDataPath,
        $"{world.info.name}_vectors.bin"
    );
    private static string SemanticCachePath => Path.Combine(
        RimTalkDataPath,
        "semantic_cache.bin"
    );

    // [OPT] 優化：每 5 秒檢查一次主執行緒佇列，避免每 Tick 空轉
    private const float MainThreadQueueCheckInterval = 5f;

    public override void WorldComponentTick()
    {
        base.WorldComponentTick();

        // 使用 GenTicks.TicksGame 替代 Counter.Tick
        if (GenTicks.TicksGame % CommonUtil.GetTicksForDuration(MainThreadQueueCheckInterval) == 0)
        {
            // 驅動 MemoryService 的主執行緒任務佇列
            MemoryService.Update();
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();

        // Check for null before saving
        CommonKnowledgeStore ??= [];
        // [NEW] 儲存常識庫
        Scribe_Collections.Look(ref CommonKnowledgeStore, "commonKnowledgeStore", LookMode.Deep);

        // [儲存前] 將 Dictionary 轉為 List
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            // 確保 PawnMemories 不為 null
            PawnMemories ??= [];
            // 注意：這裡假設 PawnMemories.Values 內無 null
            _memoryDataList = new List<PawnMemoryData>(PawnMemories.Values);
            // 移除 null 的項目 (雖不應發生，但以防萬一)
            _memoryDataList.RemoveAll(x => x == null);
            // [NEW] 保存向量資料（傳入 PawnMemories 以過濾孤兒向量）
            VectorDatabase.Instance.SaveToDisk(VectorDbPath, PawnMemories);
            SemanticCache.Instance.SaveToDisk(SemanticCachePath);
        }
        // 這會保存所有的 Short/Medium/Long Term Memories
        Scribe_Collections.Look(ref _memoryDataList, "pawnMemories", LookMode.Deep);
        // [讀取後] 重建 Dictionary
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            _memoryDataList ??= [];
            PawnMemories ??= []; // Ensure Dictionary is init
            PawnMemories.Clear();
            foreach (var data in _memoryDataList)
            {
                if (data.Pawn != null)
                {
                    PawnMemories[data.Pawn.thingIDNumber] = data;
                }
            }

            // [NEW] 確保常識庫不為 null
            CommonKnowledgeStore ??= [];

            // [NEW] 載入向量資料並還原
            SemanticCache.Instance.LoadFromDisk(SemanticCachePath);
            VectorDatabase.Instance.LoadFromDisk(VectorDbPath);
        }

        try 
        {
            Scribe_Collections.Look(ref RimTalkInteractionTexts, "rimtalkInteractionTexts", LookMode.Value, LookMode.Value);
        }
        catch (System.Exception ex)
        {
            Logger.Error($"Failed to save/load interaction texts. Resetting data to prevent save corruption. Error: {ex.Message}");
            RimTalkInteractionTexts = new Dictionary<string, string>();
            _keyInsertionOrder = new Queue<string>();
        }

        List<string> keyOrderList = null;
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            keyOrderList = _keyInsertionOrder.ToList();
        }

        Scribe_Collections.Look(ref keyOrderList, "rimtalkKeyOrder");

        if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
        RimTalkInteractionTexts ??= new Dictionary<string, string>();
            
        _keyInsertionOrder = keyOrderList != null ? new Queue<string>(keyOrderList) : new Queue<string>();
    }

    public void SetTextFor(LogEntry entry, string text)
    {
        if (entry == null || text == null) return;

        string cleanText = SanitizeXmlString(text);
        string key = entry.GetUniqueLoadID();

        if (RimTalkInteractionTexts.ContainsKey(key))
        {
            RimTalkInteractionTexts[key] = cleanText;
            return;
        }

        while (_keyInsertionOrder.Count >= MaxLogEntries)
        {
            string oldestKey = _keyInsertionOrder.Dequeue();
            RimTalkInteractionTexts.Remove(oldestKey);
        }

        _keyInsertionOrder.Enqueue(key);
        RimTalkInteractionTexts[key] = cleanText;
    }

    public bool TryGetTextFor(LogEntry entry, out string text)
    {
        text = null;
        return entry != null && RimTalkInteractionTexts.TryGetValue(entry.GetUniqueLoadID(), out text);
    }

    private static string SanitizeXmlString(string invalidXml)
    {
        if (string.IsNullOrEmpty(invalidXml)) return invalidXml;

        StringBuilder stringBuilder = new StringBuilder(invalidXml.Length);
        foreach (char c in invalidXml)
        {
            // XML 1.0 allows:
            // #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]
            if ((c == 0x9) || (c == 0xA) || (c == 0xD) ||
                ((c >= 0x20) && (c <= 0xD7FF)) ||
                ((c >= 0xE000) && (c <= 0xFFFD)))
            {
                stringBuilder.Append(c);
            }
        }
        return stringBuilder.ToString();
    }
}
