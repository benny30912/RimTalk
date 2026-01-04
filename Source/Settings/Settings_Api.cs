using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimTalk.Client.Gemini;
using RimTalk.Client.OpenAI;
using RimTalk.Client.Player2;
using RimTalk.Data;
using RimTalk.Vector;  // [LOCAL] 你的 Vector 擴展
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimTalk;

public partial class Settings
{
    private static readonly Dictionary<string, List<string>> ModelCache = new();

    // [LOCAL] 你的 Vector 狀態變數
    private bool _pendingVectorModeChange = false;
    private bool _pendingVectorModeValue = false;

    private void DrawSimpleApiSettings(Listing_Standard listingStandard)
    {
        RimTalkSettings settings = Get();

        listingStandard.Label("RimTalk.Settings.GoogleApiKeyLabel".Translate());

        const float buttonWidth = 150f;
        const float spacing = 5f;

        Rect rowRect = listingStandard.GetRect(30f);
        rowRect.width -= buttonWidth + spacing;

        settings.SimpleApiKey = Widgets.TextField(rowRect, settings.SimpleApiKey);

        Rect buttonRect = new Rect(rowRect.xMax + spacing, rowRect.y, buttonWidth, rowRect.height);
        if (Widgets.ButtonText(buttonRect, "RimTalk.Settings.GetFreeApiKeyButton".Translate()))
        {
            Application.OpenURL("https://aistudio.google.com/app/apikey");
        }

        // Add description for free Google providers
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Rect cloudDescRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(cloudDescRect, "RimTalk.Settings.GoogleApiKeyDesc".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        listingStandard.Gap();

        // Show Advanced Settings button
        Rect advancedButtonRect = listingStandard.GetRect(30f);
        if (Widgets.ButtonText(advancedButtonRect, "RimTalk.Settings.SwitchToAdvancedSettings".Translate()))
        {
            settings.UseSimpleConfig = false;
        }
    }

    private void DrawAdvancedApiSettings(Listing_Standard listingStandard)
    {
        RimTalkSettings settings = Get();

        // Show Simple Settings button
        Rect simpleButtonRect = listingStandard.GetRect(30f);
        if (Widgets.ButtonText(simpleButtonRect, "RimTalk.Settings.SwitchToSimpleSettings".Translate()))
        {
            if (string.IsNullOrWhiteSpace(settings.SimpleApiKey))
            {
                var firstValidCloudConfig = settings.CloudConfigs.FirstOrDefault(c => c.IsValid());
                if (firstValidCloudConfig != null)
                {
                    settings.SimpleApiKey = firstValidCloudConfig.ApiKey;
                }
            }
            settings.UseSimpleConfig = true;
        }

        listingStandard.Gap();

        // Cloud providers option with description
        Rect radioRect1 = listingStandard.GetRect(24f);
        if (Widgets.RadioButtonLabeled(radioRect1, "RimTalk.Settings.CloudProviders".Translate(), settings.UseCloudProviders))
        {
            settings.UseCloudProviders = true;
        }

        // Add description for cloud providers
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Rect cloudDescRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(cloudDescRect, "RimTalk.Settings.CloudProvidersDesc".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        listingStandard.Gap(3f);

        // Local provider option with description
        Rect radioRect2 = listingStandard.GetRect(24f);
        if (Widgets.RadioButtonLabeled(radioRect2, "RimTalk.Settings.LocalProvider".Translate(), !settings.UseCloudProviders))
        {
            settings.UseCloudProviders = false;
            settings.LocalConfig.Provider = AIProvider.Local;
        }

        // Add description for local provider
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Rect localDescRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(localDescRect, "RimTalk.Settings.LocalProviderDesc".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        listingStandard.Gap();

        // Draw appropriate section based on selection
        if (settings.UseCloudProviders)
        {
            DrawCloudProvidersSection(listingStandard, settings);
        }
        else
        {
            DrawLocalProviderSection(listingStandard, settings);
        }

        // [LOCAL] 你的本地擴展
        listingStandard.GapLine();
        DrawMemorySettings(listingStandard, settings);

        listingStandard.GapLine();
        DrawVectorServiceSettings(listingStandard, settings);
    }

    // ========== [LOCAL] 你的 Memory Settings 擴展 ==========
    private void DrawMemorySettings(Listing_Standard listingStandard, RimTalkSettings settings)
    {
        Rect headerRect = listingStandard.GetRect(24f);
        Rect labelRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 30f, headerRect.height);
        Rect toggleRect = new Rect(headerRect.xMax - 24f, headerRect.y, 24f, headerRect.height);
        Widgets.Label(labelRect, "RimTalk.Settings.IndependentMemoryModel".Translate());
        Widgets.Checkbox(new Vector2(toggleRect.x, toggleRect.y), ref settings.EnableMemoryModel);

        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Rect descRect = listingStandard.GetRect(Text.LineHeight);
        string desc = settings.EnableMemoryModel
            ? "RimTalk.Settings.MemoryModelDesc_Enabled".Translate()
            : "RimTalk.Settings.MemoryModelDesc_Disabled".Translate();
        Widgets.Label(descRect, desc);
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        if (settings.EnableMemoryModel)
        {
            listingStandard.Gap(6f);

            // Reusing Cloud Config UI Logic for Memory Configs
            // Header
            Rect listHeaderRect = listingStandard.GetRect(24f);
            Rect addButtonRect = new Rect(listHeaderRect.x + listHeaderRect.width - 65f, listHeaderRect.y, 30f, 24f);
            Rect removeButtonRect = new Rect(listHeaderRect.x + listHeaderRect.width - 30f, listHeaderRect.y, 30f, 24f);
            listHeaderRect.width -= 70f;
            Widgets.Label(listHeaderRect, "RimTalk.Settings.MemoryApiConfigurations".Translate());

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect listDescRect = listingStandard.GetRect(Text.LineHeight * 2);
            listDescRect.width -= 70f;
            Widgets.Label(listDescRect, "RimTalk.Settings.MemoryApiConfigurationsDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(addButtonRect, "+"))
            {
                settings.MemoryConfigs.Add(new ApiConfig { Provider = AIProvider.Google });
            }
            GUI.enabled = settings.MemoryConfigs.Count > 1;
            if (Widgets.ButtonText(removeButtonRect, "−"))
            {
                if (settings.MemoryConfigs.Count > 1)
                    settings.MemoryConfigs.RemoveAt(settings.MemoryConfigs.Count - 1);
            }
            GUI.enabled = true;
            listingStandard.Gap(6f);
            // Draw Rows
            for (int i = 0; i < settings.MemoryConfigs.Count; i++)
            {
                if (DrawCloudConfigRow(listingStandard, settings.MemoryConfigs[i], i, settings.MemoryConfigs))
                {
                    settings.MemoryConfigs.RemoveAt(i);
                    i--;
                }
                listingStandard.Gap(3f);
            }
        }
    }

    // ========== [LOCAL] 你的 Vector Service 擴展 ==========
    private void DrawVectorServiceSettings(Listing_Standard listingStandard, RimTalkSettings settings)
    {
        // 標題
        Rect headerRect = listingStandard.GetRect(24f);
        Widgets.Label(headerRect, "RimTalk.Settings.VectorServiceHeader".Translate());

        listingStandard.Gap(6f);

        // 雲端選項
        Rect cloudRect = listingStandard.GetRect(24f);
        if (Widgets.RadioButtonLabeled(cloudRect, "RimTalk.Settings.UseCloudVector".Translate(), settings.UseCloudVectorService))
        {
            if (!settings.UseCloudVectorService)
            {
                _pendingVectorModeChange = true;
                _pendingVectorModeValue = true;
            }
        }

        // 雲端描述
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Rect cloudDescRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(cloudDescRect, "RimTalk.Settings.CloudVectorDesc".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        // API Key（僅雲端模式顯示）
        if (settings.UseCloudVectorService)
        {
            listingStandard.Gap(3f);
            Rect apiKeyRect = listingStandard.GetRect(24f);
            Rect labelRect = new Rect(apiKeyRect.x, apiKeyRect.y, 80f, apiKeyRect.height);
            Rect inputRect = new Rect(apiKeyRect.x + 85f, apiKeyRect.y, 300f, apiKeyRect.height);
            Widgets.Label(labelRect, "API Key:");
            settings.VectorApiKey = Widgets.TextField(inputRect, settings.VectorApiKey);
        }

        listingStandard.Gap(3f);

        // 本地選項
        Rect localRect = listingStandard.GetRect(24f);
        if (Widgets.RadioButtonLabeled(localRect, "RimTalk.Settings.UseLocalVector".Translate(), !settings.UseCloudVectorService))
        {
            if (settings.UseCloudVectorService)
            {
                _pendingVectorModeChange = true;
                _pendingVectorModeValue = false;
            }
        }

        // 本地描述
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Rect localDescRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(localDescRect, "RimTalk.Settings.LocalVectorDesc".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        // 處理模式切換確認
        if (_pendingVectorModeChange)
        {
            _pendingVectorModeChange = false;
            Find.WindowStack.Add(new Dialog_MessageBox(
                "RimTalk.Settings.VectorModeChangeWarning".Translate(),
                "RimTalk.Confirm".Translate(),
                () => {
                    settings.UseCloudVectorService = _pendingVectorModeValue;
                    // 清除快取
                    MemoryVectorDatabase.Instance.Clear();
                    ContextVectorDatabase.Instance.Clear();
                    // [NEW] 觸發佇列服務模式切換
                    VectorQueueService.Instance.OnModeChanged(_pendingVectorModeValue);
                    Messages.Message("RimTalk.Settings.VectorCacheCleared".Translate(), MessageTypeDefOf.NeutralEvent, false);
                },
                "RimTalk.Cancel".Translate(),
                null
            ));
        }
    }

    // ========== 以下為 Upstream 架構 ==========
    private void DrawCloudProvidersSection(Listing_Standard listingStandard, RimTalkSettings settings)
    {
        Rect headerRect = listingStandard.GetRect(24f);

        float addBtnSize = 24f;
        Rect addButtonRect = new Rect(headerRect.x + headerRect.width - addBtnSize, headerRect.y, addBtnSize, addBtnSize);
        headerRect.width -= (addBtnSize + 5f);

        Widgets.Label(headerRect, "RimTalk.Settings.CloudApiConfigurations".Translate());

        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Rect cloudDescRect = listingStandard.GetRect(Text.LineHeight * 2);
        cloudDescRect.width -= 35f;
        Widgets.Label(cloudDescRect, "RimTalk.Settings.CloudApiConfigurationsDesc".Translate());
        GUI.color = Color.white;

        Color prevColor = GUI.color;
        GUI.color = Color.green;
        if (Widgets.ButtonText(addButtonRect, "+"))
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            settings.CloudConfigs.Add(new ApiConfig());
        }
        GUI.color = prevColor;

        listingStandard.Gap(6f);

        // Table Headers
        Rect tableHeaderRect = listingStandard.GetRect(20f);
        float x = tableHeaderRect.x;
        float y = tableHeaderRect.y;
        float height = tableHeaderRect.height;
        float totalWidth = tableHeaderRect.width;

        float providerWidth = 90f;
        float modelWidth = 190f;
        float controlsWidth = 100f;

        Widgets.Label(new Rect(x, y, providerWidth, height), "RimTalk.Settings.ProviderHeader".Translate());
        Widgets.Label(new Rect(x + providerWidth + 5f, y, 200f, height), "RimTalk.Settings.ApiKeyHeader".Translate());
        Widgets.Label(new Rect(totalWidth - controlsWidth - modelWidth - 5f, y, modelWidth, height), "RimTalk.Settings.ModelHeader".Translate());
        Widgets.Label(new Rect(totalWidth - controlsWidth + 5f, y, controlsWidth, height), "RimTalk.Settings.EnabledHeader".Translate());

        listingStandard.Gap(3f);

        for (int i = 0; i < settings.CloudConfigs.Count; i++)
        {
            if (DrawCloudConfigRow(listingStandard, settings.CloudConfigs[i], i, settings.CloudConfigs))
            {
                settings.CloudConfigs.RemoveAt(i);
                i--;
            }
            listingStandard.Gap(2f);
        }

        Text.Font = GameFont.Small;
    }

    private bool DrawCloudConfigRow(Listing_Standard listingStandard, ApiConfig config, int index, List<ApiConfig> configs)
    {
        Text.Font = GameFont.Tiny;

        Rect rowRect = listingStandard.GetRect(22f);
        float x = rowRect.x;
        float y = rowRect.y;
        float height = rowRect.height;
        float totalWidth = rowRect.width;

        float providerWidth = 90f;
        float modelWidth = 190f;
        float controlsWidth = 100f;
        float gap = 5f;

        float middleZoneWidth = totalWidth - providerWidth - modelWidth - controlsWidth - (gap * 3);
        float middleStartX = x + providerWidth + gap;

        Color originalColor = GUI.color;
        if (!config.IsEnabled)
        {
            GUI.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);
        }

        // 1. Provider
        DrawProviderDropdown(x, y, height, providerWidth, config);

        // 2. Middle Zone
        if (config.Provider == AIProvider.Custom)
        {
            float keyWidth = (middleZoneWidth * 0.4f) - (gap / 2);
            float urlWidth = (middleZoneWidth * 0.6f) - (gap / 2);
            DrawApiKeyInput(middleStartX, y, height, keyWidth, config);
            DrawBaseUrlInput(middleStartX + keyWidth + gap, y, height, urlWidth, config);
        }
        else
        {
            DrawApiKeyInput(middleStartX, y, height, middleZoneWidth, config);
        }

        // 3. Model
        float modelStartX = middleStartX + middleZoneWidth + gap;
        if (config.Provider == AIProvider.Custom)
        {
            DrawCustomModelInput(modelStartX, y, height, modelWidth, config);
        }
        else
        {
            DrawDefaultModelSelector(modelStartX, y, height, modelWidth, config);
        }

        GUI.color = originalColor;

        // 4. Controls
        float btnSize = 22f;
        float btnGap = 2f;

        float deleteX = totalWidth - btnSize;
        float downX = deleteX - btnGap - btnSize;
        float upX = downX - btnGap - btnSize;

        float controlsStartX = totalWidth - controlsWidth;
        float checkboxSpaceWidth = upX - controlsStartX;
        float checkboxX = controlsStartX + (checkboxSpaceWidth - 24f) / 2f;

        Rect toggleRect = new Rect(checkboxX, y, 24f, height);
        Widgets.Checkbox(new Vector2(toggleRect.x, toggleRect.y), ref config.IsEnabled, 20f);
        if (Mouse.IsOver(toggleRect)) TooltipHandler.TipRegion(toggleRect, "Enable/Disable");

        DrawReorderButtons(upX, y, height, index, configs);

        Rect deleteRect = new Rect(deleteX, y, btnSize, height);
        bool deleteClicked = false;
        bool canDelete = configs.Count > 1;

        Color prevColor = GUI.color;
        GUI.color = canDelete ? new Color(1f, 0.3f, 0.3f) : Color.gray;
        if (Widgets.ButtonText(deleteRect, "×", active: canDelete))
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            deleteClicked = true;
        }
        GUI.color = prevColor;

        Text.Font = GameFont.Tiny;
        return deleteClicked;
    }

    private void DrawReorderButtons(float x, float y, float height, int index, List<ApiConfig> configs)
    {
        float btnSize = 22f;
        Rect upButtonRect = new Rect(x, y, btnSize, height);

        if (Widgets.ButtonText(upButtonRect, "▲") && index > 0)
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            (configs[index], configs[index - 1]) = (configs[index - 1], configs[index]);
        }

        Rect downButtonRect = new Rect(x + btnSize + 2f, y, btnSize, height);
        if (Widgets.ButtonText(downButtonRect, "▼") && index < configs.Count - 1)
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            (configs[index], configs[index + 1]) = (configs[index + 1], configs[index]);
        }
    }

