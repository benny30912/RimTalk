using RimTalk.Patch;
using RimTalk.Source.Data;
using RimTalk.Util;
using System.Collections.Generic;
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

    /// <summary>
    /// 從 GetPawnStatusFull 收集的動作句子（用於語意向量）
    /// </summary>
    public List<string> StatusActivities { get; set; } = new List<string>();

    /// <summary>
    /// 從 GetPawnStatusFull 收集的人名（用於人名加分）
    /// </summary>
    public List<string> StatusNames { get; set; } = new List<string>();

    /// <summary>
    /// [NEW] 對話類型語意描述，供向量化使用
    /// </summary>
    public string DialogueType { get; set; }

    public TalkRequest(string prompt, Pawn initiator, Pawn recipient = null, TalkType talkType = TalkType.Other)
    {
        TalkType = talkType;
        Prompt = prompt;
        Initiator = initiator;
        Recipient = recipient;
        CreatedTick = GenTicks.TicksGame;
    }

    // 2. [新增] 記憶系統用的建構子 (強制指定 CreatedTick)
    public TalkRequest(string prompt, Pawn initiator, int createdTick)
    {
        TalkType = TalkType.Other;
        Prompt = prompt;
        Initiator = initiator;
        Recipient = null;
        CreatedTick = createdTick;
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
        } else if (TalkType == TalkType.Thought)
        {
            return !ThoughtTracker.IsThoughtStillActive(Initiator, Prompt);
        }
        return GenTicks.TicksGame - CreatedTick > CommonUtil.GetTicksForDuration(duration);
    }
}
