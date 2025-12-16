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
        // 新遊戲：徹底重置
        Reset(soft: false, keepSavedData: false);
    }

    public override void LoadedGame()
    {
        base.LoadedGame();
        // 讀檔：保留 WorldData (記憶)
        Reset(soft: false, keepSavedData: true);
    }

    // 增加 keepSavedData 參數
    public static void Reset(bool soft = false, bool keepSavedData = false)
    {
        var settings = Settings.Get();
        if (settings != null)
        {
            settings.CurrentCloudConfigIndex = 0;
        }

        AIErrorHandler.ResetQuotaWarning();
        TickManagerPatch.Reset();
        AIClientFactory.Clear();
        AIService.Clear();

        PatchThoughtHandlerGetDistinctMoodThoughtGroups.Clear();
        Cache.GetAll().ToList().ForEach(pawnState => pawnState.IgnoreAllTalkResponses());
        Cache.InitializePlayerPawn();

        if (soft) return;

        Counter.Tick = 0;
        Cache.Clear();
        Stats.Reset();
        TalkRequestPool.Clear();
        ApiHistory.Clear();
        TalkHistory.Clear();
        MemoryService.Clear(keepSavedData); // [NEW] 單獨呼叫
    }
}
