using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimTalk.Data;

public static class TalkHistory
{
    private const int MaxMessages = 30;
    private static readonly ConcurrentDictionary<Guid, int> SpokenTickCache = new() { [Guid.Empty] = 0 };
    private static readonly ConcurrentBag<Guid> IgnoredCache = [];

    private static RimTalkWorldComponent WorldComp =>
        Find.World?.GetComponent<RimTalkWorldComponent>();

    private static List<PawnMessageHistoryRecord> Histories =>
        WorldComp?.SavedTalkHistories;

    // Add a new talk with the current game tick
    public static void AddSpoken(Guid id)
    {
        SpokenTickCache.TryAdd(id, GenTicks.TicksGame);
    }
    
    public static void AddIgnored(Guid id)
    {
        IgnoredCache.Add(id);
    }

    public static int GetSpokenTick(Guid id)
    {
        return SpokenTickCache.TryGetValue(id, out var tick) ? tick : -1;
    }
    
    public static bool IsTalkIgnored(Guid id)
    {
        return IgnoredCache.Contains(id);
    }

    public static void AddMessageHistory(Pawn pawn, string request, string response)
    {
        if (pawn == null || Histories == null) return;

        var record = Histories.FirstOrDefault(r => r.Pawn == pawn);
        if (record == null)
        {
            record = new PawnMessageHistoryRecord { Pawn = pawn };
            Histories.Add(record);
        }

        var list = record.Messages;

        // 照舊加兩條：User + AI
        list.Add(new TalkMessageEntry { Role = Role.User, Text = request });
        list.Add(new TalkMessageEntry { Role = Role.AI, Text = response });

        EnsureMessageLimit(list);
    }


    public static List<(Role role, string message)> GetMessageHistory(Pawn pawn)
    {
        if (pawn == null || Histories == null) return [];

        var record = Histories.FirstOrDefault(r => r.Pawn == pawn);
        if (record == null || record.Messages == null) return [];

        // 回傳一份 tuple list 給 AIService 用，外部呼叫不用改
        return record.Messages
            .Select(m => (m.Role, m.Text))
            .ToList();
    }

    private static void EnsureMessageLimit(List<TalkMessageEntry> messages)
    {
        // 先把連續同 Role 的舊訊息砍掉，維持 User/AI 交錯
        for (int i = messages.Count - 1; i > 0; i--)
        {
            if (messages[i].Role == messages[i - 1].Role)
            {
                // 刪掉較舊的那一個
                messages.RemoveAt(i - 1);
            }
        }

        // 再套上總長度上限（最舊的先移除）
        while (messages.Count > MaxMessages)
        {
            messages.RemoveAt(0);
        }
    }

    public static void Clear()
    {
        if (Histories == null) return;
        Histories.Clear();
    }
}