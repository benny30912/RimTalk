using HarmonyLib;
using Verse;
using RimTalk.Service; // 用於 MemoryService

namespace RimTalk.Patch
{
    // [FIX] 確保在返回主選單時清理靜態狀態，防止背景任務崩潰或殘留
    [HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))]
    public static class MainMenuPatch
    {
        public static void Prefix()
        {
            // 取消所有記憶相關的異步任務並清空佇列
            MemoryService.Clear(keepSavedData: false);
        }
    }
}
