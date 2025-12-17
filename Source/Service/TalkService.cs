using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.UI;
using RimTalk.Util;
using RimWorld;
using Verse;
using Cache = RimTalk.Data.Cache;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Service;

/// <summary>
/// Core service for generating and managing AI-driven conversations between pawns.
/// </summary>
public static class TalkService
{
    /// <summary>
    /// Initiates the process of generating a conversation. It performs initial checks and then
    /// starts a background task to handle the actual AI communication.
    /// </summary>
    public static bool GenerateTalk(TalkRequest talkRequest)
    {
        // Guard clauses to prevent generation when the feature is disabled or the AI service is busy.
        var settings = Settings.Get();
        if (!settings.IsEnabled || !CommonUtil.ShouldAiBeActiveOnSpeed()) return false;
        if (settings.GetActiveConfig() == null) return false;
        if (AIService.IsBusy()) return false;

        PawnState pawn1 = Cache.Get(talkRequest.Initiator);
        if (talkRequest.TalkType != TalkType.User && (pawn1 == null || !pawn1.CanGenerateTalk())) return false;
        
        if (!settings.AllowSimultaneousConversations && AnyPawnHasPendingResponses()) return false;

        // Ensure the recipient is valid and capable of talking.
        PawnState pawn2 = talkRequest.Recipient != null ? Cache.Get(talkRequest.Recipient) : null;
        if (pawn2 == null || talkRequest.Recipient?.Name == null || !pawn2.CanDisplayTalk())
        {
            talkRequest.Recipient = null;
        }

        List<Pawn> nearbyPawns = PawnSelector.GetAllNearByPawns(talkRequest.Initiator);
        if (talkRequest.Recipient.IsPlayer()) nearbyPawns.Insert(0, talkRequest.Recipient);
        var (status, isInDanger) = talkRequest.Initiator.GetPawnStatusFull(nearbyPawns);
        
        // Avoid spamming generations if the pawn's status hasn't changed recently.
        if (talkRequest.TalkType != TalkType.User && status == pawn1.LastStatus && pawn1.RejectCount < 2)
        {
            pawn1.RejectCount++;
            return false;
        }
        
        if (talkRequest.TalkType != TalkType.User && isInDanger) talkRequest.TalkType = TalkType.Urgent;
        
        pawn1.RejectCount = 0;
        pawn1.LastStatus = status;

        // Select the most relevant pawns for the conversation context.
        List<Pawn> pawns = new List<Pawn> { talkRequest.Initiator, talkRequest.Recipient }
            .Where(p => p != null)
            .Concat(nearbyPawns.Where(p =>
            {
                var pawnState = Cache.Get(p);
                return pawnState.CanDisplayTalk() && pawnState.TalkResponses.Empty();
            }))
            .Distinct()
            .Take(settings.Context.MaxPawnContextCount)
            .ToList();
        
        if (pawns.Count == 1) talkRequest.IsMonologue = true;

        if (!settings.AllowMonologue && talkRequest.IsMonologue && talkRequest.TalkType != TalkType.User)
            return false;

        // Build the context and decorate the prompt with current status information.
        // 1. 先裝飾 Prompt (注入時間、天氣、事件標籤)
        // 注意：無需呼叫 GetEnvironmentContext，DecoratePrompt 內部已處理
        PromptService.DecoratePrompt(talkRequest, pawns, status);
        //此時 talkRequest.Prompt 已經包含了 Ongoing Events！

        // 2. 再構建 Context (現在可以讀取 talkRequest.Prompt 中的 Tag 來檢索記憶了)
        // 注意：需確認 PromptService.BuildContext 支援 (TalkRequest, List<Pawn>) 簽名
        string context = PromptService.BuildContext(talkRequest, pawns);
        AIService.UpdateContext(context);

        var allInvolvedPawns = pawns.Union(nearbyPawns).Distinct().ToList();

        // [CHANGE] 執行緒安全強化
        // 預先在主執行緒建立 Name->Pawn 映射，避免在 Task.Run 中存取 Pawn.LabelShort
        var playerDict = new Dictionary<string, Pawn>();
        foreach (var p in allInvolvedPawns)
        {
            if (p?.LabelShort != null)
            {
                playerDict[p.LabelShort] = p;
            }
        }

        // Offload the AI request and processing to a background thread to avoid blocking the game's main thread.
        // [MOD] Pass pre-built playerDict instead of list of pawns
        Task.Run(() => GenerateAndProcessTalkAsync(talkRequest, playerDict, allInvolvedPawns));

        return true;
    }

