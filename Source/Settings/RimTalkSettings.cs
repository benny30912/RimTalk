using System.Collections.Generic;
using RimTalk.Data;
using UnityEngine;
using Verse;

namespace RimTalk;

public class RimTalkSettings : ModSettings
{
    public List<ApiConfig> CloudConfigs = [];
    public int CurrentCloudConfigIndex = 0;
    public ApiConfig LocalConfig = new() { Provider = AIProvider.Local };

    // ★ 修改：改為列表與獨立開關
    public bool EnableMemoryModel = false;
    public List<ApiConfig> MemoryConfigs = [];
    public int CurrentMemoryConfigIndex = 0;

    // ... (其他欄位保持不變)
    public bool UseCloudProviders = true;
    public bool UseSimpleConfig = true;
    public string SimpleApiKey = "";
    public bool IsUsingFallbackModel = false;
    public bool IsEnabled = true;
    public int TalkInterval = 7;
    public const int ReplyInterval = 2;
    public bool ProcessNonRimTalkInteractions;
    public bool AllowSimultaneousConversations;
    public string CustomInstruction = "";
    public Dictionary<string, bool> EnabledArchivableTypes = new();
    public bool DisplayTalkWhenDrafted = true;
    public bool AllowMonologue = true;
    public bool AllowSlavesToTalk = true;
    public bool AllowPrisonersToTalk = true;
    public bool AllowOtherFactionsToTalk = false;
    public bool AllowEnemiesToTalk = false;
    public bool AllowCustomConversation = true;
    public Settings.PlayerDialogueMode PlayerDialogueMode = Settings.PlayerDialogueMode.Manual;
    public string PlayerName = "Player";
    public bool ContinueDialogueWhileSleeping = false;
    public bool AllowBabiesToTalk = true;
    public bool AllowNonHumanToTalk = true;
    public bool ApplyMoodAndSocialEffects = false;
    public int DisableAiAtSpeed = 0;
    public Settings.ButtonDisplayMode ButtonDisplay = Settings.ButtonDisplayMode.Tab;
    public float MemoryImportanceWeight = 3.0f;
    // ★ 新增：關鍵字權重 (預設 2.0)
    public float KeywordWeight = 2.0f;

    public ContextSettings Context = new();

    // Debug & Overlay settings...
    public bool DebugModeEnabled = false;
    public bool DebugGroupingEnabled = false;
    public string DebugSortColumn;
    public bool DebugSortAscending = true;
    public bool OverlayEnabled = false;
    public float OverlayOpacity = 0.5f;
    public float OverlayFontSize = 15f;
    public bool OverlayDrawAboveUI = true;
    public Rect OverlayRectDebug = new(200f, 200f, 600f, 450f);
    public Rect OverlayRectNonDebug = new(200f, 200f, 400f, 250f);

    // ... (GetActiveConfig 保持不變) ...
    public ApiConfig GetActiveConfig()
    {
        if (UseSimpleConfig)
        {
            if (!string.IsNullOrWhiteSpace(SimpleApiKey))
            {
                return new ApiConfig
                {
                    ApiKey = SimpleApiKey,
                    Provider = AIProvider.Google,
                    SelectedModel = IsUsingFallbackModel ? Constant.FallbackCloudModel : Constant.DefaultCloudModel,
                    IsEnabled = true
                };
            }
            return null;
        }

        if (UseCloudProviders)
        {
            if (CloudConfigs.Count == 0) return null;
            for (int i = 0; i < CloudConfigs.Count; i++)
            {
                int index = (CurrentCloudConfigIndex + i) % CloudConfigs.Count;
                var config = CloudConfigs[index];
                if (config.IsValid())
                {
                    CurrentCloudConfigIndex = index;
                    return config;
                }
            }
            return null;
        }
        else
        {
            if (LocalConfig != null && LocalConfig.IsValid()) return LocalConfig;
        }
        return null;
    }

    // ★ 新增：取得當前記憶 Config
    public ApiConfig GetActiveMemoryConfig()
    {
        if (!EnableMemoryModel || MemoryConfigs.Count == 0) return null;

        for (int i = 0; i < MemoryConfigs.Count; i++)
        {
            int index = (CurrentMemoryConfigIndex + i) % MemoryConfigs.Count;
            var config = MemoryConfigs[index];
            if (config.IsValid())
            {
                CurrentMemoryConfigIndex = index;
                return config;
            }
        }
        return null;
    }

