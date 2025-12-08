using System;
using System.Threading.Tasks;
using RimTalk.Client; // 需要
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Error;

public static class AIErrorHandler
{
    private static bool _quotaWarningShown;

    // ★ 修改：將 ClientType 改為 bool isMemory (預設 false)
    public static async Task<T> HandleWithRetry<T>(Func<Task<T>> operation, bool isMemory = false)
    {
        try
        {
            T result = await operation();
            return result;
        }
        catch (Exception ex)
        {
            var settings = Settings.Get();

            // 嘗試切換到下一個 Config
            if (CanRetryGeneration(settings, isMemory)) // 傳入 isMemory 標記
            {
                // ★ 恢復 ShowRetryMessage 邏輯
                if (!settings.UseSimpleConfig)
                {
                    string nextModel;
                    if (isMemory)
                    {
                        var config = settings.GetActiveMemoryConfig();
                        nextModel = config?.SelectedModel == "Custom" ? config.CustomModelName : (config?.SelectedModel ?? "Unknown");
                    }
                    else
                    {
                        nextModel = settings.GetCurrentModel();
                    }

                    ShowRetryMessage(ex, nextModel);
                }

                try
                {
                    // 重試操作
                    T result = await operation();
                    return result;
                }
                catch (Exception retryEx)
                {
                    Logger.Warning($"Retry failed: {retryEx.Message}");
                    HandleFinalFailure(ex);
                    return default;
                }
            }

            HandleFinalFailure(ex);
            return default;
        }
    }

    // ★ 修改：根據 isMemory 決定輪替哪組 Config
    private static bool CanRetryGeneration(RimTalkSettings settings, bool isMemory)
    {
        if (isMemory)
        {
            // 如果沒啟用獨立記憶，就不能在這裡重試記憶列表
            if (!settings.EnableMemoryModel) return false;

            int originalIndex = settings.CurrentMemoryConfigIndex;
            settings.TryNextMemoryConfig();
            return settings.CurrentMemoryConfigIndex != originalIndex;
        }
        else // 對話生成 (Dialogue)
        {
            if (settings.UseSimpleConfig)
            {
                if (settings.IsUsingFallbackModel) return false;
                settings.IsUsingFallbackModel = true;
                return true;
            }

            if (!settings.UseCloudProviders) return false;
            int originalIndex = settings.CurrentCloudConfigIndex;
            settings.TryNextConfig();
            return settings.CurrentCloudConfigIndex != originalIndex;
        }
    }

    private static void HandleFinalFailure(Exception ex)
    {
        if (ex is QuotaExceededException)
        {
            ShowQuotaWarning(ex);
        }
        else
        {
            ShowGenerationWarning(ex);
        }
    }

    public static void ResetQuotaWarning()
    {
        _quotaWarningShown = false;
    }

    private static void ShowQuotaWarning(Exception ex)
    {
        if (!_quotaWarningShown)
        {
            _quotaWarningShown = true;
            string message = "RimTalk.TalkService.QuotaExceeded".Translate();
            Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
            Logger.Warning(ex.Message);
        }
    }

    private static void ShowGenerationWarning(Exception ex)
    {
        Logger.Warning(ex.StackTrace);
        string message = $"{"RimTalk.TalkService.GenerationFailed".Translate()}: {ex.Message}";
        Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
    }

    private static void ShowRetryMessage(Exception ex, string nextModel)
    {
        string messageKey = ex is QuotaExceededException ? "RimTalk.TalkService.QuotaReached" : "RimTalk.TalkService.APIError";
        string message = $"{messageKey.Translate()}. {"RimTalk.TalkService.TryingNextAPI".Translate(nextModel)}";
        Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
    }
}