    /// <summary>
    /// Handles the asynchronous AI streaming and processes the responses.
    /// </summary>
    // [MOD] 接收 Dictionary<string, Pawn> playerDict
    private static async Task GenerateAndProcessTalkAsync(TalkRequest talkRequest, Dictionary<string, Pawn> playerDict, List<Pawn> allInvolvedPawns)
    {
        var initiator = talkRequest.Initiator;
        try
        {
            Cache.Get(initiator).IsGeneratingTalk = true;

            // Create a dictionary for quick pawn lookup by name during streaming.
            // [REMOVED] 移除這行 (它在異步線程存取 Unity API，不安全)
            var receivedResponses = new List<TalkResponse>();

            // ★ 修改點：使用 BuildMemoryBlockFromHistory
            // 這會回傳經過清洗的 List<(Role, string)>
            var memoryBlock = MemoryService.BuildMemoryBlockFromHistory(initiator);

            // 將本次的請求 (Prompt) 加入到列表末尾 (這次是不清洗的，因為包含當前指令)
            // 注意：ChatStreaming 內部會再加一次 request.Prompt，所以這裡只要準備好 History 即可
            // 如果 AIService.ChatStreaming 是設計為 "History + Prompt"，那麼我們傳入 memoryBlock 作為 History 即可
            // Call the streaming chat service. The callback is executed as each piece of dialogue is parsed.
            await AIService.ChatStreaming(
                talkRequest,
                memoryBlock, //傳入清洗過的歷史
                playerDict,
                (pawn, talkResponse) =>
                {
                    Logger.Debug($"Streamed: {talkResponse}");

                    PawnState pawnState = Cache.Get(pawn);
                    // [Refined Logic] 
                    // 若 talkResponse.Name 為空，從 playerDict 反查名字 (Thread-Safe)
                    // 這比直接存取 pawn.LabelShort (Unity API) 安全
                    if (string.IsNullOrEmpty(talkResponse.Name) && pawn != null)
                    {
                        var match = playerDict.FirstOrDefault(x => x.Value == pawn);
                        if (!string.IsNullOrEmpty(match.Key))
                        {
                            talkResponse.Name = match.Key;
                        }
                    }

                    // Link replies to the previous message in the conversation.
                    if (receivedResponses.Any())
                    {
                        talkResponse.ParentTalkId = receivedResponses.Last().Id;
                    }

                    receivedResponses.Add(talkResponse);

                    // [CHANGE] 關鍵：寫入佇列時加鎖
                    // Enqueue the received talk for the pawn to display later.
                    lock (pawnState.TalkResponses)
                    {
                        pawnState.TalkResponses.Add(talkResponse);
                    }
                }
            );

            // Once the stream is complete, save the full conversation to history.
            AddResponsesToHistory(allInvolvedPawns, receivedResponses, talkRequest.Prompt);
        }
        catch (Exception ex)
        {
            Logger.Error(ex.StackTrace);
        }
        finally
        {
            Cache.Get(initiator).IsGeneratingTalk = false;
        }
    }

    /// <summary>
    /// Serializes the generated responses and adds them to the message history for all involved pawns.
    /// </summary>
    private static void AddResponsesToHistory(List<Pawn> pawns, List<TalkResponse> responses, string prompt)
    {
        if (!responses.Any()) return;
        string serializedResponses = JsonUtil.SerializeToJson(responses);

        // 嘗試從回應列表中提取 Metadata
        // 通常位於最後一個回應，或合併所有回應的 Metadata
        var lastResponse = responses.LastOrDefault();
        string summary = lastResponse?.Summary;
        List<string> keywords = lastResponse?.Keywords ?? [];
        int importance = lastResponse?.Importance ?? 1;
        // 若 LLM 未回傳 Summary (例如舊的 Prompt)，則提供一個預設值或讓 MemoryService 後續補救
        if (string.IsNullOrWhiteSpace(summary))
        {
            // 這裡可以選擇不生成 STM，或是生成一個僅包含 Raw Text 提示的 STM
            summary = "(No summary provided by AI)";
        }
        // 建立 STM MemoryRecord
        var memoryRecord = new MemoryRecord
        {
            Summary = summary,
            Keywords = keywords,
            Importance = UnityEngine.Mathf.Clamp(importance, 1, 5),
            CreatedTick = GenTicks.TicksGame,
            AccessCount = 0
        };

        foreach (var pawn in pawns)
        {
            TalkHistory.AddMessageHistory(pawn, prompt, serializedResponses);
            MemoryService.OnShortMemoriesGenerated(pawn, memoryRecord);
        }
    }