    // [UPSTREAM] 動態 Provider 列表
    private void DrawProviderDropdown(float x, float y, float height, float width, ApiConfig config)
    {
        Rect providerRect = new Rect(x, y, width, height);
        if (Widgets.ButtonText(providerRect, config.Provider.GetLabel()))
        {
            List<FloatMenuOption> providerOptions = [];
            foreach (AIProvider provider in Enum.GetValues(typeof(AIProvider)))
            {
                if (provider is AIProvider.None or AIProvider.Local) continue;

                providerOptions.Add(new FloatMenuOption(provider.GetLabel(), () =>
                {
                    config.Provider = provider;
                    switch (provider)
                    {
                        case AIProvider.Player2:
                            config.SelectedModel = "Default";
                            Player2Client.CheckPlayer2StatusAndNotify();
                            break;
                        case AIProvider.Custom:
                            config.SelectedModel = "Custom";
                            break;
                        default:
                            config.SelectedModel = Constant.ChooseModel;
                            break;
                    }
                }));
            }
            Find.WindowStack.Add(new FloatMenu(providerOptions));
        }
    }

    private void DrawApiKeyInput(float x, float y, float height, float width, ApiConfig config)
    {
        Rect apiKeyRect = new Rect(x, y, width, height);
        config.ApiKey = DrawTextFieldWithPlaceholder(apiKeyRect, config.ApiKey, "Paste API Key...");
    }

