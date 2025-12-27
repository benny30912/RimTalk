using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RimTalk.Service;

// WARNING:
// This class defines core logic and has a significant impact on system behavior.
// In most cases, you should NOT modify this file.
public static class AIService
{
    // [Upstream] 移除 _instruction 和 _contextUpdating
    private static bool _busy;
    private static bool _firstInstruction = true;

    /// <summary>
    /// Streaming chat that invokes callback as each player's dialogue is parsed
    /// </summary>
    public static async Task ChatStreaming<T>(TalkRequest request,
        List<(Role role, string message)> messages,
        Dictionary<string, T> players,
        Action<T, TalkResponse> onPlayerResponseReceived)
    {
        var currentMessages = new List<(Role role, string message)>(messages) { (Role.User, request.Prompt) };
        var initApiLog = ApiHistory.AddRequest(request);  // [Upstream] 移除 context 參數
        var lastApiLog = initApiLog;

        _busy = true;
        try
        {
            var payload = await AIErrorHandler.HandleWithRetry(async () =>
            {
                var client = await AIClientFactory.GetAIClientAsync();
                if (client == null) return null;

                return await client.GetStreamingChatCompletionAsync<TalkResponse>(request.Context, currentMessages,
                    talkResponse =>
                    {
                        // [NEW] 檢查是否為 Metadata 專用物件
                        bool isMetadataOnly = string.IsNullOrEmpty(talkResponse.Name) &&
                                              !string.IsNullOrEmpty(talkResponse.Summary);

                        if (isMetadataOnly)
                        {
                            // Metadata 物件不需要匹配 Pawn，直接傳遞給回調
                            var firstPlayer = players.Values.FirstOrDefault();
                            if (firstPlayer != null)
                            {
                                talkResponse.TalkType = request.TalkType;
                                onPlayerResponseReceived?.Invoke(firstPlayer, talkResponse);
                            }
                            return;
                        }

                        // [原有邏輯] 一般對話物件需要匹配 Pawn
                        if (!players.TryGetValue(talkResponse.Name, out var player))
                            return;

                        talkResponse.TalkType = request.TalkType;

                        // Add logs
                        int elapsedMs = (int)(DateTime.Now - lastApiLog.Timestamp).TotalMilliseconds;
                        if (lastApiLog == initApiLog)
                            elapsedMs -= lastApiLog.ElapsedMs;

                        var newApiLog = ApiHistory.AddResponse(initApiLog.Id, talkResponse.Text, talkResponse.Name, talkResponse.InteractionRaw, elapsedMs: elapsedMs);
                        talkResponse.Id = newApiLog.Id;

                        lastApiLog = newApiLog;

                        onPlayerResponseReceived?.Invoke(player, talkResponse);
                    });
            });

            if (payload == null || string.IsNullOrEmpty(initApiLog.Response))
            {
                initApiLog.Response = "Failed";
            }

            ApiHistory.UpdatePayload(initApiLog.Id, payload);

            Stats.IncrementCalls();
            Stats.IncrementTokens(payload?.TokenCount ?? 0);

            _firstInstruction = false;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// [Upstream] Simplified streaming chat for Debug Resend (uses Cache.GetByName)
    /// </summary>
    public static async Task ChatStreaming(TalkRequest request,
        List<(Role role, string message)> messages,
        Action<TalkResponse> onPlayerResponseReceived)
    {
        var currentMessages = new List<(Role role, string message)>(messages) { (Role.User, request.Prompt) };
        var initApiLog = ApiHistory.AddRequest(request);
        var lastApiLog = initApiLog;

        _busy = true;
        try
        {
            var payload = await AIErrorHandler.HandleWithRetry(async () =>
            {
                var client = await AIClientFactory.GetAIClientAsync();
                if (client == null) return null;
                return await client.GetStreamingChatCompletionAsync<TalkResponse>(request.Context, currentMessages,
                    talkResponse =>
                    {
                        if (Cache.GetByName(talkResponse.Name) == null) return;

                        talkResponse.TalkType = request.TalkType;

                        // Add logs
                        int elapsedMs = (int)(DateTime.Now - lastApiLog.Timestamp).TotalMilliseconds;
                        if (lastApiLog == initApiLog)
                            elapsedMs -= lastApiLog.ElapsedMs;

                        var newApiLog = ApiHistory.AddResponse(initApiLog.Id, talkResponse.Text, talkResponse.Name, talkResponse.InteractionRaw, elapsedMs: elapsedMs);
                        talkResponse.Id = newApiLog.Id;

                        lastApiLog = newApiLog;

                        onPlayerResponseReceived?.Invoke(talkResponse);
                    });
            });

            if (payload == null || string.IsNullOrEmpty(initApiLog.Response))
            {
                initApiLog.Response = "Failed";
            }

            ApiHistory.UpdatePayload(initApiLog.Id, payload);

            Stats.IncrementCalls();
            Stats.IncrementTokens(payload?.TokenCount ?? 0);

            _firstInstruction = false;
        }
        finally
        {
            _busy = false;
        }
    }

    // Original non-streaming method
    public static async Task<List<TalkResponse>> Chat(TalkRequest request,
        List<(Role role, string message)> messages)
    {
        var currentMessages = new List<(Role role, string message)>(messages) { (Role.User, request.Prompt) };
        var apiLog = ApiHistory.AddRequest(request);
        var payload = await ExecuteAIRequest(request.Context, currentMessages);

        if (payload == null)
        {
            apiLog.Response = "Failed";
            return null;
        }

        var talkResponses = JsonUtil.DeserializeFromJson<List<TalkResponse>>(payload.Response);

        if (talkResponses != null)
        {
            foreach (var talkResponse in talkResponses)
            {
                apiLog = ApiHistory.AddResponse(apiLog.Id, talkResponse.Text, talkResponse.Name, talkResponse.InteractionRaw, payload);
                talkResponse.Id = apiLog.Id;
            }
        }

        _firstInstruction = false;

        return talkResponses;
    }

    // One time query - used for generating persona, etc
    public static async Task<T> Query<T>(TalkRequest request) where T : class, IJsonData
    {
        List<(Role role, string message)> message = [(Role.User, request.Prompt)];
        var apiLog = ApiHistory.AddRequest(request);
        var payload = await ExecuteAIRequest(request.Context, message);

        if (payload == null)
        {
            apiLog.Response = "Failed";
            return null;
        }

        var jsonData = JsonUtil.DeserializeFromJson<T>(payload.Response);
        ApiHistory.AddResponse(apiLog.Id, jsonData.GetText(), null, null, payload: payload);

        return jsonData;
    }

    private static async Task<Payload> ExecuteAIRequest(string instruction,
        List<(Role role, string message)> messages)
    {
        _busy = true;
        try
        {
            var payload = await AIErrorHandler.HandleWithRetry(async () =>
            {
                var client = await AIClientFactory.GetAIClientAsync();
                if (client == null) return null;
                return await client.GetChatCompletionAsync(instruction, messages);
            });

            if (payload == null)
                return null;

            Stats.IncrementCalls();
            Stats.IncrementTokens(payload.TokenCount);

            return payload;
        }
        finally
        {
            _busy = false;
        }
    }

    public static bool IsFirstInstruction()
    {
        return _firstInstruction;
    }

    public static bool IsBusy()
    {
        return _busy;  // [Upstream] 移除 _contextUpdating
    }

    public static void Clear()
    {
        _busy = false;
        _firstInstruction = true;
        // [Upstream] 移除 _instruction 和 _contextUpdating 的清理
    }
}
