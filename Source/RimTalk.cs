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

        if (!soft)
        {
            TalkHistory.Clear();
        }

        AIErrorHandler.ResetQuotaWarning();
        TickManagerPatch.Reset();
        AIClientFactory.Clear();
        AIService.Clear();
        PatchThoughtHandlerGetDistinctMoodThoughtGroups.Clear();
        Cache.GetAll().ToList().ForEach(pawnState => pawnState.IgnoreAllTalkResponses());

        if (soft) return;

        Counter.Tick = 0;
        Cache.Clear();
        Stats.Reset();
        TalkRequestPool.Clear();
        ApiHistory.Clear();
    }
}