    private void DrawBaseUrlInput(float x, float y, float height, float width, ApiConfig config)
    {
        Rect baseUrlRect = new Rect(x, y, width, height);
        config.BaseUrl = DrawTextFieldWithPlaceholder(baseUrlRect, config.BaseUrl, "https://...");
        if (Mouse.IsOver(baseUrlRect)) TooltipHandler.TipRegion(baseUrlRect, "RimTalk_Settings_Api_BaseUrlInfo".Translate());
    }

    private void DrawCustomModelInput(float x, float y, float height, float width, ApiConfig config)
    {
        Rect customModelRect = new Rect(x, y, width, height);
        config.CustomModelName = DrawTextFieldWithPlaceholder(customModelRect, config.CustomModelName, "Model ID");
        config.SelectedModel = string.IsNullOrWhiteSpace(config.CustomModelName)
            ? Constant.ChooseModel
            : config.CustomModelName;
    }

    private void DrawDefaultModelSelector(float x, float y, float height, float width, ApiConfig config)
    {
        Rect modelRect = new Rect(x, y, width, height);
        if (config.SelectedModel == "Custom")
        {
            float xButtonWidth = 22f;
            float textFieldWidth = width - xButtonWidth - 2f;

            config.CustomModelName = DrawTextFieldWithPlaceholder(new Rect(x, y, textFieldWidth, height), config.CustomModelName, "Model ID");

            if (Widgets.ButtonText(new Rect(x + textFieldWidth + 2f, y, xButtonWidth, height), "×"))
            {
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                config.SelectedModel = Constant.ChooseModel;
            }
        }
        else
        {
            if (Widgets.ButtonText(modelRect, config.SelectedModel))
            {
                ShowModelSelectionMenu(config);
            }
        }
    }

