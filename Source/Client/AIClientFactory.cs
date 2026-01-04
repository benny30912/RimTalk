using System.Threading.Tasks;
using RimTalk.Client.Gemini;
using RimTalk.Client.OpenAI;
using RimTalk.Client.Player2;

namespace RimTalk.Client;

/// <summary>
/// Factory for creating AI client instances with support for async initialization
/// Handles Player2 local app detection and fallback mechanisms
/// </summary>
public static class AIClientFactory
{
    private static IAIClient _instance;
    private static AIProvider _currentProvider;

    // [LOCAL] 你的 Memory Client 擴展
    private static IAIClient _memoryInstance;
    private static AIProvider _currentMemoryProvider;

    /// <summary>
    /// Async method for getting AI client - required for Player2 local detection
    /// </summary>
    public static async Task<IAIClient> GetAIClientAsync()
    {
        var config = Settings.Get().GetActiveConfig();
        if (config == null)
        {
            return null;
        }

        if (_instance == null || _currentProvider != config.Provider)
        {
            _instance = await CreateServiceInstanceAsync(config);
            _currentProvider = config.Provider;
        }

        return _instance;
    }

    // [LOCAL] 你的 Memory Client 擴展
    /// <summary>
    /// Gets or creates the AI client for memory operations.
    /// Uses independent config if enabled, otherwise falls back to main config.
    /// </summary>
    public static async Task<IAIClient> GetMemoryClientAsync()
    {
        var settings = Settings.Get();

        // 如果未啟用獨立記憶模型，使用主 Client
        if (!settings.EnableMemoryModel)
        {
            return await GetAIClientAsync();
        }

        var config = settings.GetActiveMemoryConfig();
        if (config == null)
        {
            // 回退到主 Client
            return await GetAIClientAsync();
        }

        if (_memoryInstance == null || _currentMemoryProvider != config.Provider)
        {
            _memoryInstance = await CreateServiceInstanceAsync(config);
            _currentMemoryProvider = config.Provider;
        }

        return _memoryInstance;
    }

    /// <summary>
    /// Creates appropriate AI client instance based on provider configuration
    /// Player2 uses async factory method for local app detection
    /// </summary>
    private static async Task<IAIClient> CreateServiceInstanceAsync(ApiConfig config)
    {
        var model = config.SelectedModel == "Custom" ? config.CustomModelName : config.SelectedModel;

        // 1. Handle Special/Dynamic cases
        switch (config.Provider)
        {
            case AIProvider.Google: return new GeminiClient();
            case AIProvider.Player2: return await Player2Client.CreateAsync(config.ApiKey);
            case AIProvider.Local: return new OpenAIClient(config.BaseUrl, config.CustomModelName);
            case AIProvider.Custom: return new OpenAIClient(config.BaseUrl, config.CustomModelName, config.ApiKey);
        }

        // 2. Handle Standard Clients via Registry
        if (AIProviderRegistry.Defs.TryGetValue(config.Provider, out var def))
        {
            return new OpenAIClient(def.EndpointUrl, model, config.ApiKey, def.ExtraHeaders);
        }

        return null;
    }

    /// <summary>
    /// Clean up resources and stop background processes
    /// </summary>
    public static void Clear()
    {
        if (_currentProvider == AIProvider.Player2)
        {
            Player2Client.StopHealthCheck();
        }
        _instance = null;
        _currentProvider = AIProvider.None;

        // [LOCAL] 清理 Memory Client
        _memoryInstance = null;
        _currentMemoryProvider = AIProvider.None;
    }
}