    // ... (TryNextConfig 保持不變) ...
    public void TryNextConfig()
    {
        if (CloudConfigs.Count <= 1) return;
        int originalIndex = CurrentCloudConfigIndex;
        for (int i = 1; i < CloudConfigs.Count; i++)
        {
            int nextIndex = (originalIndex + i) % CloudConfigs.Count;
            var config = CloudConfigs[nextIndex];
            if (config.IsValid())
            {
                CurrentCloudConfigIndex = nextIndex;
                Write();
                return;
            }
        }
        Write();
    }

    // ★ 新增：切換下一個記憶 Config
    public void TryNextMemoryConfig()
    {
        if (MemoryConfigs.Count <= 1) return;
        int originalIndex = CurrentMemoryConfigIndex;
        for (int i = 1; i < MemoryConfigs.Count; i++)
        {
            int nextIndex = (originalIndex + i) % MemoryConfigs.Count;
            var config = MemoryConfigs[nextIndex];
            if (config.IsValid())
            {
                CurrentMemoryConfigIndex = nextIndex;
                Write();
                return;
            }
        }
        Write();
    }

    // ... (GetCurrentModel 保持不變) ...
    public string GetCurrentModel()
    {
        var activeConfig = GetActiveConfig();
        if (activeConfig == null) return Constant.DefaultCloudModel;
        return activeConfig.SelectedModel == "Custom" ? activeConfig.CustomModelName : activeConfig.SelectedModel;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref CloudConfigs, "cloudConfigs", LookMode.Deep);
        Scribe_Deep.Look(ref LocalConfig, "localConfig");

        // ★ 修改：保存新的記憶設定
        Scribe_Values.Look(ref EnableMemoryModel, "enableMemoryModel", false);
        Scribe_Collections.Look(ref MemoryConfigs, "memoryConfigs", LookMode.Deep);

        Scribe_Values.Look(ref UseCloudProviders, "useCloudProviders", true);
        Scribe_Values.Look(ref UseSimpleConfig, "useSimpleConfig", true);
        // ... (其他 Scribe 保持不變)
        Scribe_Values.Look(ref SimpleApiKey, "simpleApiKey", "");
        Scribe_Values.Look(ref IsEnabled, "isEnabled", true);
        Scribe_Values.Look(ref TalkInterval, "talkInterval", 7);
        Scribe_Values.Look(ref ProcessNonRimTalkInteractions, "processNonRimTalkInteractions", true);
        Scribe_Values.Look(ref AllowSimultaneousConversations, "allowSimultaneousConversations", true);
        Scribe_Values.Look(ref CustomInstruction, "customInstruction", "");
        Scribe_Values.Look(ref DisplayTalkWhenDrafted, "displayTalkWhenDrafted", true);
        Scribe_Values.Look(ref AllowMonologue, "allowMonologue", true);
        Scribe_Values.Look(ref AllowSlavesToTalk, "allowSlavesToTalk", true);
        Scribe_Values.Look(ref AllowPrisonersToTalk, "allowPrisonersToTalk", true);
        Scribe_Values.Look(ref AllowOtherFactionsToTalk, "allowOtherFactionsToTalk", false);
        Scribe_Values.Look(ref AllowEnemiesToTalk, "allowEnemiesToTalk", false);
        Scribe_Values.Look(ref AllowCustomConversation, "allowCustomConversation", true);
        Scribe_Values.Look(ref PlayerDialogueMode, "playerDialogueMode", Settings.PlayerDialogueMode.Manual);
        Scribe_Values.Look(ref PlayerName, "playerName", "Player");
        Scribe_Values.Look(ref ContinueDialogueWhileSleeping, "continueDialogueWhileSleeping", false);
        Scribe_Values.Look(ref DisableAiAtSpeed, "DisableAiAtSpeed", 0);
        Scribe_Collections.Look(ref EnabledArchivableTypes, "enabledArchivableTypes", LookMode.Value, LookMode.Value);
        Scribe_Values.Look(ref AllowBabiesToTalk, "allowBabiesToTalk", true);
        Scribe_Values.Look(ref AllowNonHumanToTalk, "allowNonHumanToTalk", true);
        Scribe_Values.Look(ref ApplyMoodAndSocialEffects, "applyMoodAndSocialEffects", false);
        Scribe_Values.Look(ref MemoryImportanceWeight, "memoryImportanceWeight", 3.0f);
        Scribe_Values.Look(ref KeywordWeight, "KeywordWeight", 2.0f);
        Scribe_Deep.Look(ref Context, "context");
        Scribe_Values.Look(ref ButtonDisplay, "buttonDisplay", Settings.ButtonDisplayMode.Tab, true);
        Scribe_Values.Look(ref DebugModeEnabled, "debugModeEnabled", false);
        Scribe_Values.Look(ref DebugGroupingEnabled, "debugGroupingEnabled", false);
        Scribe_Values.Look(ref DebugSortColumn, "debugSortColumn", null);
        Scribe_Values.Look(ref DebugSortAscending, "debugSortAscending", true);
        Scribe_Values.Look(ref OverlayEnabled, "overlayEnabled", false);
        Scribe_Values.Look(ref OverlayOpacity, "overlayOpacity", 0.5f);
        Scribe_Values.Look(ref OverlayFontSize, "overlayFontSize", 15f);
        Scribe_Values.Look(ref OverlayDrawAboveUI, "overlayDrawAboveUI", true);

