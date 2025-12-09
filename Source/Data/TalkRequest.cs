using RimTalk.Patch;
using RimTalk.Source.Data;
using RimTalk.Util;
using Verse;

namespace RimTalk.Data;

public class TalkRequest
{
    public TalkType TalkType { get; set; }
    public string Prompt { get; set; }
    public Pawn Initiator { get; set; }
    public Pawn Recipient { get; set; }
    public int MapId { get; set; }
    public int CreatedTick { get; set; }
    public bool IsMonologue;

    // 原有的建構函數 (保持不變，給主線程使用)
    public TalkRequest(string prompt, Pawn initiator, Pawn recipient = null, TalkType talkType = TalkType.Other)
    {
        TalkType = talkType;
        Prompt = prompt;
        Initiator = initiator;
        Recipient = recipient;
        CreatedTick = GenTicks.TicksGame; // 主線程調用是安全的
    }

    // ★ 新增：給後台線程使用的建構函數 (手動傳入 tick)
    public TalkRequest(string prompt, Pawn initiator, int createdTick, TalkType talkType = TalkType.Other)
    {
        TalkType = talkType;
        Prompt = prompt;
        Initiator = initiator;
        Recipient = null;
        CreatedTick = createdTick; // 直接使用傳入的值，不存取 GenTicks
    }

    public bool IsExpired()
    {
        int duration = 10;
        if (TalkType == TalkType.User) return false;
        if (TalkType == TalkType.Urgent)
        {
            duration = 5;
            if (!Initiator.IsInDanger())
            {
                return true;
            }
        }
        else if (TalkType == TalkType.Thought)
        {
            return !ThoughtTracker.IsThoughtStillActive(Initiator, Prompt);
        }
        return GenTicks.TicksGame - CreatedTick > CommonUtil.GetTicksForDuration(duration);
    }
}