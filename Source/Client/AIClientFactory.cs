using RimTalk.Client.Gemini;
using RimTalk.Client.OpenAI;
using RimTalk.Client.Player2;
using System.Threading.Tasks;

namespace RimTalk.Client;

/// <summary>
/// Factory for creating AI client instances with support for async initialization
/// Handles Player2 local app detection and fallback mechanisms
/// </summary>
public static class AIClientFactory
{
    private static IAIClient _instance;
    private static AIProvider _currentProvider;

    // ★ 新增：記憶 Client 快取
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

    // ★ 新增方法：取得記憶專用 Client
    public static async Task<IAIClient> GetMemoryClientAsync()
    {
        var settings = Settings.Get();
        ApiConfig config;

        // 判斷邏輯：如果啟用且有效，用 MemoryConfig；否則回退到主設定
        if (settings.MemoryConfig != null && settings.MemoryConfig.IsEnabled && settings.MemoryConfig.IsValid())
        {
            config = settings.MemoryConfig;
        }
        else
        {
            // 回退：直接使用對話 Client 的設定 (但建立新實例或重用邏輯)
            // 為了簡單且共用連線池，這裡我們直接呼叫 GetAIClientAsync() 取得主實例
            return await GetAIClientAsync();
        }

        // 如果使用了獨立設定，則維護獨立的快取
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
        switch (config.Provider)
        {
            case AIProvider.Google:
                return new GeminiClient();
            case AIProvider.OpenAI:
                return new OpenAIClient("https://api.openai.com" + OpenAIClient.OpenAIPath, config.SelectedModel, config.ApiKey);
            case AIProvider.DeepSeek:
                return new OpenAIClient("https://api.deepseek.com" + OpenAIClient.OpenAIPath, config.SelectedModel, config.ApiKey);
            case AIProvider.Grok:
                return new OpenAIClient("https://api.x.ai" + OpenAIClient.OpenAIPath, config.SelectedModel, config.ApiKey);
            case AIProvider.OpenRouter:
                return new OpenAIClient("https://openrouter.ai/api" + OpenAIClient.OpenAIPath, config.SelectedModel, config.ApiKey);
            case AIProvider.Player2:
                // Use async factory method that attempts local app detection before fallback to manual API key
                return await Player2Client.CreateAsync(config.ApiKey);
            case AIProvider.Local:
                return new OpenAIClient(config.BaseUrl, config.CustomModelName);
            case AIProvider.Custom:
                return new OpenAIClient(config.BaseUrl, config.CustomModelName, config.ApiKey);
            default:
                return null;
        }
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

        // ★ 新增：清理記憶 Client
        _memoryInstance = null;
        _currentMemoryProvider = AIProvider.None;
    }
}