    private string DrawTextFieldWithPlaceholder(Rect rect, string text, string placeholder)
    {
        string result = Widgets.TextField(rect, text);

        if (string.IsNullOrEmpty(result))
        {
            TextAnchor originalAnchor = Text.Anchor;
            Color originalColor = GUI.color;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);

            Widgets.Label(new Rect(rect.x + 5f, rect.y, rect.width - 5f, rect.height), placeholder);

            GUI.color = originalColor;
            Text.Anchor = originalAnchor;
        }

        return result;
    }

    private void ShowModelSelectionMenu(ApiConfig config)
    {
        // Allow Player2 to work without API key (local app detection)
        if (string.IsNullOrWhiteSpace(config.ApiKey) && config.Provider != AIProvider.Player2)
        {
            Find.WindowStack.Add(new FloatMenu([new FloatMenuOption("RimTalk.Settings.EnterApiKey".Translate(), null)]));
            return;
        }

        if (config.Provider == AIProvider.Player2)
        {
            config.SelectedModel = "Default";
            return;
        }

        string url = config.Provider.GetListModelsUrl();
        if (string.IsNullOrEmpty(url)) return;

        void OpenMenu(List<string> models)
        {
            var options = new List<FloatMenuOption>();

            if (models != null && models.Any())
            {
                options.AddRange(models.Select(model => new FloatMenuOption(model, () => config.SelectedModel = model)));
            }
            else
            {
                options.Add(new FloatMenuOption("(no models found - check API Key)", null));
            }

            options.Add(new FloatMenuOption("Custom", () => config.SelectedModel = "Custom"));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        if (ModelCache.ContainsKey(url))
        {
            OpenMenu(ModelCache[url]);
        }
        else
        {
            Task<List<string>> fetchTask = config.Provider == AIProvider.Google
                ? GeminiClient.FetchModelsAsync(config.ApiKey, url)
                : OpenAIClient.FetchModelsAsync(config.ApiKey, url);

            fetchTask.ContinueWith(task =>
            {
                var models = task.Result;
                if (models != null && models.Any())
                {
                    ModelCache[url] = models;
                }
                OpenMenu(models);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    private void DrawLocalProviderSection(Listing_Standard listingStandard, RimTalkSettings settings)
    {
        listingStandard.Label("RimTalk.Settings.LocalProviderConfiguration".Translate());
        listingStandard.Gap(6f);

        if (settings.LocalConfig == null)
        {
            settings.LocalConfig = new ApiConfig { Provider = AIProvider.Local };
        }

        DrawLocalConfigRow(listingStandard, settings.LocalConfig);
    }

    private void DrawLocalConfigRow(Listing_Standard listingStandard, ApiConfig config)
    {
        Rect rowRect = listingStandard.GetRect(24f);
        float x = rowRect.x;
        float y = rowRect.y;
        float height = rowRect.height;

        Rect baseUrlLabelRect = new Rect(x, y, 80f, height);
        var labelText = "RimTalk.Settings.BaseUrlLabel".Translate() + " [?]";
        Widgets.Label(baseUrlLabelRect, labelText);
        TooltipHandler.TipRegion(baseUrlLabelRect, "RimTalk_Settings_Api_BaseUrlInfo".Translate());
        x += 85f;

        Rect urlRect = new Rect(x, y, 250f, height);
        config.BaseUrl = Widgets.TextField(urlRect, config.BaseUrl);
        x += 285f;

        Rect modelLabelRect = new Rect(x, y, 70f, height);
        Widgets.Label(modelLabelRect, "RimTalk.Settings.ModelLabel".Translate());
        x += 75f;

        Rect modelRect = new Rect(x, y, 200f, height);
        config.CustomModelName = Widgets.TextField(modelRect, config.CustomModelName);
    }
}