        // Rects...
        Rect defaultDebugRect = new Rect(200f, 200f, 600f, 450f);
        float overlayDebugX = OverlayRectDebug.x;
        float overlayDebugY = OverlayRectDebug.y;
        float overlayDebugWidth = OverlayRectDebug.width;
        float overlayDebugHeight = OverlayRectDebug.height;
        Scribe_Values.Look(ref overlayDebugX, "overlayRectDebug_x", defaultDebugRect.x);
        Scribe_Values.Look(ref overlayDebugY, "overlayRectDebug_y", defaultDebugRect.y);
        Scribe_Values.Look(ref overlayDebugWidth, "overlayRectDebug_width", defaultDebugRect.width);
        Scribe_Values.Look(ref overlayDebugHeight, "overlayRectDebug_height", defaultDebugRect.height);

        Rect defaultNonDebugRect = new Rect(200f, 200f, 400f, 250f);
        float overlayNonDebugX = OverlayRectNonDebug.x;
        float overlayNonDebugY = OverlayRectNonDebug.y;
        float overlayNonDebugWidth = OverlayRectNonDebug.width;
        float overlayNonDebugHeight = OverlayRectNonDebug.height;
        Scribe_Values.Look(ref overlayNonDebugX, "overlayRectNonDebug_x", defaultNonDebugRect.x);
        Scribe_Values.Look(ref overlayNonDebugY, "overlayRectNonDebug_y", defaultNonDebugRect.y);
        Scribe_Values.Look(ref overlayNonDebugWidth, "overlayRectNonDebug_width", defaultNonDebugRect.width);
        Scribe_Values.Look(ref overlayNonDebugHeight, "overlayRectNonDebug_height", defaultNonDebugRect.height);

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            OverlayRectDebug = new Rect(overlayDebugX, overlayDebugY, overlayDebugWidth, overlayDebugHeight);
            OverlayRectNonDebug = new Rect(overlayNonDebugX, overlayNonDebugY, overlayNonDebugWidth, overlayNonDebugHeight);
        }

        if (CloudConfigs == null) CloudConfigs = new List<ApiConfig>();
        if (LocalConfig == null) LocalConfig = new ApiConfig { Provider = AIProvider.Local };
        if (MemoryConfigs == null) MemoryConfigs = new List<ApiConfig>();
        if (EnabledArchivableTypes == null) EnabledArchivableTypes = new Dictionary<string, bool>();
        if (Context == null) Context = new ContextSettings();
        if (CloudConfigs.Count == 0) CloudConfigs.Add(new ApiConfig());
        // 確保至少有一個記憶 Config
        if (MemoryConfigs.Count == 0) MemoryConfigs.Add(new ApiConfig { Provider = AIProvider.Google });
    }
}