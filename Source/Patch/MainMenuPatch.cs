using HarmonyLib;
using Verse;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))]
public static class MainMenuPatch
{
    public static void Prefix()
    {
        // 當玩家返回主選單時，強制重置 RimTalk 狀態
        // 這會觸發 TalkHistory.CancelAllTasks()，取消所有正在運行的背景任務
        RimTalk.Reset();
    }
}