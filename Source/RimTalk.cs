using System.Linq;
using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Patch;
using RimTalk.Service;
using Verse;

namespace RimTalk;

public enum ButtonDisplayMode
{
    Tab,
    Toggle,
    None
}

public class RimTalk : GameComponent
{
    public RimTalk(Game game)
    {
    }

    public override void StartedNewGame()
    {
        base.StartedNewGame();
        Reset();
    }

    public override void LoadedGame()
    {
        base.LoadedGame();
        Reset(soft: true);
    }

    public static void Reset(bool soft = false)
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

        // 讓現有 pawn 把排隊回應全部丟棄
        Cache.GetAll().ToList().ForEach(pawnState => pawnState.IgnoreAllTalkResponses());

        Cache.Clear();           // PawnState 快取，裡面直指 Pawn，世界換了就全是舊人
        ApiHistory.Clear();      //都清掉上一輪的 API 紀錄
        TalkRequestPool.Clear(); // 排隊中的 TalkRequest，裡面也會綁 Pawn/Map 狀態

        if (soft) return;

        TalkHistory.Clear();     // 只有完全重置時才清跨存檔的訊息歷史
        Counter.Tick = 0;        // 給統計用的時間基準
        Stats.Reset();           // 呼叫次數 / token 統計，跟 ApiHistory 一樣是本世界的 debug 狀態
    }
}