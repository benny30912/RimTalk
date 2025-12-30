using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Source.Data;  // [Upstream] 新增
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
    private static bool _busy;
    private static bool _firstInstruction = true;

    /// <summary>
    /// [本地] Streaming chat with player dictionary (supports Metadata objects)
    /// </summary>
    public static async Task ChatStreaming<T>(TalkRequest request,
        List<(Role role, string message)> messages,
        Dictionary<string, T> players,
        Action<T, TalkResponse> onPlayerResponseReceived)
    {
        var currentMessages = new List<(Role role, string message)>(messages) { (Role.User, request.Prompt) };
        var initApiLog = ApiHistory.AddRequest(request, Channel.Stream);  // [Upstream] 新增 Channel
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
                        // [本地] 檢查是否為 Metadata 專用物件
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
            }, onFailure: ex =>  // [Upstream] 新增錯誤回調
            {
                initApiLog.Response = $"API Error: {ex.Message}";
                initApiLog.IsError = true;
            });

            if (payload == null || string.IsNullOrEmpty(initApiLog.Response))
            {
                // [Upstream] 更詳細的錯誤訊息
                if (!initApiLog.IsError)
                {
                    initApiLog.Response = payload != null
                        ? $"Json Deserialization Failed\n\nRaw Response:\n{payload.Response}"
                        : "Unknown Error (No payload received)";
                    initApiLog.IsError = true;
                }
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
        var initApiLog = ApiHistory.AddRequest(request, Channel.Stream);
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

                        int elapsedMs = (int)(DateTime.Now - lastApiLog.Timestamp).TotalMilliseconds;
                        if (lastApiLog == initApiLog)
                            elapsedMs -= lastApiLog.ElapsedMs;

                        var newApiLog = ApiHistory.AddResponse(initApiLog.Id, talkResponse.Text, talkResponse.Name,
                            talkResponse.InteractionRaw, elapsedMs: elapsedMs);
                        talkResponse.Id = newApiLog.Id;

                        lastApiLog = newApiLog;

                        onPlayerResponseReceived?.Invoke(talkResponse);
                    });
            }, onFailure: ex =>
            {
                initApiLog.Response = $"API Error: {ex.Message}";
                initApiLog.IsError = true;
            });

            if (payload == null || string.IsNullOrEmpty(initApiLog.Response))
            {
                if (!initApiLog.IsError)
                {
                    initApiLog.Response = payload != null
                        ? $"Json Deserialization Failed\n\nRaw Response:\n{payload.Response}"
                        : "Unknown Error (No payload received)";
                    initApiLog.IsError = true;
                }
            }

            ApiHistory.UpdatePayload(initApiLog.Id, payload);
            _firstInstruction = false;
        }
        finally
        {
            _busy = false;
        }
    }

    // One time query - used for generating persona, etc
    public static async Task<T> Query<T>(TalkRequest request) where T : class, IJsonData
    {
        List<(Role role, string message)> message = [(Role.User, request.Prompt)];
        var apiLog = ApiHistory.AddRequest(request, Channel.Query);  // [Upstream] Channel.Query
        var payload = await ExecuteAIRequest(request.Context, message);

        if (payload == null)
        {
            apiLog.Response = "Failed";
            apiLog.IsError = true;
            return null;
        }

        T jsonData;
        try
        {
            jsonData = JsonUtil.DeserializeFromJson<T>(payload.Response);
        }
        catch (Exception)
        {
            // [Upstream] 更詳細的錯誤處理
            apiLog.Response = $"Json Deserialization Failed\n\nRaw Response:\n{payload.Response}";
            apiLog.IsError = true;
            ApiHistory.UpdatePayload(apiLog.Id, payload);
            return null;
        }

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
        return _busy;
    }

    public static void Clear()
    {
        _busy = false;
        _firstInstruction = true;
    }
}
