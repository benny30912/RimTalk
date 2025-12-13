using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimTalk.Service;
using RimWorld; // 用於 MessageTypeDefOf
using Verse;

// 解決 Logger 歧義
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Data;

public static class TalkHistory
{
    // ★ 修改：將常數改為 public 以便 UI 讀取
    public const int MaxMessages = 30;       //從 6 增加到 30，以保留更多的對話歷史記錄
    public const int MaxMediumMemories = 200; // 中期記憶上限
    public const int MaxLongMemories = 100;   // 長期記憶上限

    // 這些快取屬於運行時狀態，不需要存檔
    private static readonly ConcurrentDictionary<Guid, int> SpokenTickCache = new() { [Guid.Empty] = 0 };
    private static readonly ConcurrentBag<Guid> IgnoredCache = [];

    // 新增：任務取消 Token 來源
    private static CancellationTokenSource _cts = new();

    // 取得 WorldComponent 的捷徑
    private static RimTalkWorldComponent WorldComp => Find.World?.GetComponent<RimTalkWorldComponent>();

    // ★ 新增：主線程任務隊列
    private static readonly ConcurrentQueue<Action> _mainThreadActionQueue = new();

    // Add a new talk with the current game tick
    public static void AddSpoken(Guid id)
    {
        SpokenTickCache.TryAdd(id, GenTicks.TicksGame);
    }

    public static void AddIgnored(Guid id)
    {
        IgnoredCache.Add(id);
    }

    // ★ 新增：Update 方法，供 TickManager 調用
    public static void Update()
    {
        while (_mainThreadActionQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error executing main thread action: {ex}");
            }
        }
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

            // ★ 防禦性初始化
            record.Messages ??= new List<TalkMessageEntry>();

            // ★ 修改：加入訊息時記錄當前 Tick
            int currentTick = GenTicks.TicksGame;

            // 加入訊息
            record.Messages.Add(new TalkMessageEntry { Role = Role.User, Text = request, Tick = currentTick });
            record.Messages.Add(new TalkMessageEntry { Role = Role.AI, Text = response, Tick = currentTick });

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

                // ★ 修改：在主線程（Lock內）準備數據
                string existingKeywords = MemoryService.GetAllExistingKeywords(pawn);

