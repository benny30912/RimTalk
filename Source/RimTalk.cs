using System.Linq;
using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Patch;
using RimTalk.Service;
using Verse;

namespace RimTalk;

public class RimTalk : GameComponent
{
    public RimTalk(Game game)
    {
    }

    public override void StartedNewGame()
    {
        base.StartedNewGame();
        Reset(); // 硬重置
    }

    public override void LoadedGame()
    {
        base.LoadedGame();
        Reset(soft: true); // 軟重置：保留從存檔載入的 TalkHistory
    }

    public static void Reset(bool soft = false)
    {
        var settings = Settings.Get();
        if (settings != null)
        {
            settings.CurrentCloudConfigIndex = 0;
        }

        // 1. 取消所有正在進行的背景任務 (新增)
        TalkHistory.CancelAllTasks();
        PersonaService.CancelAllRetries();

        AIErrorHandler.ResetQuotaWarning();
        TickManagerPatch.Reset();
        AIClientFactory.Clear();
        AIService.Clear();

        // Debug 用的 ApiHistory 和 TalkRequest 都是暫時的，無論如何都清理
        ApiHistory.Clear();
        TalkRequestPool.Clear();

        PatchThoughtHandlerGetDistinctMoodThoughtGroups.Clear();
        Cache.GetAll().ToList().ForEach(pawnState => pawnState.IgnoreAllTalkResponses());
        Cache.InitializePlayerPawn();

        // 快取一定要清，因為 Pawn 實例在讀檔後會改變
        Cache.Clear();
        Counter.Tick = 0;
        Stats.Reset();

        if (soft) return;

        // 如果不是軟重置 (即新遊戲或回到主選單)，才徹底清除對話歷史
        // 如果是讀檔 (soft=true)，TalkHistory 會由 WorldComponent.ExposeData 自動載入，所以這裡不清除
        TalkHistory.Clear();
    }
}