using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Patch;
using RimTalk.Service;
using RimTalk.Source.Memory;
using RimTalk.Vector;
using System;
using System.IO;
using System.Linq;
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

        // [NEW] 確保取消所有 Persona 生成任務
        PersonaService.CancelAllRetries();

        PatchThoughtHandlerGetDistinctMoodThoughtGroups.Clear();
        Cache.GetAll().ToList().ForEach(pawnState => pawnState.IgnoreAllTalkResponses());
        Cache.InitializePlayerPawn();

        if (soft) return;

        // [MODIFY] 延遲載入向量服務 + 初始化佇列服務
        try
        {
            // 取得 RimTalk Mod 根目錄
            var mod = LoadedModManager.RunningModsListForReading
                .FirstOrDefault(m => m.PackageIdPlayerFacing.ToLower() == "cj.rimtalk");
            if (mod != null)
            {
                string modelPath = Path.Combine(mod.RootDir, "Resources", "Model", "bge-base-zh-v1.5_int8.onnx");
                string vocabPath = Path.Combine(mod.RootDir, "Resources", "Model", "vocab.txt");

                // 設定模型路徑（供延遲載入使用）
                VectorService.Instance.SetModelPaths(modelPath, vocabPath);

                // 根據設定決定是否立即初始化
                if (!Settings.Get().UseCloudVectorService && !VectorService.Instance.IsInitialized)
                {
                    VectorService.Instance.Initialize(modelPath, vocabPath);
                }

                // 切換模型時初始化佇列服務
                VectorQueueService.Instance.OnModeChanged(Settings.Get().UseCloudVectorService);
            }
            else
            {
                Log.Warning("[RimTalk] Cannot find RimTalk mod for VectorService initialization.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[RimTalk] VectorService initialization failed: {ex.Message}");
        }

        // [NEW] 清空向量請求佇列
        VectorQueueService.Instance.Clear();

        Counter.Tick = 0;
        Cache.Clear();
        Stats.Reset();
        TalkRequestPool.Clear();
        ApiHistory.Clear();
        TalkHistory.Clear();
        MemoryService.Clear(keepSavedData); // [NEW] 單獨呼叫
    }
}