                RunRetryableTask(
                    taskName: $"STM->MTM for {pawn.LabelShort}",
                    // ★ 修改：傳遞準備好的數據
                    action: () => MemoryService.SummarizeToMediumAsync(messagesSnapshot, pawn, existingKeywords, currentTick),
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

            // ★ 防禦性初始化：這是報錯的關鍵點
            record.MediumTermMemories ??= new List<MemoryRecord>();

            record.MediumTermMemories.AddRange(newMemories);
            record.NewMemoriesSinceLastArchival += newMemories.Count;

            // ★ 新增：成功提示
            Messages.Message("RimTalk.MemoryService.MediumMemoryCreated".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NeutralEvent, false);

            // 檢查是否觸發 MTM -> LTM 歸檔 (滿 200 條)
            if (record.NewMemoriesSinceLastArchival >= MaxMediumMemories)
            {
                // 建立當前 MTM 的快照
                var mtmSnapshot = record.MediumTermMemories.ToList();

                int countToRestore = record.NewMemoriesSinceLastArchival; // 暫存
                // 重置計數器
                record.NewMemoriesSinceLastArchival = 0;

                // ★ 修改：在主線程（Lock內）準備數據
                int currentTick = GenTicks.TicksGame;

                RunRetryableTask(
                    taskName: $"MTM->LTM for {pawn.LabelShort}",
                    // ★ 修改：傳遞準備好的數據
                    action: () => MemoryService.ConsolidateToLongAsync(mtmSnapshot, pawn, currentTick),
                    onSuccess: (longMemories) =>
                    {
                        if (!longMemories.NullOrEmpty())
                            OnLongMemoriesGenerated(pawn, longMemories, currentTick);
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

            while (record.MediumTermMemories.Count > MaxMediumMemories)
            {
                record.MediumTermMemories.RemoveAt(0);
            }
        }
    }

    // 處理生成的長期記憶
    // ★ 修改：增加 currentTick 參數
    private static void OnLongMemoriesGenerated(Pawn pawn, List<MemoryRecord> newMemories, int currentTick)
    {
        var comp = WorldComp;
        if (comp == null) return;

        lock (comp.SavedTalkHistories)
        {
            var record = comp.SavedTalkHistories.FirstOrDefault(x => x.Pawn == pawn);
            if (record == null) return;

            record.LongTermMemories ??= new List<MemoryRecord>();

            if (newMemories != null && newMemories.Any())
            {
                record.LongTermMemories.AddRange(newMemories);

                // ★ LTM 加權剔除維護 (直接使用傳遞進來的正確時間)
                MemoryService.PruneLongTermMemories(record.LongTermMemories, MaxLongMemories, currentTick);

                // ★ 新增：成功提示
                Messages.Message("RimTalk.MemoryService.LongMemoryCreated".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NeutralEvent, false);
            }
        }
    }

    // --- 輔助方法：任務重試與回滾邏輯 ---

    /// <summary>
    /// 執行一個帶有重試機制的異步任務。
    /// </summary>
    /// <param name="action">要執行的異步操作 (回傳結果)</param>
    /// <param name="onSuccess">成功時的回調</param>
    /// <param name="onFailureOrCancel">失敗或取消時的回調 (參數為是否因取消而結束)</param>
    // ★ 修改：RunRetryableTask 方法
    private static void RunRetryableTask<T>(
        string taskName,
        Func<Task<T>> action,
        Action<T> onSuccess,
        Action<bool> onFailureOrCancel,
        CancellationToken token)
    {
        Task.Run(async () =>
        {
            int maxRetries = 5;
            int attempt = 0;

            while (attempt < maxRetries && !token.IsCancellationRequested)
            {
                // ★ 新增：如果遊戲已經結束（回到主選單），直接中止任務
                if (Current.Game == null) return;

                try
                {
                    var result = await action();
                    // 根據目前 MemoryService 的設計，失敗會回傳 null 或空列表
                    bool isValid = result != null;
                    if (result is System.Collections.ICollection collection && collection.Count == 0)
                        isValid = false;

                    if (isValid)
                    {
                        // 再次檢查遊戲狀態，避免在退出瞬間寫入隊列
                        if (Current.Game != null)
                            // ★ 關鍵修改：將成功回調放入主線程隊列
                            _mainThreadActionQueue.Enqueue(() => onSuccess(result));
                        return;
                    }
                    else
                    {
                        // 這裡使用 Messages 是安全的，因為 Verse.Messages 內部有處理線程安全（或僅寫入隊列）
                        // 但為了保險，建議只用 Log
                        Logger.Warning($"Task {taskName} failed (attempt {attempt + 1}/{maxRetries}). Retrying...");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Exception in task {taskName}: {ex.Message}"); //發生異常改回錯誤日誌，方便檢查調用堆疊
                }

                attempt++;
                if (attempt < maxRetries)
                {
                    // 等候 1 分鐘
                    int delay = 1000 * 60;
                    try { await Task.Delay(delay, token); } catch (TaskCanceledException) { break; }
                }
            }

            // 如果遊戲結束，不需要執行失敗回調
            if (Current.Game == null) return;

            // ★ 關鍵修改：將失敗回調放入主線程隊列
            // 因為 onFailureOrCancel 裡面的 RestoreMessageCount 也會存取 WorldComp
            bool isCancelled = token.IsCancellationRequested;
            _mainThreadActionQueue.Enqueue(() => onFailureOrCancel(isCancelled));

            if (!isCancelled)
            {
                // 最終放棄 - 使用翻譯鍵
                Messages.Message(
                    "RimTalk.TalkHistory.TaskGiveUp".Translate(taskName, maxRetries),
                    MessageTypeDefOf.NeutralEvent,
                    false
                );
            }

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

            // 防禦性檢查
            if (record.Messages == null) return [];

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

        // ★ 新增：清理隊列
        while (_mainThreadActionQueue.TryDequeue(out _)) { }

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