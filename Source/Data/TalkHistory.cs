using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimTalk.Data;

public static class TalkHistory
{
    private const int MaxMessages = 30; //從 6 增加到 30，以保留更多的對話歷史記錄

    // 這些快取屬於運行時狀態，不需要存檔
    private static readonly ConcurrentDictionary<Guid, int> SpokenTickCache = new() { [Guid.Empty] = 0 };
    private static readonly ConcurrentBag<Guid> IgnoredCache = [];

    // 取得 WorldComponent 的捷徑
    private static RimTalkWorldComponent WorldComp => Find.World?.GetComponent<RimTalkWorldComponent>();
    
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
        var comp = WorldComp;
        if (pawn == null || comp == null) return;

        lock (comp.SavedTalkHistories)
        {
            // 查找該 Pawn 的紀錄，若無則新增
            var record = comp.SavedTalkHistories.FirstOrDefault(x => x.Pawn == pawn);
            if (record == null)
            {
                record = new PawnMessageHistoryRecord { Pawn = pawn };
                comp.SavedTalkHistories.Add(record);
            }

            // 加入訊息
            record.Messages.Add(new TalkMessageEntry { Role = Role.User, Text = request });
            record.Messages.Add(new TalkMessageEntry { Role = Role.AI, Text = response });
            
            EnsureMessageLimit(record.Messages);
        }
    }

    public static List<(Role role, string message)> GetMessageHistory(Pawn pawn)
    {
        var comp = WorldComp;
        if (pawn == null || comp == null) return [];

        lock (comp.SavedTalkHistories)
        {
            var record = comp.SavedTalkHistories.FirstOrDefault(x => x.Pawn == pawn);
            if (record == null) return [];

            // 轉換回原本的 Tuple 格式以相容其他代碼
            return record.Messages.Select(m => (m.Role, m.Text)).ToList();
        }
    }

    private static void EnsureMessageLimit(List<TalkMessageEntry> messages)
    {
        // First, ensure alternating pattern by removing consecutive duplicates from the end
        for (int i = messages.Count - 1; i > 0; i--)
        {
            if (messages[i].Role == messages[i - 1].Role)
            {
                // Remove the earlier message of the consecutive pair
                messages.RemoveAt(i - 1);
            }
        }

        // Then, enforce the maximum message limit by removing the oldest messages
        while (messages.Count > MaxMessages)
        {
            messages.RemoveAt(0);
        }
    }

    public static void Clear()
    {
        // 清理運行時快取
        SpokenTickCache.Clear();
        IgnoredCache.Clear(); // 重新生成 ConcurrentBag
        
        // 清理 WorldComponent 中的資料 (通常用於新遊戲或主選單重置)
        var comp = WorldComp;
        if (comp != null)
        {
            lock (comp.SavedTalkHistories)
            {
                comp.SavedTalkHistories.Clear();
            }
        }
    }
}