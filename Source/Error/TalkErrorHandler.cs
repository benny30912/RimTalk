using System;
using System.Threading.Tasks;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Error;

public static class AIErrorHandler
{
    private static bool _quotaWarningShown;

    // [MODIFY] Add optional parameter isMemoryOperation
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
            // [MODIFY] Pass isMemoryOperation
            if (CanRetryGeneration(settings, isMemory))
            {
                // [MODIFY] Get correct model name for message
                string nextModel = isMemory
                    ? settings.GetActiveMemoryConfig()?.SelectedModel ?? "Unknown"
                    : settings.GetCurrentModel();

                if (!settings.UseSimpleConfig)
                {
                    ShowRetryMessage(ex, nextModel);
                }

                try
                {
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

    // [MODIFY] Update logic to rotate correct config
    private static bool CanRetryGeneration(RimTalkSettings settings, bool isMemory)
    {
        if (settings.UseSimpleConfig)
        {
            // Simple Config 不支援 Memory Model 輪替 (假設用同一把 Key)
            if (settings.IsUsingFallbackModel) return false;
            settings.IsUsingFallbackModel = true;
            return true;
        }

        if (!settings.UseCloudProviders) return false;

        if (isMemory && settings.EnableMemoryModel)
        {
            // [NEW] Rotate Memory Config
            int originalIndex = settings.CurrentMemoryConfigIndex;
            settings.TryNextMemoryConfig();
            return settings.CurrentMemoryConfigIndex != originalIndex;
        }
        else
        {
            // Rotate Chat Config
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
