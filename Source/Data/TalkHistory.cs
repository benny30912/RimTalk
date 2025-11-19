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

    //（可選）給將來用的 hook，或你直接在下面寫死也可以
    public static event Action<Pawn, List<(Role role, string message)>> OnHistoryBatchReady;

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
        int beforeCount = list.Count;   // 新增前的長度

        // 照舊加兩條：User + AI
        list.Add(new TalkMessageEntry { Role = Role.User, Text = request });
        list.Add(new TalkMessageEntry { Role = Role.AI, Text = response });

        EnsureMessageLimit(list);

        // 先更新 batch 計數，再決定要不要做事
        int added = list.Count - beforeCount; // 通常是 2，但這樣寫比較保險
        ProcessBatchIfNeeded(record, added);

    }
    private static void ProcessBatchIfNeeded(PawnMessageHistoryRecord record, int added)
    {
        if (record == null || added <= 0) return;
        if (record.Messages == null || record.Messages.Count == 0) return;

        // 累積這個 pawn 自上次觸發之後新增了多少條
        record.PendingMessagesSinceLastBatch += added;

        // 可能一次跨過多個 MaxMessages（雖然你現在一次只加 2，但這樣寫比較通用）
        while (record.PendingMessagesSinceLastBatch >= MaxMessages)
        {
            record.PendingMessagesSinceLastBatch -= MaxMessages;

            var list = record.Messages;

            // 取目前歷史中的最後 MaxMessages 條，當成這次要丟給 Persona 的 batch
            int startIndex = list.Count - MaxMessages;
            if (startIndex < 0) startIndex = 0;

            var batch = new List<(Role role, string message)>();
            for (int i = startIndex; i < list.Count; i++)
            {
                var m = list[i];
                batch.Add((m.Role, m.Text));
            }

            OnHistoryBatchReady?.Invoke(record.Pawn, batch);
        }
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