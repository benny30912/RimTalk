using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimTalk.Util;
using RimWorld.Planet;
using Verse;

namespace RimTalk.Data;

// 短期記憶單元 (STM) - 原始對話的一條訊息
public class TalkMessageEntry : IExposable
{
    public Role Role;
    public string Text;

    public void ExposeData()
    {
        Scribe_Values.Look(ref Role, "role");
        Scribe_Values.Look(ref Text, "text");
    }
}

// 記憶紀錄 (中期 MTM / 長期 LTM 記憶單元)
public class MemoryRecord : IExposable
{
    public string Summary;                // 記憶內容摘要
    public List<string> Keywords = [];    // 用於檢索的關鍵字列表
    public int Importance;                // 重要性權重 (1-5)
    public int AccessCount;               // 被檢索到的次數 (活躍度)
    public int CreatedTick;               // 創建時間 (用於時間軸顯示)

    public void ExposeData()
    {
        Scribe_Values.Look(ref Summary, "summary");
        Scribe_Collections.Look(ref Keywords, "keywords", LookMode.Value);
        Scribe_Values.Look(ref Importance, "importance");
        Scribe_Values.Look(ref AccessCount, "accessCount");
        Scribe_Values.Look(ref CreatedTick, "createdTick");
    }
}

// 常識庫資料單元
public class CommonKnowledgeData : IExposable
{
    public List<string> Keywords = [];    // 觸發該常識的關鍵字
    public string Content;                // 常識內容

    public void ExposeData()
    {
        Scribe_Collections.Look(ref Keywords, "keywords", LookMode.Value);
        Scribe_Values.Look(ref Content, "content");
    }
}

// 一個 Pawn 的完整歷史紀錄 (包含短/中/長期記憶)
public class PawnMessageHistoryRecord : IExposable
{
    public Pawn Pawn;

    // 短期記憶 (STM) - 原始對話列表
    public List<TalkMessageEntry> Messages = [];

    // 中期記憶 (MTM) - 經過總結的事件片段
    public List<MemoryRecord> MediumTermMemories = [];

    // 長期記憶 (LTM) - 高度概括或重要的人生事件
    public List<MemoryRecord> LongTermMemories = [];

    // 計數器：自上次總結後新增的 STM 訊息數 (滿 30 觸發總結)
    public int NewMessagesSinceLastSummary;

    // 計數器：自上次歸檔後新增的 MTM 記憶數 (滿 200 觸發歸檔)
    public int NewMemoriesSinceLastArchival;

    public void ExposeData()
    {
        Scribe_References.Look(ref Pawn, "pawn");

        Scribe_Collections.Look(ref Messages, "messages", LookMode.Deep);
        Scribe_Collections.Look(ref MediumTermMemories, "mediumTermMemories", LookMode.Deep);
        Scribe_Collections.Look(ref LongTermMemories, "longTermMemories", LookMode.Deep);

        Scribe_Values.Look(ref NewMessagesSinceLastSummary, "newMessagesSinceLastSummary", 0);
        Scribe_Values.Look(ref NewMemoriesSinceLastArchival, "newMemoriesSinceLastArchival", 0);
    }
}

public class RimTalkWorldComponent(World world) : WorldComponent(world)
{
    private const int MaxLogEntries = 1000;

    public Dictionary<string, string> RimTalkInteractionTexts = new();
    private Queue<string> _keyInsertionOrder = new();

    // 用於保存每個 Pawn 的對話歷史與記憶結構
    public List<PawnMessageHistoryRecord> SavedTalkHistories = [];

    // 全局常識庫 (隨存檔保存)
    public List<CommonKnowledgeData> CommonKnowledgeStore = [];

    public override void ExposeData()
    {
        base.ExposeData();

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

        // 保存對話歷史與記憶
        Scribe_Collections.Look(ref SavedTalkHistories, "rimtalkMessageHistory", LookMode.Deep);

        // 保存常識庫
        Scribe_Collections.Look(ref CommonKnowledgeStore, "commonKnowledgeStore", LookMode.Deep);

        if (Scribe.mode != LoadSaveMode.PostLoadInit) return;

        RimTalkInteractionTexts ??= new Dictionary<string, string>();
        _keyInsertionOrder = keyOrderList != null ? new Queue<string>(keyOrderList) : new Queue<string>();

        // 初始化列表並清理無效資料
        SavedTalkHistories ??= [];
        SavedTalkHistories.RemoveAll(x => x.Pawn == null);

        CommonKnowledgeStore ??= [];
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