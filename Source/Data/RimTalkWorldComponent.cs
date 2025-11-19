using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using Verse;

namespace RimTalk.Data;

// 一句話
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

// 一個 pawn 的歷史
public class PawnMessageHistoryRecord : IExposable
{
    public Pawn Pawn;
    public List<TalkMessageEntry> Messages = new();

    public void ExposeData()
    {
        Scribe_References.Look(ref Pawn, "pawn");
        Scribe_Collections.Look(ref Messages, "messages", LookMode.Deep);
    }
}

public class RimTalkWorldComponent(World world) : WorldComponent(world)
{
    private const int MaxLogEntries = 1000;

    public Dictionary<string, string> RimTalkInteractionTexts = new();
    private Queue<string> _keyInsertionOrder = new();
    public List<PawnMessageHistoryRecord> SavedTalkHistories = new();

    public override void ExposeData()
    {
        base.ExposeData();

        Scribe_Collections.Look(ref RimTalkInteractionTexts, "rimtalkInteractionTexts", LookMode.Value, LookMode.Value);
       
        List<string> keyOrderList = null;
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            keyOrderList = _keyInsertionOrder.ToList();
        }

        Scribe_Collections.Look(ref keyOrderList, "rimtalkKeyOrder");

        Scribe_Collections.Look(ref SavedTalkHistories, "rimtalkMessageHistory", LookMode.Deep);
        
        if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
        RimTalkInteractionTexts ??= new Dictionary<string, string>();
        SavedTalkHistories ??= new List<PawnMessageHistoryRecord>();

        SavedTalkHistories.RemoveAll(r =>
            r == null || r.Pawn == null || r.Messages == null || r.Messages.Count == 0);

        _keyInsertionOrder = keyOrderList != null ? new Queue<string>(keyOrderList) : new Queue<string>();
    }

    public void SetTextFor(LogEntry entry, string text)
    {
        if (entry == null || text == null) return;

        string key = entry.GetUniqueLoadID();

        if (RimTalkInteractionTexts.ContainsKey(key))
        {
            RimTalkInteractionTexts[key] = text;
            return;
        }

        while (_keyInsertionOrder.Count >= MaxLogEntries)
        {
            string oldestKey = _keyInsertionOrder.Dequeue();
            RimTalkInteractionTexts.Remove(oldestKey);
        }

        _keyInsertionOrder.Enqueue(key);
        RimTalkInteractionTexts[key] = text;
    }

    public bool TryGetTextFor(LogEntry entry, out string text)
    {
        text = null;
        return entry != null && RimTalkInteractionTexts.TryGetValue(entry.GetUniqueLoadID(), out text);
    }
}