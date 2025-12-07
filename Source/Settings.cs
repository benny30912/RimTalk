using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimTalk.Data; // 確保引用了含有 RimTalkWorldComponent 的命名空間
using RimWorld;
using UnityEngine;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk;

public partial class Settings : Mod
{
    private Vector2 _mainScrollPosition = Vector2.zero;
    private string _textAreaBuffer = "";
    private bool _textAreaInitialized;
    private List<string> _discoveredArchivableTypes = [];
    private bool _archivableTypesScanned;
    private int _apiSettingsHash = 0;

    // Common Knowledge UI state
    private Vector2 _ckListScrollPosition = Vector2.zero;
    private string _ckKeywordsBuffer = "";
    private string _ckContentBuffer = "";
    private CommonKnowledgeData _selectedCkData = null;

    // Tab system
    private enum SettingsTab
    {
        Basic,
        AIInstruction,
        Context,
        EventFilter,
        CommonKnowledge // 新增
    }
    public enum ButtonDisplayMode
    {
        Tab,
        Toggle,
        None
    }
    public enum PlayerDialogueMode
    {
        Disabled,
        Manual,
        AIDriven
    }

    private SettingsTab _currentTab = SettingsTab.Basic;

    private static RimTalkSettings _settings;

    public static RimTalkSettings Get()
    {
        return _settings ??= LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
    }

    public Settings(ModContentPack content) : base(content)
    {
        var harmony = new Harmony("cj.rimtalk");
        var settings = GetSettings<RimTalkSettings>();
        harmony.PatchAll();
        _apiSettingsHash = GetApiSettingsHash(settings);
    }

    public override string SettingsCategory() =>
        Content?.Name ?? GetType().Assembly.GetName().Name;

