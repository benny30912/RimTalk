using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Source.Data;
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

    // ========== [LOCAL] 本地重載：帶 Dictionary 的 ChatStreaming<T> ==========
    /// <summary>
    /// [LOCAL] Streaming chat with player dictionary (supports Metadata objects)
    /// 給 TalkService.GenerateAndProcessTalkAsync 使用
    /// </summary>
    public static async Task ChatStreaming<T>(TalkRequest request,
        List<(Role role, string message)> messages,
        Dictionary<string, T> players,
        Action<T, TalkResponse> onPlayerResponseReceived)
    {
        var currentMessages = new List<(Role role, string message)>(messages) { (Role.User, request.Prompt) };
        var initApiLog = ApiHistory.AddRequest(request, Channel.Stream);
        var lastApiLog = initApiLog;
        // [UPSTREAM] 使用 ExecuteAIAction 統一處理
        var payload = await ExecuteAIAction(initApiLog, async client =>
        {
            return await client.GetStreamingChatCompletionAsync<TalkResponse>(request.Context, currentMessages,
                talkResponse =>
                {
                    // [LOCAL] 檢查是否為 Metadata 專用物件
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
                    var newApiLog = ApiHistory.AddResponse(initApiLog.Id, talkResponse.Text, talkResponse.Name,
                        talkResponse.InteractionRaw, elapsedMs: elapsedMs);
                    talkResponse.Id = newApiLog.Id;
                    lastApiLog = newApiLog;
                    onPlayerResponseReceived?.Invoke(player, talkResponse);
                });
        });
        // [UPSTREAM] 統一錯誤處理
        if (string.IsNullOrEmpty(initApiLog.Response))
        {
            if (!initApiLog.IsError && string.IsNullOrEmpty(payload?.ErrorMessage))
            {
                var errorMsg = "Json Deserialization Failed";
                initApiLog.Response = payload != null
                    ? $"{errorMsg}\n\nRaw Response:\n{payload.Response}"
                    : "Unknown Error (No payload received)";
                initApiLog.IsError = true;
                if (payload != null) payload.ErrorMessage = errorMsg;
            }
        }
        ApiHistory.UpdatePayload(initApiLog.Id, payload);
        _firstInstruction = false;
    }

    /// <summary>
    /// Streaming chat that invokes callback as each player's dialogue is parsed
    /// </summary>
    public static async Task ChatStreaming(TalkRequest request,
        List<(Role role, string message)> messages,
        Action<TalkResponse> onPlayerResponseReceived)
    {
        var currentMessages = new List<(Role role, string message)>(messages) { (Role.User, request.Prompt) };
        var initApiLog = ApiHistory.AddRequest(request, Channel.Stream);
        var lastApiLog = initApiLog;

        var payload = await ExecuteAIAction(initApiLog, async client =>
        {
            return await client.GetStreamingChatCompletionAsync<TalkResponse>(request.Context, currentMessages,
                talkResponse =>
                {
                    if (Cache.GetByName(talkResponse.Name) == null) return;

                    talkResponse.TalkType = request.TalkType;

                    // Add logs
                    int elapsedMs = (int)(DateTime.Now - lastApiLog.Timestamp).TotalMilliseconds;
                    if (lastApiLog == initApiLog)
                        elapsedMs -= lastApiLog.ElapsedMs;

                    var newApiLog = ApiHistory.AddResponse(initApiLog.Id, talkResponse.Text, talkResponse.Name,
                        talkResponse.InteractionRaw, elapsedMs: elapsedMs);
                    talkResponse.Id = newApiLog.Id;

                    lastApiLog = newApiLog;

                    onPlayerResponseReceived?.Invoke(talkResponse);
                });
        });

        if (string.IsNullOrEmpty(initApiLog.Response))
        {
            if (!initApiLog.IsError && string.IsNullOrEmpty(payload.ErrorMessage))
            {
                var errorMsg = "Json Deserialization Failed";
                initApiLog.Response = $"{errorMsg}\n\nRaw Response:\n{payload.Response}";
                initApiLog.IsError = true;
                payload.ErrorMessage = errorMsg;
            }
        }
            
        ApiHistory.UpdatePayload(initApiLog.Id, payload);
        _firstInstruction = false;
    }

    // One time query - used for generating persona, etc
    public static async Task<T> Query<T>(TalkRequest request) where T : class, IJsonData
    {
        List<(Role role, string message)> message = [(Role.User, request.Prompt)];

        var apiLog = ApiHistory.AddRequest(request, Channel.Query);
        var payload = await ExecuteAIAction(apiLog, async client => 
            await client.GetChatCompletionAsync(request.Context, message));

        if (!string.IsNullOrEmpty(payload.ErrorMessage) || string.IsNullOrEmpty(payload.Response))
        {
            ApiHistory.UpdatePayload(apiLog.Id, payload);
            return null;
        }
        
        T jsonData;
        try
        {
            jsonData = JsonUtil.DeserializeFromJson<T>(payload.Response);
        }
        catch (Exception)
        {
            var errorMsg = "Json Deserialization Failed";
            apiLog.Response = $"{errorMsg}\n\nRaw Response:\n{payload.Response}";
            apiLog.IsError = true;
            payload.ErrorMessage = errorMsg;
            ApiHistory.UpdatePayload(apiLog.Id, payload);
            return null;
        }

        ApiHistory.AddResponse(apiLog.Id, jsonData.GetText(), null, null, payload: payload);

        return jsonData;
    }

    private static async Task<Payload> ExecuteAIAction(ApiLog apiLog, Func<IAIClient, Task<Payload>> action)
    {
        _busy = true;
        try
        {
            Exception capturedException = null;
            var payload = await AIErrorHandler.HandleWithRetry(async () => 
                await action(await AIClientFactory.GetAIClientAsync()), onFailure: ex =>
            {
                capturedException = ex;
                apiLog.Response = ex.Message;
                apiLog.IsError = true;
            });

            if (payload == null && capturedException != null)
                if (capturedException is AIRequestException requestEx && requestEx.Payload != null)
                    payload = requestEx.Payload;
                else
                    payload = new Payload("Unknown", "Unknown", "", null, 0, capturedException.Message);

            Stats.IncrementCalls();
            Stats.IncrementTokens(payload!.TokenCount);

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
