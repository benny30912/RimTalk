using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimTalk.Service;
using Verse;

// 解決 Logger 歧義
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Data;

public static class TalkHistory
{
    private const int MaxMessages = 30;       //從 6 增加到 30，以保留更多的對話歷史記錄
    private const int MaxMediumMemories = 200; // 中期記憶上限
    private const int MaxLongMemories = 100;   // 長期記憶上限

    // 這些快取屬於運行時狀態，不需要存檔
    private static readonly ConcurrentDictionary<Guid, int> SpokenTickCache = new() { [Guid.Empty] = 0 };
    private static readonly ConcurrentBag<Guid> IgnoredCache = [];

    // 新增：任務取消 Token 來源
    private static CancellationTokenSource _cts = new();

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

    public static void CancelAllTasks()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
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

            // 更新計數器 (新增 2 條)
            record.NewMessagesSinceLastSummary += 2;

            // 檢查是否觸發 STM -> MTM 總結 (滿 30 條)
            if (record.NewMessagesSinceLastSummary >= MaxMessages)
            {
                // 建立當前訊息的快照供異步任務使用
                var messagesSnapshot = record.Messages.ToList();

                int countToRestore = record.NewMessagesSinceLastSummary; // 暫存當前計數
                // 重置計數器
                record.NewMessagesSinceLastSummary = 0;

                // 2. 執行重試任務
                RunRetryableTask(
                    taskName: $"STM->MTM for {pawn.LabelShort}",
                    action: () => MemoryService.SummarizeToMediumAsync(messagesSnapshot, pawn),
                    onSuccess: (newMemories) =>
                    {
                        if (!newMemories.NullOrEmpty())
                            OnMediumMemoriesGenerated(pawn, newMemories);
                    },
                    onFailureOrCancel: (isCancelled) =>
                    {
                        // 如果不是因為遊戲重置導致的取消，則執行回滾 (Rollback)
                        if (!isCancelled)
                        {
                            RestoreMessageCount(pawn, countToRestore);
                        }
                    },
                    token: _cts.Token
                );
            }

            EnsureMessageLimit(record.Messages);
        }
    }

    // 處理生成的中期記憶
    private static void OnMediumMemoriesGenerated(Pawn pawn, List<MemoryRecord> newMemories)
    {
        var comp = WorldComp;
        if (comp == null) return;

        lock (comp.SavedTalkHistories)
        {
            var record = comp.SavedTalkHistories.FirstOrDefault(x => x.Pawn == pawn);
            if (record == null) return;

            record.MediumTermMemories.AddRange(newMemories);
            record.NewMemoriesSinceLastArchival += newMemories.Count;

            // 檢查是否觸發 MTM -> LTM 歸檔 (滿 200 條)
            if (record.NewMemoriesSinceLastArchival >= MaxMediumMemories)
            {
                // 建立當前 MTM 的快照
                var mtmSnapshot = record.MediumTermMemories.ToList();

                int countToRestore = record.NewMemoriesSinceLastArchival; // 暫存
                // 重置計數器
                record.NewMemoriesSinceLastArchival = 0;

                // 2. 執行重試任務
                RunRetryableTask(
                    taskName: $"MTM->LTM for {pawn.LabelShort}",
                    action: () => MemoryService.ConsolidateToLongAsync(mtmSnapshot, pawn),
                    onSuccess: (longMemories) =>
                    {
                        if (!longMemories.NullOrEmpty())
                            OnLongMemoriesGenerated(pawn, longMemories);
                    },
                    onFailureOrCancel: (isCancelled) =>
                    {
                        if (!isCancelled)
                        {
                            RestoreMemoryCount(pawn, countToRestore);
                        }
                    },
                    token: _cts.Token
                );
            }

            // MTM FIFO 維護：移除最舊的記憶直到滿足上限
            while (record.MediumTermMemories.Count > MaxMediumMemories)
            {
                record.MediumTermMemories.RemoveAt(0);
            }
        }
    }

    // 處理生成的長期記憶
    private static void OnLongMemoriesGenerated(Pawn pawn, List<MemoryRecord> newMemories)
    {
        var comp = WorldComp;
        if (comp == null) return;

        lock (comp.SavedTalkHistories)
        {
            var record = comp.SavedTalkHistories.FirstOrDefault(x => x.Pawn == pawn);
            if (record == null) return;

            record.LongTermMemories.AddRange(newMemories);

            // LTM 加權剔除維護
            MemoryService.PruneLongTermMemories(record.LongTermMemories, MaxLongMemories);
        }
    }

    // --- 輔助方法：任務重試與回滾邏輯 ---

    /// <summary>
    /// 執行一個帶有重試機制的異步任務。
    /// </summary>
    /// <param name="action">要執行的異步操作 (回傳結果)</param>
    /// <param name="onSuccess">成功時的回調</param>
    /// <param name="onFailureOrCancel">失敗或取消時的回調 (參數為是否因取消而結束)</param>
    private static void RunRetryableTask<T>(
        string taskName,
        Func<Task<T>> action,
        Action<T> onSuccess,
        Action<bool> onFailureOrCancel,
        CancellationToken token)
    {
        Task.Run(async () =>
        {
            int maxRetries = 10;
            int attempt = 0;

            while (attempt < maxRetries && !token.IsCancellationRequested)
            {
                try
                {
                    var result = await action();
                    // 根據目前 MemoryService 的設計，失敗會回傳 null 或空列表
                    bool isValid = result != null;
                    if (result is System.Collections.ICollection collection && collection.Count == 0)
                        isValid = false;

                    if (isValid)
                    {
                        onSuccess(result);
                        return; // 成功，結束任務
                    }
                    else
                    {
                        // 邏輯失敗 (例如 LLM 回傳格式錯誤或拒絕生成)
                        Logger.Warning($"Task {taskName} failed (attempt {attempt + 1}/{maxRetries}). Retrying...");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Exception in task {taskName}: {ex.Message}");
                }

                attempt++;
                if (attempt < maxRetries)
                {
                    // 等候 1 分鐘
                    int delay = 1000 * 60;
                    try { await Task.Delay(delay, token); } catch (TaskCanceledException) { break; }
                }
            }

            // 如果重試次數用盡或被取消，執行失敗/回滾回調
            onFailureOrCancel(token.IsCancellationRequested);

            if (!token.IsCancellationRequested)
                Logger.Warning($"Task {taskName} gave up after {maxRetries} attempts. Rolling back state.");

        }, token);
    }

    // 回滾 STM 計數器
    private static void RestoreMessageCount(Pawn pawn, int count)
    {
        var comp = WorldComp;
        if (comp == null) return;
        lock (comp.SavedTalkHistories)
        {
            var record = comp.SavedTalkHistories.FirstOrDefault(x => x.Pawn == pawn);
            if (record != null)
            {
                record.NewMessagesSinceLastSummary += count;
            }
        }
    }

    // 回滾 MTM 計數器
    private static void RestoreMemoryCount(Pawn pawn, int count)
    {
        var comp = WorldComp;
        if (comp == null) return;
        lock (comp.SavedTalkHistories)
        {
            var record = comp.SavedTalkHistories.FirstOrDefault(x => x.Pawn == pawn);
            if (record != null)
            {
                record.NewMemoriesSinceLastArchival += count;
            }
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
        CancelAllTasks(); // 確保清除時也取消任務

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