    private void ScanForArchivableTypes()
    {
        if (_archivableTypesScanned) return;

        var archivableTypes = new HashSet<string>();

        // Scan all assemblies for IArchivable implementations (includes mods)
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var types = assembly.GetTypes()
                    .Where(t => typeof(IArchivable).IsAssignableFrom(t) &&
                                !t.IsInterface &&
                                !t.IsAbstract)
                    .Select(t => t.FullName)
                    .ToList();

                foreach (var type in types)
                    archivableTypes.Add(type);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error scanning assembly {assembly.FullName}: {ex.Message}");
            }
        }

        // Also add types from current archive if game is loaded (to catch any missed runtime types)
        if (Current.Game != null && Find.Archive != null)
        {
            foreach (var archivable in Find.Archive.ArchivablesListForReading)
            {
                archivableTypes.Add(archivable.GetType().FullName);
            }
        }

        _discoveredArchivableTypes = archivableTypes.OrderBy(x => x).ToList();
        _archivableTypesScanned = true;

        // Initialize settings for new types
        RimTalkSettings settings = Get();
        foreach (var typeName in _discoveredArchivableTypes)
        {
            if (!settings.EnabledArchivableTypes.ContainsKey(typeName))
            {
                // Enable by default for most types, but disable Verse.Message specifically
                bool defaultEnabled = !typeName.Equals("Verse.Message", StringComparison.OrdinalIgnoreCase);
                settings.EnabledArchivableTypes[typeName] = defaultEnabled;
            }
        }

        Log.Message($"[RimTalk] Discovered {_discoveredArchivableTypes.Count} archivable types");
    }

    public override void WriteSettings()
    {
        base.WriteSettings();
        ClearCache(); // Invalidate the cache
        RimTalkSettings settings = Get();
        int newHash = GetApiSettingsHash(settings);

        // If hash changes, reset the cloud config index and trigger a full reset of RimTalk.
        if (newHash != _apiSettingsHash)
        {
            settings.CurrentCloudConfigIndex = 0;
            _apiSettingsHash = newHash;
            RimTalk.Reset(true);
        }
    }

    private int GetApiSettingsHash(RimTalkSettings settings)
    {
        // Create a string representation of the API settings and get its hash code
        var sb = new StringBuilder();

        if (settings.CloudConfigs != null)
        {
            foreach (var config in settings.CloudConfigs)
            {
                sb.AppendLine(config.Provider.ToString());
                sb.AppendLine(config.ApiKey);
                sb.AppendLine(config.SelectedModel);
                sb.AppendLine(config.CustomModelName);
                sb.AppendLine(config.IsEnabled.ToString());
                sb.AppendLine(config.BaseUrl);
            }
        }
        if (settings.LocalConfig != null)
        {
            sb.AppendLine(settings.LocalConfig.Provider.ToString());
            sb.AppendLine(settings.LocalConfig.BaseUrl);
            sb.AppendLine(settings.LocalConfig.CustomModelName);
        }

        sb.AppendLine(settings.CustomInstruction);
        sb.AppendLine(settings.AllowSimultaneousConversations.ToString());
        sb.AppendLine(settings.AllowSlavesToTalk.ToString());
        sb.AppendLine(settings.AllowPrisonersToTalk.ToString());
        sb.AppendLine(settings.AllowOtherFactionsToTalk.ToString());
        sb.AppendLine(settings.AllowEnemiesToTalk.ToString());
        sb.AppendLine(settings.AllowBabiesToTalk.ToString());
        sb.AppendLine(settings.AllowNonHumanToTalk.ToString());
        sb.AppendLine(settings.ApplyMoodAndSocialEffects.ToString());
        sb.AppendLine(settings.PlayerDialogueMode.ToString());
        sb.AppendLine(settings.PlayerName);
        sb.AppendLine(settings.MemoryImportanceWeight.ToString()); // 包含新設定

        return sb.ToString().GetHashCode();
    }

    private void DrawTabButtons(Rect rect)
    {
        // 調整按鈕寬度以容納 5 個標籤
        float tabWidth = rect.width / 5f;

        Rect basicTabRect = new Rect(rect.x, rect.y, tabWidth, 30f);
        Rect instructionTabRect = new Rect(rect.x + tabWidth, rect.y, tabWidth, 30f);
        Rect contextTabRect = new Rect(rect.x + tabWidth * 2, rect.y, tabWidth, 30f);
        Rect filterTabRect = new Rect(rect.x + tabWidth * 3, rect.y, tabWidth, 30f);
        Rect ckTabRect = new Rect(rect.x + tabWidth * 4, rect.y, tabWidth, 30f);

        // Basic Settings Tab
        GUI.color = _currentTab == SettingsTab.Basic ? Color.white : Color.gray;
        if (Widgets.ButtonText(basicTabRect, "RimTalk.Settings.BasicSettings".Translate()))
        {
            _currentTab = SettingsTab.Basic;
        }

        // AI Instruction Tab
        GUI.color = _currentTab == SettingsTab.AIInstruction ? Color.white : Color.gray;
        if (Widgets.ButtonText(instructionTabRect, "RimTalk.Settings.AIInstruction".Translate()))
        {
            _currentTab = SettingsTab.AIInstruction;
        }

        // Context Tab
        GUI.color = _currentTab == SettingsTab.Context ? Color.white : Color.gray;
        if (Widgets.ButtonText(contextTabRect, "RimTalk.Settings.ContextFilter".Translate()))
        {
            _currentTab = SettingsTab.Context;
        }

        // Event Filter Tab
        GUI.color = _currentTab == SettingsTab.EventFilter ? Color.white : Color.gray;
        if (Widgets.ButtonText(filterTabRect, "RimTalk.Settings.EventFilter".Translate()))
        {
            _currentTab = SettingsTab.EventFilter;
            if (!_archivableTypesScanned)
            {
                ScanForArchivableTypes();
            }
        }

        // Common Knowledge Tab
        GUI.color = _currentTab == SettingsTab.CommonKnowledge ? Color.white : Color.gray;
        if (Widgets.ButtonText(ckTabRect, "RimTalk.Settings.CommonKnowledge".Translate())) // 常識庫
        {
            _currentTab = SettingsTab.CommonKnowledge;
        }

        GUI.color = Color.white;
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        // Draw tab buttons at the top
        Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, 35f);
        DrawTabButtons(tabRect);

        // Draw content area below tabs
        Rect contentRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);

        // CommonKnowledge 不需要 Listing_Standard 的自動高度計算，因為它自己管理布局
        if (_currentTab == SettingsTab.CommonKnowledge)
        {
            DrawCommonKnowledgeSettings(contentRect);
            return;
        }

        // --- Dynamic height calculation (off-screen) ---
        GUI.BeginGroup(new Rect(-9999, -9999, 1, 1)); // Draw off-screen
        Listing_Standard listing = new Listing_Standard();
        Rect calculationRect = new Rect(0, 0, contentRect.width - 16f, 9999f);
        listing.Begin(calculationRect);

        switch (_currentTab)
        {
            case SettingsTab.Basic:
                DrawBasicSettings(listing);
                break;
            case SettingsTab.AIInstruction:
                DrawAIInstructionSettings(listing);
                break;
            case SettingsTab.Context:
                DrawContextFilterSettings(listing);
                break;
            case SettingsTab.EventFilter:
                DrawEventFilterSettings(listing);
                break;
        }

        float contentHeight = listing.CurHeight;
        listing.End();
        GUI.EndGroup();
        // --- End of height calculation ---

        // Now draw for real with the correct scroll view height
        Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, contentHeight);
        _mainScrollPosition = GUI.BeginScrollView(contentRect, _mainScrollPosition, viewRect);

        listing.Begin(viewRect);

        switch (_currentTab)
        {
            case SettingsTab.Basic:
                DrawBasicSettings(listing);
                break;
            case SettingsTab.AIInstruction:
                DrawAIInstructionSettings(listing);
                break;
            case SettingsTab.Context:
                DrawContextFilterSettings(listing);
                break;
            case SettingsTab.EventFilter:
                DrawEventFilterSettings(listing);
                break;
        }

        listing.End();
        GUI.EndScrollView();
    }

    private void DrawCommonKnowledgeSettings(Rect rect)
    {
        if (Current.Game == null)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, "RimTalk.Settings.CKStoredInSave".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        var worldComp = Find.World.GetComponent<RimTalkWorldComponent>();
        if (worldComp == null) return;
        var ckList = worldComp.CommonKnowledgeStore;

        // 定義左右分割
        float gap = 10f;
        float rightWidth = rect.width * 0.35f;
        float leftWidth = rect.width - rightWidth - gap;

        Rect leftRect = new Rect(rect.x, rect.y, leftWidth, rect.height);
        Rect rightRect = new Rect(rect.x + leftWidth + gap, rect.y, rightWidth, rect.height);

        // --- 左側：列表 (Scroll View) ---
        Widgets.DrawMenuSection(leftRect);

        float itemHeight = 40f;
        float viewHeight = ckList.Count * itemHeight;
        Rect viewRect = new Rect(0, 0, leftRect.width - 16f, viewHeight);

        Widgets.BeginScrollView(leftRect, ref _ckListScrollPosition, viewRect);
        float currentY = 0f;

        for (int i = 0; i < ckList.Count; i++)
        {
            var data = ckList[i];
            Rect rowRect = new Rect(0, currentY, viewRect.width, itemHeight);

            // 背景高亮
            if (i % 2 == 0) Widgets.DrawLightHighlight(rowRect);
            if (data == _selectedCkData) Widgets.DrawHighlightSelected(rowRect);

            // 內容預覽
            string keys = string.Join(", ", data.Keywords);
            string preview = $"[{keys}] {data.Content}";

            Rect textRect = new Rect(rowRect.x + 5f, rowRect.y, rowRect.width - 30f, rowRect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(textRect, preview);
            Text.Anchor = TextAnchor.UpperLeft;

            // 點擊選擇
            if (Widgets.ButtonInvisible(textRect))
            {
                _selectedCkData = data;
                _ckKeywordsBuffer = string.Join(", ", data.Keywords);
                _ckContentBuffer = data.Content;
            }

            // 刪除按鈕
            Rect deleteRect = new Rect(rowRect.xMax - 25f, rowRect.y + 10f, 20f, 20f);
            if (Widgets.ButtonText(deleteRect, "X"))
            {
                ckList.RemoveAt(i);
                if (_selectedCkData == data)
                {
                    _selectedCkData = null;
                    _ckKeywordsBuffer = "";
                    _ckContentBuffer = "";
                }
                break; // 重繪列表
            }

            currentY += itemHeight;
        }
        Widgets.EndScrollView();

        // --- 右側：編輯面板 ---
        Widgets.DrawMenuSection(rightRect);
        Rect innerRight = rightRect.ContractedBy(10f);

        Listing_Standard rightListing = new Listing_Standard();
        rightListing.Begin(innerRight);

        rightListing.Label(_selectedCkData == null ? "RimTalk.Settings.CKNewEntry".Translate() : "RimTalk.Settings.CKEditEntry".Translate());
        rightListing.Gap();

        rightListing.Label("RimTalk.Settings.CKKeywords".Translate());
        _ckKeywordsBuffer = rightListing.TextEntry(_ckKeywordsBuffer);

        rightListing.Gap();

        rightListing.Label("RimTalk.Settings.CKContent".Translate());
        // TextArea 沒有直接在 Listing 中的 helper，用 GetRect 手動畫
        Rect contentAreaRect = rightListing.GetRect(200f);
        _ckContentBuffer = Widgets.TextArea(contentAreaRect, _ckContentBuffer);

        rightListing.Gap(20f);

        string btnLabel = _selectedCkData == null ? "RimTalk.Settings.CKAdd".Translate() : "RimTalk.Settings.CKUpdate".Translate();
        if (rightListing.ButtonText(btnLabel))
        {
            if (!string.IsNullOrWhiteSpace(_ckKeywordsBuffer) && !string.IsNullOrWhiteSpace(_ckContentBuffer))
            {
                List<string> keys = _ckKeywordsBuffer.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                if (_selectedCkData != null)
                {
                    // Update existing
                    _selectedCkData.Keywords = keys;
                    _selectedCkData.Content = _ckContentBuffer;
                    // 清空選擇以便新增下一個
                    _selectedCkData = null;
                    _ckKeywordsBuffer = "";
                    _ckContentBuffer = "";
                }
                else
                {
                    // Add new
                    var newData = new CommonKnowledgeData
                    {
                        Keywords = keys,
                        Content = _ckContentBuffer
                    };
                    ckList.Add(newData);
                    // 清空
                    _ckKeywordsBuffer = "";
                    _ckContentBuffer = "";
                }
            }
        }

        // 如果正在編輯，給個取消按鈕
        if (_selectedCkData != null)
        {
            rightListing.Gap(5f);
            if (rightListing.ButtonText("RimTalk.Settings.CKCancelClear".Translate()))
            {
                _selectedCkData = null;
                _ckKeywordsBuffer = "";
                _ckContentBuffer = "";
            }
        }

        rightListing.End();
    }

    private static void ClearCache()
    {
        _settings = null;
    }
}