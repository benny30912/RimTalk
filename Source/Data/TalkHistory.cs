using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Verse;
using System.Threading;
using System.Threading.Tasks;
using RimTalk.Service;
using RimWorld;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Data;

public static class TalkHistory
{
    public const int MaxShortMemories = 30;   // 累積 30 條 STM 觸發總結
    public const int MaxMediumMemories = 200; // 累積 200 條 MTM 觸發歸檔
    public const int MaxLongMemories = 100;   // LTM 數量上限

    private static readonly ConcurrentDictionary<int, List<(Role role, string message)>> MessageHistory = new();
    private static readonly ConcurrentDictionary<Guid, int> SpokenTickCache = new() { [Guid.Empty] = 0 };
    // 1. 移除 IgnoredCache 的 readonly (因為需要重新 new)
    private static ConcurrentBag<Guid> IgnoredCache = []; // 加回 volatile 更好，但 lock 也夠用

    // 任務取消 Token 與主線程隊列
    private static CancellationTokenSource _cts = new();
    private static readonly ConcurrentQueue<Action> _mainThreadActionQueue = new();

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

    // 必須在 GameComponent.Update 或類似位置調用以執行回調
    public static void Update()
    {
        while (_mainThreadActionQueue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Logger.Error($"Main thread action error: {ex}"); }
        }
    }

    // 當 TalkService 完成一輪對話後調用此方法
    // 參數 memory 是本次對話生成的 Summary (STM)
    public static void AddTalkResult(Pawn pawn, MemoryRecord memory)
    {
        // 加入記憶系統 (STM) 並檢查觸發條件
        // MemoryService.AddMemory 負責加入與計數
        bool thresholdReached = MemoryService.AddMemory(pawn, memory);

        if (thresholdReached)
        {
            TriggerStmToMtmSummary(pawn);
        }
    }

    // 觸發 STM -> MTM 流程
    private static void TriggerStmToMtmSummary(Pawn pawn)
    {
        var comp = WorldComp;
        if (comp == null) return;

        PawnMemoryData data;

        // 1. 安全地獲取 data
        lock (comp.PawnMemories)
        {
            if (!comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out data)) return;
        }

        // 2. 鎖定 data 進行快照 (與 AddMemory 互斥)
        lock (data)
        {
            // 建立快照
            var stmSnapshot = data.ShortTermMemories.ToList();
            int countToRestore = data.NewShortMemoriesSinceSummary;
            data.NewShortMemoriesSinceSummary = 0; // 重置計數
                                                   // 準備參數 (Fallback Tick)
            int currentTick = GenTicks.TicksGame;
            RunRetryableTask(
                taskName: $"STM->MTM for {pawn.LabelShort}",
                action: () => MemoryService.SummarizeToMediumAsync(stmSnapshot, pawn, currentTick),
                onSuccess: (newMemories) =>
                {
                    if (!newMemories.NullOrEmpty())
                        OnMediumMemoriesGenerated(pawn, newMemories);
                },
                onFailureOrCancel: (isCancelled) =>
                {
                    if (!isCancelled)
                    {
                        // 失敗回滾計數器，以便下次對話再次觸發
                        RestoreShortMemoryCount(pawn, countToRestore);
                    }
                },
                token: _cts.Token
            );
        }
    }

    // MTM 生成回調
    private static void OnMediumMemoriesGenerated(Pawn pawn, List<MemoryRecord> newMemories)
    {
        var comp = WorldComp;
        if (comp == null || !comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data)) return;
        lock (data)
        {
            data.MediumTermMemories ??= [];
            data.MediumTermMemories.AddRange(newMemories);
            data.NewMediumMemoriesSinceArchival += newMemories.Count;

            Messages.Message("RimTalk.MemoryService.MediumMemoryCreated".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NeutralEvent, false);
            // 檢查 MTM -> LTM 閾值
            if (data.NewMediumMemoriesSinceArchival >= MaxMediumMemories)
            {
                TriggerMtmToLtmConsolidation(pawn, data);
            }
            // 簡單維護 MTM 列表長度 (移除最舊的)
            // 注意：這裡直接移除可能影響正在進行的合併，但由於是異步快照，只要不移除剛生成的就還好
            // 建議保留一定緩衝區
            while (data.MediumTermMemories.Count > MaxMediumMemories * 1.5)
            {
                data.MediumTermMemories.RemoveAt(0);
            }
        }
    }

    // 觸發 MTM -> LTM 流程
    private static void TriggerMtmToLtmConsolidation(Pawn pawn, PawnMemoryData data)
    {
        // 建立快照
        var mtmSnapshot = data.MediumTermMemories.ToList();
        int countToRestore = data.NewMediumMemoriesSinceArchival;
        data.NewMediumMemoriesSinceArchival = 0;
        int currentTick = GenTicks.TicksGame;
        RunRetryableTask(
            taskName: $"MTM->LTM for {pawn.LabelShort}",
            action: () => MemoryService.ConsolidateToLongAsync(mtmSnapshot, pawn, currentTick),
            onSuccess: (longMemories) =>
            {
                if (!longMemories.NullOrEmpty())
                    OnLongMemoriesGenerated(pawn, longMemories, currentTick);
            },
            onFailureOrCancel: (isCancelled) =>
            {
                if (!isCancelled) RestoreMediumMemoryCount(pawn, countToRestore);
            },
            token: _cts.Token
        );
    }

    // LTM 生成回調
    private static void OnLongMemoriesGenerated(Pawn pawn, List<MemoryRecord> newMemories, int currentTick)
    {
        var comp = WorldComp;
        if (comp == null || !comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data)) return;
        lock (data)
        {
            data.LongTermMemories ??= [];
            data.LongTermMemories.AddRange(newMemories);
            // 執行權重剔除
            MemoryService.PruneLongTermMemories(data.LongTermMemories, MaxLongMemories, currentTick);

            Messages.Message("RimTalk.MemoryService.LongMemoryCreated".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NeutralEvent, false);
        }
    }

    // --- 輔助方法 (異步任務、回滾、清理) ---
    private static void RestoreShortMemoryCount(Pawn pawn, int count)
    {
        // 邏輯同 Legacy，鎖定並加回 NewShortMemoriesSinceSummary
        var comp = WorldComp;
        if (comp != null && comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data))
        {
            data.NewShortMemoriesSinceSummary += count;
        }
    }
    private static void RestoreMediumMemoryCount(Pawn pawn, int count)
    {
        var comp = WorldComp;
        if (comp != null && comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data))
        {
            data.NewMediumMemoriesSinceArchival += count;
        }
    }

    // RunRetryableTask 方法與 Legacy 版本相同 (包含重試、Cancel Token、主線程回調)
    // 請直接沿用之前分析過的 RunRetryableTask 代碼，無需變動
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

    public static void AddMessageHistory(Pawn pawn, string request, string response)
    {
        var messages = MessageHistory.GetOrAdd(pawn.thingIDNumber, _ => []);

        lock (messages)
        {
            messages.Add((Role.User, request));
            messages.Add((Role.AI, response));
            EnsureMessageLimit(messages);
        }
    }

    public static List<(Role role, string message)> GetMessageHistory(Pawn pawn)
    {
        if (!MessageHistory.TryGetValue(pawn.thingIDNumber, out var history))
            return [];
            
        lock (history)
        {
            return [..history];
        }
    }

    private static void EnsureMessageLimit(List<(Role role, string message)> messages)
    {
        // First, ensure alternating pattern by removing consecutive duplicates from the end
        for (int i = messages.Count - 1; i > 0; i--)
        {
            if (messages[i].role == messages[i - 1].role)
            {
                // Remove the earlier message of the consecutive pair
                messages.RemoveAt(i - 1);
            }
        }

        // Then, enforce the maximum message limit by removing the oldest messages
        int maxMessages = Settings.Get().Context.ConversationHistoryCount;
        while (messages.Count > maxMessages * 2)
        {
            messages.RemoveAt(0);
        }
    }

    // 修改 Clear 方法簽名與邏輯
    public static void Clear(bool keepSavedData = false)
    {
        // 1. 先停止所有異步任務，減少競爭
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        while (_mainThreadActionQueue.TryDequeue(out _)) { }

        // 2. 清理 UI 暫存與快取
        MessageHistory.Clear();
        SpokenTickCache.Clear();
        // ConcurrentBag 無 Clear 方法，且為了安全直接換新的
        IgnoredCache = [];

        // 僅在非保留模式下清除 WorldComponent
        if (!keepSavedData)
        {
            var comp = WorldComp;
            if (comp != null)
            {
                // 3. 加鎖清除 (防止背景剛好在寫入)
                // 注意：這裡鎖的是 PawnMemories (Dictionary)，與 AddMemory 的第一層鎖對應
                lock (comp.PawnMemories)
                {
                    comp.PawnMemories.Clear();
                }
            }
        }
    }
}