    /// <summary>
    /// Iterates through all pawns on each game tick to display any queued talks.
    /// </summary>
    public static void DisplayTalk()
    {
        foreach (Pawn pawn in Cache.Keys)
        {
            PawnState pawnState = Cache.Get(pawn);

            TalkResponse talk = null;

            // [CHANGE] 讀取時加鎖
            lock (pawnState.TalkResponses)
            {
                if (pawnState.TalkResponses.Any())
                {
                    talk = pawnState.TalkResponses.First();
                    // [Corrected] 保留原有的防呆邏輯
                    if (talk == null)
                    {
                        pawnState.TalkResponses.RemoveAt(0);
                    }
                }
            }
            if (talk == null) continue;

            // Skip this talk if its parent was ignored or the pawn is currently unable to speak.
            if (TalkHistory.IsTalkIgnored(talk.ParentTalkId) || !pawnState.CanDisplayTalk())
            {
                // [CHANGE] 移除時加鎖
                lock (pawnState.TalkResponses)
                {
                    pawnState.IgnoreTalkResponse();
                }
                continue;
            }

            if (!talk.IsReply() && !CommonUtil.HasPassed(pawnState.LastTalkTick, Settings.Get().TalkInterval))
            {
                continue;
            }

            int replyInterval = RimTalkSettings.ReplyInterval;
            if (pawn.IsInDanger())
            {
                replyInterval = 2;
                // [CHANGE] 清除時加鎖
                lock (pawnState.TalkResponses)
                {
                    pawnState.IgnoreAllTalkResponses([TalkType.Urgent, TalkType.User]);
                }
            }
            // Enforce a delay for replies to make conversations feel more natural.
            int parentTalkTick = TalkHistory.GetSpokenTick(talk.ParentTalkId);
            if (parentTalkTick == -1 || !CommonUtil.HasPassed(parentTalkTick, replyInterval)) continue;

            CreateInteraction(pawn, talk);
            
            break; // Display only one talk per tick to prevent overwhelming the screen.
        }
    }

    /// <summary>
    /// Retrieves the text for a pawn's current talk. Called by the game's UI system.
    /// </summary>
    public static string GetTalk(Pawn pawn)
    {
        PawnState pawnState = Cache.Get(pawn);
        if (pawnState == null) return null;

        TalkResponse talkResponse = ConsumeTalk(pawnState);
        pawnState.LastTalkTick = GenTicks.TicksGame;

        return talkResponse.Text;
    }

    /// <summary>
    /// Dequeues a talk and updates its history as either spoken or ignored.
    /// </summary>
    private static TalkResponse ConsumeTalk(PawnState pawnState)
    {
        // [CHANGE] 移除時加鎖
        lock (pawnState.TalkResponses)
        {
            // Failsafe check
            if (pawnState.TalkResponses.Empty())
                return new TalkResponse(TalkType.Other, null!, ""); // 依據原方法簽名調整

            var talkResponse = pawnState.TalkResponses.First();
            pawnState.TalkResponses.Remove(talkResponse);
            TalkHistory.AddSpoken(talkResponse.Id);
            var apiLog = ApiHistory.GetApiLog(talkResponse.Id);
            if (apiLog != null)
                apiLog.SpokenTick = GenTicks.TicksGame;

            Overlay.NotifyLogUpdated();
            return talkResponse;
        }
    }

    private static void CreateInteraction(Pawn pawn, TalkResponse talk)
    {
        // Create the interaction log entry, which triggers the display of the talk bubble in-game.
        InteractionDef intDef = DefDatabase<InteractionDef>.GetNamed("RimTalkInteraction");
        var recipient = talk.GetTarget() ?? pawn;
        var playLogEntryInteraction = new PlayLogEntry_RimTalkInteraction(intDef, pawn, recipient, null);

        Find.PlayLog.Add(playLogEntryInteraction);

        if (Settings.Get().ApplyMoodAndSocialEffects && pawn != recipient)
        {
            var interactionType = talk.GetInteractionType();
            var memory = interactionType.GetThoughtDef();
            if (memory != null)
            {
                recipient.needs?.mood?.thoughts?.memories?.TryGainMemory(memory, pawn);
                if (interactionType is InteractionType.Chat)
                {
                    pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(memory, recipient);
                }
            }
        }
    }

    private static bool AnyPawnHasPendingResponses()
    {
        return Cache.GetAll().Any(pawnState =>
        {
            // [CHANGE] 檢查時加鎖
            lock (pawnState.TalkResponses)
            {
                return pawnState.TalkResponses.Count > 0;
            }
        });
    }
}
