using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Util;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Service
{
    public static class MemoryService
    {
        // --- 任務管理狀態 (Moved from TalkHistory) ---
        private static CancellationTokenSource _cts = new();
        private static readonly ConcurrentQueue<Action> _mainThreadActionQueue = new();
        
        // 閾值定義
        public const int MaxShortMemories = 30;
        public const int MaxMediumMemories = 60;
        public const int MaxLongMemories = 40;

        private static RimTalkWorldComponent WorldComp => Find.World?.GetComponent<RimTalkWorldComponent>();

        // --- 生命週期與更新 ---

        /// <summary>
        /// [NEW] 主執行緒更新，處理異步任務的回調。
        /// 應由 GameComponent 呼叫。
        /// </summary>
        public static void Update()
        {
            while (_mainThreadActionQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { Logger.Error($"MemoryService main thread action error: {ex}"); }
            }
        }

        /// <summary>
        /// [NEW] 清理記憶系統狀態 (包含取消正在進行的總結任務)。
        /// </summary>
        public static void Clear(bool keepSavedData = false)
        {
            // 1. 停止異步任務
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            while (_mainThreadActionQueue.TryDequeue(out _)) { }

            // 2. 清除資料 (若不保留)
            if (!keepSavedData)
            {
                var comp = WorldComp;
                if (comp != null)
                {
                    lock (comp.PawnMemories)
                    {
                        comp.PawnMemories.Clear();
                    }
                    // CommonKnowledgeStore 視需求也可以在這裡清空，目前保留
                }
            }
        }

        // --- 流程編排 (Orchestration - Moved from TalkHistory) ---

        /// <summary>
        /// [NEW] 處理新生成的 STM。這是外部對話系統將記憶交給 MemoryService 的入口。
        /// </summary>
        public static void OnShortMemoriesGenerated(Pawn pawn, MemoryRecord memory)
        {
            // 1. 加入 STM 並檢查是否達到總結閾值
            bool thresholdReached = AddMemoryInternal(pawn, memory);

            if (thresholdReached)
            {
                // 2. 觸發異步總結任務
                TriggerStmToMtmSummary(pawn);
            }
        }

        private static void TriggerStmToMtmSummary(Pawn pawn)
        {
            var comp = WorldComp;
            if (comp == null) return;

            PawnMemoryData data;
            // 1. 安全地獲取 data
            lock (comp.PawnMemories)
            {
                if (!comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out data)) return;
            }

            // [THREAD-SAFETY] 預先擷取資料，避免在異步線程存取 Pawn
            string pawnName = pawn.LabelShort;
            int pawnId = pawn.thingIDNumber;

            // 2. 鎖定 data 進行快照
            lock (data)
            {
                var stmSnapshot = data.ShortTermMemories.ToList();
                int countToRestore = data.NewShortMemoriesSinceSummary;
                data.NewShortMemoriesSinceSummary = 0;
                int currentTick = GenTicks.TicksGame;

                RunRetryableTask(
                    taskName: $"STM->MTM for {pawnName}", // Use captured name
                    action: () => SummarizeToMediumAsync(stmSnapshot, pawn, currentTick),
                    onSuccess: (newMemories) =>
                    {
                        if (!newMemories.NullOrEmpty())
                        {
                            OnMediumMemoriesGenerated(pawn, newMemories);
                            // [FIX] 成功總結後，進行 STM 維護
                            // 刪除最舊的記憶直到數量回到 MaxShortMemories
                            // 這通常會移除剛才被 Snapshot 的那些記憶
                            var c = WorldComp;
                            if (c != null && c.PawnMemories.TryGetValue(pawnId, out var d))
                            {
                                lock (d)
                                {
                                    while (d.ShortTermMemories.Count > MaxShortMemories)
                                    {
                                        d.ShortTermMemories.RemoveAt(0);
                                    }
                                }
                            }
                        }
                    },
                    onFailureOrCancel: (isCancelled) =>
                    {
                        if (!isCancelled)
                        {
                            // 失敗回滾：加回計數器
                             var c = WorldComp; // Re-fetch to be safe inside lambda
                             if (c != null && c.PawnMemories.TryGetValue(pawnId, out var d))
                             {
                                 d.NewShortMemoriesSinceSummary += countToRestore;
                             }
                        }
                    },
                    token: _cts.Token
                );
            }
        }

        private static void OnMediumMemoriesGenerated(Pawn pawn, List<MemoryRecord> newMemories)
        {
            var comp = WorldComp;
            if (comp == null || !comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data)) return;
            
            lock (data)
            {
                data.MediumTermMemories ??= [];
                data.MediumTermMemories.AddRange(newMemories);
                data.NewMediumMemoriesSinceArchival += newMemories.Count;

                Messages.Message("RimTalk.MemoryService.MediumMemoryCreated".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NeutralEvent, false);

                // 鏈式觸發：檢查 MTM -> LTM
                if (data.NewMediumMemoriesSinceArchival >= MaxMediumMemories)
                {
                    TriggerMtmToLtmConsolidation(pawn, data);
                }

                // [REMOVED] 移除原先的 "MaxMediumMemories" 強制維護
                // 改為在 MTM -> LTM 成功後才進行淘汰
            }
        }

        private static void TriggerMtmToLtmConsolidation(Pawn pawn, PawnMemoryData data)
        {
            // [THREAD-SAFETY] 預先擷取資料
            string pawnName = pawn.LabelShort;
            int pawnId = pawn.thingIDNumber;

            // [FIX] 補上 missing lock! 確保讀取 List 與修改計數器的原子性
            lock (data)
            {
                var mtmSnapshot = data.MediumTermMemories.ToList();
                int countToRestore = data.NewMediumMemoriesSinceArchival;
                data.NewMediumMemoriesSinceArchival = 0;
                int currentTick = GenTicks.TicksGame;

                RunRetryableTask(
                    taskName: $"MTM->LTM for {pawnName}",
                    action: () => ConsolidateToLongAsync(mtmSnapshot, pawn, currentTick),
                    onSuccess: (longMemories) =>
                    {
                        if (!longMemories.NullOrEmpty())
                        {
                            OnLongMemoriesGenerated(pawn, longMemories, currentTick);
                            // [FIX] 成功歸檔後，進行 MTM 維護
                            // 刪除已歸檔的 MTM，保留上限內的最新記憶
                            var c = WorldComp;
                            if (c != null && c.PawnMemories.TryGetValue(pawnId, out var d))
                            {
                                lock (d)
                                {
                                    while (d.MediumTermMemories.Count > MaxMediumMemories)
                                    {
                                        d.MediumTermMemories.RemoveAt(0);
                                    }
                                }
                            }
                        }
                    },
                    onFailureOrCancel: (isCancelled) =>
                    {
                        if (!isCancelled)
                        {
                            var c = WorldComp;
                            if (c != null && c.PawnMemories.TryGetValue(pawnId, out var d))
                            {
                                d.NewMediumMemoriesSinceArchival += countToRestore;
                            }
                        }
                    },
                    token: _cts.Token
                );
            }
        }

        private static void OnLongMemoriesGenerated(Pawn pawn, List<MemoryRecord> newMemories, int currentTick)
        {
            var comp = WorldComp;
            if (comp == null || !comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data)) return;
            
            lock (data)
            {
                data.LongTermMemories ??= [];
                data.LongTermMemories.AddRange(newMemories);
                
                // 執行權重剔除
                PruneLongTermMemories(data.LongTermMemories, MaxLongMemories, currentTick);

                Messages.Message("RimTalk.MemoryService.LongMemoryCreated".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NeutralEvent, false);
            }
        }
        // --- 核心業務邏輯 (Core Logic) ---

        /// <summary>
        /// [Internal] 實際執行加入 STM 的動作。
        /// </summary>
        private static bool AddMemoryInternal(Pawn pawn, MemoryRecord record)
        {
            var comp = WorldComp;
            if (comp == null || pawn == null) return false;

            PawnMemoryData data;
            lock (comp.PawnMemories)
            {
                if (!comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out data))
                {
                    data = new PawnMemoryData { Pawn = pawn };
                    comp.PawnMemories[pawn.thingIDNumber] = data;
                }
            }

            lock (data)
            {
                data.ShortTermMemories.Add(record);
                data.NewShortMemoriesSinceSummary++;
                return data.NewShortMemoriesSinceSummary >= MaxShortMemories;
            }
        }

        // [Moved & Refactored] 通用的異步重試執行器
        private static void RunRetryableTask<T>(
            string taskName,
            Func<Task<T>> action,
            Action<T> onSuccess,
            Action<bool> onFailureOrCancel,
            CancellationToken token)
        {
            Task.Run(async () =>
            {
                int maxRetries = 5;
                int attempt = 0;

                while (attempt < maxRetries && !token.IsCancellationRequested)
                {
                    if (Current.Game == null) return;

                    try
                    {
                        var result = await action();
                        bool isValid = result != null;
                        if (result is System.Collections.ICollection collection && collection.Count == 0)
                            isValid = false;

                        if (isValid)
                        {
                            if (Current.Game != null)
                                _mainThreadActionQueue.Enqueue(() => onSuccess(result));
                            return;
                        }
                        else
                        {
                           Logger.Warning($"Task {taskName} failed (attempt {attempt + 1}/{maxRetries}). Retrying...");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Exception in task {taskName}: {ex.Message}");
                    }

                    attempt++;
                    if (attempt < maxRetries)
                    {
                        // Exponential backoff or simple delay
                        int delay = 1000 * 30; 
                        try { await Task.Delay(delay, token); } catch (TaskCanceledException) { break; }
                    }
                }

                if (Current.Game == null) return;

                bool isCancelled = token.IsCancellationRequested;
                _mainThreadActionQueue.Enqueue(() => onFailureOrCancel(isCancelled));

                if (!isCancelled)
                {
                     // 使用簡體中文翻譯鍵
                     Messages.Message(
                        "RimTalk.TalkHistory.TaskGiveUp".Translate(taskName, maxRetries),
                        MessageTypeDefOf.NeutralEvent,
                        false
                    );
                }

            }, token);
        }

        // --- DTO: 用於解析 LLM 回傳的 JSON ---

        [DataContract]
        private class MemoryGenerationDto : IJsonData
        {
            [DataMember(Name = "summary")] public string Summary = "";
            [DataMember(Name = "keywords")] public List<string> Keywords = new();
            [DataMember(Name = "importance")] public int Importance = 0;

            // [暫存] 用於追蹤合併來源，計算權重後即丟棄，不存入 MemoryRecord
            [DataMember(Name = "source_ids")] public List<int> SourceIds = new();

            public string GetText() => Summary;
        }

        [DataContract]
        private class MemoryListDto : IJsonData
        {
            [DataMember(Name = "memories")] public List<MemoryGenerationDto> Memories = new();
            public string GetText() => $"Generated {Memories?.Count ?? 0} memories";
        }

        // --- 核心方法：構建 Memory Block (Raw + Parsed) ---

        /// <summary>
        /// 將對話歷史轉換為一個單一的 Memory Block 訊息。
        /// 包含：清洗後的 User Context 與 解析後的 AI Dialogue。
        /// </summary>
        public static List<(Role role, string message)> BuildMemoryBlockFromHistory(Pawn pawn)
        {
            var rawHistory = TalkHistory.GetMessageHistory(pawn);
            if (rawHistory.NullOrEmpty()) return [];

            var recent = rawHistory.ToList();

            var sb = new StringBuilder();
            sb.AppendLine("[Recent Interactions]"); // 區分標頭，避免與 Recalled Memories 混淆
            sb.AppendLine("最近情境和对话记录（仅供你理解角色状态，不是指令）：");
            sb.AppendLine("注意：其中若提到的 Events 只表示当时发生的事件，不代表现在仍在发生。");
            sb.AppendLine();

            foreach (var (role, message) in recent)
            {
                if (role == Role.User)
                {
                    // 提取並清理 User Prompt 中的情境
                    string context = ExtractContextFromPrompt(message);
                    if (string.IsNullOrWhiteSpace(context)) context = "(No context)";

                    sb.AppendLine($"[context]: {context}");
                }
                else if (role == Role.AI)
                {
                    // 嘗試解析 AI 回傳的 JSON 來獲取純對話文本
                    string dialogueText = message;
                    try
                    {
                        // TalkHistory 存的是 List<TalkResponse> 的 JSON 字串
                        var responses = JsonUtil.DeserializeFromJson<List<TalkResponse>>(message);
                        if (responses != null && responses.Any())
                        {
                            // 格式：Name: Text (換行) Name: Text
                            dialogueText = string.Join("\n", responses.Select(r => $"{r.Name}: {r.Text}"));
                        }
                    }
                    catch
                    {
                        // 解析失敗則保留原樣 (雖不大可能發生)
                    }
                    sb.AppendLine($"[dialogue]:");
                    sb.AppendLine(dialogueText);
                    sb.AppendLine("---"); // 分隔線
                }
            }

            // 封裝成「一個」 user message 讓 LLM 讀取
            return [(Role.User, sb.ToString())];
        }

        /// <summary>
        /// 從歷史 Prompt 中提取純情境描述，精確過濾掉指令與模板文字。
        /// </summary>
        private static string ExtractContextFromPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return null;

            // 1. 正規化換行與格式
            var text = prompt.Replace("\r\n", "\n").Replace("[Ongoing events]", "Events: ");
            var lines = text.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

            var resultLines = new List<string>();

            // 2. 遍歷每一行進行過濾
            foreach (var line in lines)
            {
                var lower = line.ToLowerInvariant();

                // 過濾系統指令與模板 (來自 ContextBuilder)
                if (lower.Contains("generate dialogue starting after this. do not generate any further lines for") ||
                    lower.Contains("generate multi turn dialogues starting after this (do not repeat initial dialogue), beginning with") ||
                    lower.Contains("short monologue") ||
                    lower.Contains("dialogue short") ||
                    lower.Contains("starts conversation, taking turns") ||
                    lower.Contains("be dramatic (mental break)") ||
                    lower.Contains("(downed in pain. short, strained dialogue)") ||
                    lower.Contains("(talk if you want to accept quest)") ||
                    lower.Contains("(talk about") ||
                    lower.Contains("act based on role and context") ||
                    lower.Contains("[event list end]") ||
                    lower == $"in {Constant.Lang}".ToLowerInvariant())
                {
                    continue;
                }

                // 保留有意義的內容 (例如 Location, Events, Wealth, Status)
                resultLines.Add(line);
            }

            // 3. 重新組裝
            var result = string.Join("\n", resultLines).Trim();
            return string.IsNullOrEmpty(result) ? null : result;
        }

        // --- 核心查詢方法 ---

        /// <summary>
        /// 統一的記憶查詢入口，處理 Client 獲取與錯誤重試。
        /// </summary>
        private static async Task<T> QueryMemory<T>(TalkRequest request) where T : class, IJsonData
        {
            try
            {
                var payload = await AIErrorHandler.HandleWithRetry(async () =>
                {
                    var client = await AIClientFactory.GetMemoryClientAsync();
                    if (client == null) return null;

                    // 記憶生成不使用 Persona Instruction，直接發送 Prompt
                    return await client.GetChatCompletionAsync("", [(Role.User, request.Prompt)]);
                }, isMemory: true);

                if (payload == null)
                {
                    Logger.Warning($"Memory generation failed used by: {request.Initiator?.LabelShort}");
                    return null;
                }

                return JsonUtil.DeserializeFromJson<T>(payload.Response);
            }
            catch (Exception ex)
            {
                Logger.Error($"Memory Query Error: {ex.Message}");
                return null;
            }
        }

        // --- STM -> MTM: 將短期摘要合併為中期記憶 ---
        /// <summary>
        /// 將一系列 ShortTermMemories (已是摘要) 進一步歸納為 MediumTermMemories。
        /// </summary>
        /// <param name="stmList">短期記憶列表</param>
        /// <param name="pawn">記憶主體</param>
        /// <param name="currentTick">若無法計算時間時的備案時間</param>
        public static async Task<List<MemoryRecord>> SummarizeToMediumAsync(List<MemoryRecord> stmList, Pawn pawn, int currentTick)
        {
            if (stmList.NullOrEmpty()) return [];

            // 1. 格式化 STM 為帶編號的文本 (ID 僅供本次 Prompt 使用)
            string stmContext = FormatMemoriesWithIds(stmList);

            // 2. 建構 Prompt
            // 這裡的挑戰是：輸入已經是 Summary 了，AI 需要做的是 "Meta-Summarization"
            string prompt =
                $$"""
                    分析以下 {{pawn.LabelShort}} 近期的对话摘要片段（Short Term Memories）。
      
                    片段列表：
                    {{stmContext}}
      
                    任务：将这些零散片段归纳为**6-8 条**更完整的"事件记忆"（Medium Term Memories）。
      
                    1. 'summary' (简体中文)：
                        - 合并相关片段（如"A問候B"+"A與B談論天氣"→"A与B寒暄"）
                        - 保留独有细节（绰号、玩笑、承诺、激烈语气）
                        - 若事件判定为刻骨铭心 (Importance=5)，请保留「闪光灯式」的细节：包括具体的人名（谁造成的）、对话原句（关键的一句话）或具体的受伤部位，不需过度概括。
                        - **禁止**相对时间（"昨天"等），允许"近期"或"5501年"
      
                    2. 'keywords'：
                        **只能**从该条综述涵盖的原始片段 keywords 中选择 3-5 个：
                        - Anchor (必选): 1-2 实体名词（人名/物品/地名）
                        - Link (必选): 1-2 概念/动作
                        - Optional: 1 情感/状态
                        **禁止**: 发明新词
      
                    3. 'importance' (1-5)：
                        1=琐碎(闲聊/天气) | 2=普通(工作/轻微不适) | 3=值得记住(友谊/争吵/轻伤)
                        4=重大(崩溃/战斗/恋爱/重伤) | 5=刻骨铭心(死亡/结婚/残疾)
                        日常对话≤2，仅危及生命或改变关系的事件≥4
      
                    4. 'source_ids'(必填)：
                        列出合并来源的原始 ID（如 [1, 2, 3]）
      
                    输出包含 'memories' 数组的 JSON 对象：
                    {
                      "memories": [
                        { 
                          "summary": "...", 
                          "keywords": ["..."], 
                          "importance": 3,
                          "source_ids": [1, 2, 5] // 必须列出合并到此条记忆的所有原始片段编号
                        },
                        ...
                      ]
                    }
                """;

            try
            {
                var request = new TalkRequest(prompt, pawn, currentTick);
                var result = await QueryMemory<MemoryListDto>(request);

                if (result?.Memories == null) return [];

                // 3. 將 DTO 轉換為 MemoryRecord
                return result.Memories
                    .Where(m => m != null)
                    .Select(m =>
                    {
                        // 計算加權時間：根據 source_ids 找回原始 STM 的時間做平均
                        var (calculatedTick, avgAccess) = CalculateMergedMetadata(m.SourceIds, stmList, currentTick);

                        return new MemoryRecord
                        {
                            Summary = m.Summary,
                            Keywords = m.Keywords ?? [],
                            Importance = Mathf.Clamp(m.Importance, 1, 5),
                            CreatedTick = calculatedTick,
                            AccessCount = avgAccess // 新增：繼承活躍度
                        };
                    }).ToList();
            }
            catch (Exception ex)
            {
                Messages.Message("RimTalk.MemoryService.SummarizeFailed".Translate(ex.Message), MessageTypeDefOf.NegativeEvent, false);
                return [];
            }
        }

        // --- MTM -> LTM: 將中期記憶轉化為長期傳記 ---
        /// <summary>
        /// 將累積的中期記憶合併為高層次的長期記憶 (傳記式)。
        /// </summary>
        public static async Task<List<MemoryRecord>> ConsolidateToLongAsync(List<MemoryRecord> mtmList, Pawn pawn, int currentTick)
        {
            if (mtmList.NullOrEmpty()) return [];

            // 1. 格式化
            string mtmContext = FormatMemoriesWithIds(mtmList);

            // 2. 建構 Prompt
            string prompt =
                $$"""
                  将以下 {{pawn.LabelShort}} 的中期记忆片段（Medium Term Memories）合并为**6-8 条**个高层次的传记式摘要。
                  
                  记忆片段：
                  {{mtmContext}}
        
                  任务：
                  1. 多维度归纳：找出这段时期的主要生活基调（如生存主线、人际长弧、重大危机、平稳发展期）。
                  2. 去芜存菁：忽略琐事，除非它是生活基调的一部分。
                  3. 'summary' (简体中文)：
                     - 以第三人称撰写传记风格的摘要。
                     - 着重描述对角色性格、心境或人际关系的深层影响。
                     - 对于判定为 Importance=5 的记忆，触发「闪光灯记忆」模式：必须保留当时的场景描写、强烈的情绪形容词以及具体的互动对象名字。
                     - **严禁**使用相对时间（如“三天前”），允许使用模糊时间（如“这段时期”）或绝对时间（如“5501年”）。
                  4. 'keywords'：
                     **必须且只能**从该条综述所涵盖的原始片段中选择最核心的 3-5 个 keywords。
                     严禁发明新的关键词。
                  5. 'importance'：
                     - 继承原则：若包含高重要性事件(4-5)，合并后的记忆必须继承高分。
                     - 综述原则：若是平凡生活的总述，提升至 3 分。
                  6. 'source_ids'：
                     - **必须**列出该条记忆涵盖了哪些原始片段。
        
                  输出包含 'memories' 数组的 JSON 对象：
                  {
                    "memories": [
                      { 
                        "summary": "...", 
                        "keywords": ["..."], 
                        "importance": 3,
                        "source_ids": [1, 2, 5] // 必须列出合并到此条记忆的所有原始片段编号
                      },
                      ...
                    ]
                  }
                """;

            try
            {
                var request = new TalkRequest(prompt, pawn, currentTick);
                var result = await QueryMemory<MemoryListDto>(request);

                if (result?.Memories == null) return [];

                return result.Memories
                    .Where(m => m != null)
                    .Select(m =>
                    {
                        // 計算權重平均時間與 AccessCount
                        var (calculatedTick, avgAccess) = CalculateMergedMetadata(m.SourceIds, mtmList, currentTick);

                        return new MemoryRecord
                        {
                            Summary = m.Summary,
                            Keywords = m.Keywords ?? [],
                            Importance = Mathf.Clamp(m.Importance, 1, 5),
                            CreatedTick = calculatedTick,
                            AccessCount = avgAccess
                        };
                    }).ToList();
            }
            catch (Exception ex)
            {
                Messages.Message("RimTalk.MemoryService.ConsolidateFailed".Translate(ex.Message), MessageTypeDefOf.NegativeEvent, false);
                return [];
            }
        }

        /// <summary>
        /// 統一計算合併後的記憶元數據 (時間與活躍度)。
        /// </summary>
        /// <param name="sourceIds">來源 ID 列表</param>
        /// <param name="sources">原始記憶來源列表</param>
        /// <param name="fallbackTick">備用時間 (通常是當前時間)</param>
        /// <returns>(加權平均時間, 平均活躍度)</returns>
        private static (int Tick, int AccessCount) CalculateMergedMetadata(List<int> sourceIds, List<MemoryRecord> sources, int fallbackTick)
        {
            if (sourceIds == null || !sourceIds.Any() || sources.NullOrEmpty())
            {
                return (fallbackTick, 0);
            }

            var validTicks = new List<long>();
            var validAccess = new List<int>();

            foreach (var id in sourceIds)
            {
                int index = id - 1; // ID 轉換為 Index (Prompt 中 ID 從 1 開始)
                if (index >= 0 && index < sources.Count)
                {
                    var record = sources[index];
                    validTicks.Add(record.CreatedTick);
                    validAccess.Add(record.AccessCount);
                }
            }

            int avgTick = validTicks.Any() ? (int)validTicks.Average() : fallbackTick;
            int avgAccess = validAccess.Any() ? (int)validAccess.Average() : 0;

            return (avgTick, avgAccess);
        }

        // --- 記憶管理與檢索 ---

        /// <summary>
        /// 新增一條短期記憶 (STM) 到 Pawn 的記憶庫。
        /// 若觸發總結閾值，會回傳 true 提示調用者。
        /// </summary>
        public static bool AddMemory(Pawn pawn, MemoryRecord record)
        {
            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();
            if (comp == null || pawn == null) return false;

            PawnMemoryData data;

            // 1. 鎖定字典僅用於「查找或建立」
            lock (comp.PawnMemories)
            {
                // 獲取或建立 PawnMemoryData
                if (!comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out data))
                {
                    data = new PawnMemoryData { Pawn = pawn };
                    comp.PawnMemories[pawn.thingIDNumber] = data;
                }
            }

            // 2. 鎖定資料物件用於「修改列表」
            // 這樣就與 TriggerStmToMtmSummary 的 lock(data) 互斥了
            lock (data)
            {
                // 加入 STM
                data.ShortTermMemories.Add(record);
                data.NewShortMemoriesSinceSummary++;

                // 檢查是否達到總結閾值 (30條)
                return data.NewShortMemoriesSinceSummary >= MaxShortMemories;
            }
        }

        // [New] 輔助類別：用於排序
        private class ScoredMemory
        {
            public MemoryRecord Memory;
            public float Score;
        }

        // [MODIFY] Updated GetRelevantMemories
        // [NEW] GetRelevantMemories (分離記憶與常識檢索)
        // 回傳 Tuple: (個人記憶列表, 常識列表)
        /// <summary>
        /// 根據上下文檢索相關記憶 (Short + Medium + Long + Common Knowledge)。 (導入 TF-IDF、時間衰減與動態閾值)
        /// </summary>
        public static (List<MemoryRecord> memories, List<MemoryRecord> knowledge) GetRelevantMemories(string context, Pawn pawn)
        {
            if (string.IsNullOrWhiteSpace(context)) return ([], []);

            // [NEW] 內部獲取 MemoryBlock
            string memoryBlock = null;
            List<string> memoryBlockTags = [];
            var memoryBlockMessages = BuildMemoryBlockFromHistory(pawn);
            if (memoryBlockMessages.Any())
            {
                memoryBlock = memoryBlockMessages[0].message;
                memoryBlockTags = CoreTagMapper.GetTextTags(memoryBlock);  // [NEW] 直接提取標籤
            }

            string memoryBlockContext = memoryBlock + "\n" + string.Join(", ", memoryBlockTags);

            // 1. 準備資料來源（分類收集）
            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();
            var stmList = new List<MemoryRecord>();
            var mtmList = new List<MemoryRecord>();
            var ltmList = new List<MemoryRecord>();

            if (comp != null && pawn != null)
            {
                lock (comp.PawnMemories) // 讀取鎖
                {
                    if (comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data))
                    {
                        lock (data)
                        {
                            // 收集所有層級記憶作為候選
                            // [FIX] Use null-coalescing operator for cleaner null safety
                            stmList = (data.ShortTermMemories ?? []).ToList();
                            mtmList = (data.MediumTermMemories ?? []).ToList();
                            ltmList = (data.LongTermMemories ?? []).ToList();
                        }
                    }
                }
            }

            // 2. 設定參數
            const int memoryLimit = 8;
            const int stmLimit = 3;
            const int ltmLimit = 1;
            const int knowledgeLimit = 10;

            // 權重
            const float memoryBlockScoreMultiplier = 0.3f; // MemoryBlock 匹配的權重
            float weightKeyword = Settings.Get().KeywordWeight;
            float weightImportance = Settings.Get().MemoryImportanceWeight;
            const float weightAccess = 0.5f;
            const float timeDecayHalfLifeDays = 60f; // 1年半衰期 (60天遊戲天數)
            int currentTick = GenTicks.TicksGame; // 使用 GenTicks 較安全
            const float gracePeriodDays = 15f; // 新手保護期 15 天
            const float standardLength = 5.0f; // 用於正規化關鍵字權重

            // 3. 合併所有記憶用於 TF-IDF 計算
            var allMemories = stmList.Concat(mtmList).Concat(ltmList).ToList();
            var keywordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int totalDocs = allMemories.Count;

            foreach (var m in allMemories)
            {
                if (m.Keywords == null) continue;
                foreach (var k in m.Keywords)
                {
                    if (!keywordCounts.ContainsKey(k)) keywordCounts[k] = 0;
                    keywordCounts[k]++;
                }
            }

            // 4. 計分函數
            float CalculateScore(MemoryRecord mem)
            {
                if (mem.Keywords.NullOrEmpty()) return -1f;

                float rarityScoreSum = 0f;
                int matchCount = 0;
                bool hasContextMatch = false;  // [NEW] 追蹤是否有主上下文匹配

                foreach (var k in mem.Keywords)
                {
                    // 使用 IndexOf 避免不必要的 String Alloc
                    bool matchedInContext = context.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchedInMemoryBlock = !string.IsNullOrEmpty(memoryBlockContext) &&
                                                memoryBlockContext.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (matchedInContext)
                    {
                        hasContextMatch = true;  // [NEW] 標記有主上下文匹配
                        matchCount++;
                        int count = keywordCounts.TryGetValue(k, out int c) ? c : 0;
                        float rarity = (float)Math.Log((double)totalDocs / (count + 1));
                        rarityScoreSum += rarity;
                    }
                    else if (matchedInMemoryBlock)
                    {
                        // MemoryBlock 匹配也計入（先以 100% 計算，最後再打折）
                        matchCount++;
                        int count = keywordCounts.TryGetValue(k, out int c) ? c : 0;
                        float rarity = (float)Math.Log((double)totalDocs / (count + 1));
                        rarityScoreSum += rarity;
                    }
                }
                if (matchCount == 0) return -1f; // 必須至少命中一個
                // 修正係數：關鍵字少的記憶，單個命中的價值較高
                float lengthMultiplier = standardLength / Math.Max(mem.Keywords.Count, 1);
                // 時間衰減 (含階梯保底)
                float elapsedDays = (currentTick - mem.CreatedTick) / 60000f;
                // 計算有效衰減天數(從保護期後才開始衰減)：若在保護期內則為 0，超過則減去保護期
                float effectiveDecayDays = Math.Max(0, elapsedDays - gracePeriodDays);
                float rawDecay = (float)Math.Exp(-effectiveDecayDays / timeDecayHalfLifeDays);
                // 保底邏輯：Importance 越高，衰減下限越高
                float minFloor = mem.Importance switch
                {
                    >= 5 => 0.5f, // 刻骨銘心：永不低於 50%
                    4 => 0.3f,    // 重大：永不低於 30%
                    _ => 0f       // 普通：可衰減至 0
                };
                // 新手保護期 (15天內不衰減)
                if (elapsedDays < 15f) rawDecay = 1.0f;
                float timeDecayFactor = Math.Max(rawDecay, minFloor);
                // 最終公式
                // Score = (關鍵字稀有度 * 修正 * W_Key) + (重要性 * W_Imp * 時間係數) - (提及次數罰分)
                float totalScore = (rarityScoreSum * lengthMultiplier * weightKeyword)
                     + (mem.Importance * weightImportance * timeDecayFactor)
                     - (mem.AccessCount * weightAccess);

                // [NEW] 如果沒有任何主上下文匹配，總分打 30% 折扣
                if (!hasContextMatch)
                {
                    totalScore *= memoryBlockScoreMultiplier;  // 0.3f
                }
                return totalScore;
            }

            // 5. 分層檢索
            var relevantMemories = new List<MemoryRecord>();

            // === 階段 1：STM（上限 3 條）===
            var scoredStm = stmList
                .Select(m => new { Memory = m, Score = CalculateScore(m) })
                .Where(x => x.Score >= 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Memory.AccessCount)
                .ToList();
            if (scoredStm.Any())
            {
                float stmMaxScore = scoredStm[0].Score;
                float stmThreshold = stmMaxScore * 0.5f;
                var selectedStm = scoredStm
                    .Where(x => x.Score >= stmThreshold)
                    .Take(stmLimit)
                    .Select(x => x.Memory)
                    .ToList();
                relevantMemories.AddRange(selectedStm);
            }

            // === 階段 2：LTM（上限 1 條，無門檻）===
            var scoredLtm = ltmList
                .Select(m => new { Memory = m, Score = CalculateScore(m) })
                .Where(x => x.Score >= 0)
                .OrderByDescending(x => x.Score)
                .ToList();
            if (scoredLtm.Any())
            {
                var selectedLtm = scoredLtm.Take(ltmLimit).Select(x => x.Memory).ToList();
                relevantMemories.AddRange(selectedLtm);
            }

            // === 階段 3：剩餘名額（從所有未選取的記憶中選）===
            int remainingSlots = memoryLimit - relevantMemories.Count;
            if (remainingSlots > 0)
            {
                // 排除已選取的記憶
                var alreadySelected = new HashSet<MemoryRecord>(relevantMemories);
                var remainingCandidates = allMemories
                    .Where(m => !alreadySelected.Contains(m))
                    .Select(m => new { Memory = m, Score = CalculateScore(m) })
                    .Where(x => x.Score >= 0)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Memory.AccessCount)
                    .ToList();
                if (remainingCandidates.Any())
                {
                    float remainingMaxScore = remainingCandidates[0].Score;
                    float remainingThreshold = remainingMaxScore * 0.5f;
                    var selectedRemaining = remainingCandidates
                        .Where(x => x.Score >= remainingThreshold)
                        .Take(remainingSlots)
                        .Select(x => x.Memory)
                        .ToList();
                    relevantMemories.AddRange(selectedRemaining);
                }
            }

            // 6. 更新 AccessCount
            foreach (var m in relevantMemories) m.AccessCount++;

            // 7. 常識庫檢索 (優化匹配邏輯)
            // [NEW] 實作評分公式與 AccessCount
            var allKnowledge = comp?.CommonKnowledgeStore ?? [];
            var scoredKnowledge = new List<ScoredMemory>();

            foreach (var k in allKnowledge)
            {
                if (k.Keywords.NullOrEmpty()) continue;
                int matchCount = 0;
                foreach (var key in k.Keywords)
                {
                    if (context.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                        matchCount++;
                }

                if (matchCount == 0) continue;

                // Formula: score = (Keyword * lengthMultiplier * weightKeyword) + (Importance * weightImportance)

                // 1. Keyword Score (Match Count)
                float keywordScore = matchCount;
                // 2. Length Multiplier
                float lengthMultiplier = standardLength / Math.Max(k.Keywords.Count, 1);
                // 3. Final Score Calculation
                float score = (keywordScore * lengthMultiplier * weightKeyword)
                            + (k.Importance * weightImportance);
                scoredKnowledge.Add(new ScoredMemory { Memory = k, Score = score });
            }

            var relevantKnowledge = scoredKnowledge
                .OrderByDescending(x => x.Score)
                .Take(knowledgeLimit)
                .Select(x => x.Memory)
                .ToList();

            // [NEW] 增加 AccessCount
            foreach (var k in relevantKnowledge) k.AccessCount++;

            return (relevantMemories, relevantKnowledge);
        }

        // --- 輔助方法 ---

        /// <summary>
        /// 將 MemoryRecord 列表格式化為帶 ID 與時間的字串。
        /// </summary>
        private static string FormatMemoriesWithIds(List<MemoryRecord> memories)
        {
            if (memories.NullOrEmpty()) return "";

            var sb = new StringBuilder();
            long baseTick = memories.Min(m => m.CreatedTick); // 用於計算相對天數

            for (int i = 0; i < memories.Count; i++)
            {
                var m = memories[i];

                // 計算相對天數
                int dayIndex = (int)((m.CreatedTick - baseTick) / 60000);
                if (dayIndex < 0) dayIndex = 0;

                string tags = string.Join(",", m.Keywords);
                // ID 從 1 開始
                sb.AppendLine($"[ID: {i + 1}] [Day: {dayIndex}] (Imp:{m.Importance}) [{tags}] {m.Summary}");
            }
            return sb.ToString();
        }

        // 用於從外部獲取所有關鍵詞 (保持標籤一致性)
        // [NEW] 實作 GetAllExistingKeywords (適配個人記憶與常識庫)
        public static string GetAllExistingKeywords(Pawn pawn)
        {
            var keywords = new HashSet<string>();

            // 這裡插入核心關鍵詞庫 (CoreMemoryTags)
            foreach (var coreTag in CoreMemoryTags.KeywordMap)
            {
                keywords.Add(coreTag);
            }

            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();

            if (comp == null) return "None";

            // 1. 從個人記憶收集 (STM + MTM + LTM)
            if (comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data) && data != null)
            {
                // [FIX] Use null-coalescing operator for cleaner null safety
                if (data.ShortTermMemories != null)
                    foreach (var m in data.ShortTermMemories) keywords.AddRange(m.Keywords ?? []);
                if (data.MediumTermMemories != null)
                    foreach (var m in data.MediumTermMemories) keywords.AddRange(m.Keywords ?? []);
                if (data.LongTermMemories != null)
                    foreach (var m in data.LongTermMemories) keywords.AddRange(m.Keywords ?? []);
            }

            // 2. 從常識庫收集
            if (comp.CommonKnowledgeStore != null)
            {
                foreach (var k in comp.CommonKnowledgeStore) keywords.AddRange(k.Keywords ?? []);
            }

            if (keywords.Count == 0) return "None";

            // 限制數量避免 Prompt 過長
            return string.Join(", ", keywords.Take(1000));
        }

        /// <summary>
        /// 根據權重與時間衰減剔除多餘的長期記憶 (From Legacy)
        /// </summary>
        /// <param name="ltm">長期記憶列表</param>
        /// <param name="maxCount">最大保留數量</param>
        /// <param name="currentTick">當前遊戲時間</param>
        public static void PruneLongTermMemories(List<MemoryRecord> ltm, int maxCount, int currentTick)
        {
            if (ltm.Count <= maxCount) return;

            // 讀取設定中的權重
            float weightImportance = Settings.Get().MemoryImportanceWeight;

            float weightAccess = 0.5f;           // 提及次數的權重 (活躍度)
            float timeDecayHalfLifeDays = 60f;   // 半衰期：60天 (1年權重減半)

            // 寬限期 (Grace Period)：15 天
            // 剛生成的記憶通常提及數為 0，且時間亦短，給予豁免以免被 "秒殺"
            float gracePeriodDays = 15f;
            int gracePeriodTicks = (int)(gracePeriodDays * 60000);

            int removeCount = ltm.Count - maxCount;

            // 定義內部計分函數 (Local Function)
            float CalculateRetentionScore(MemoryRecord m)
            {
                // 1. 新手保護期：給予極大分數 (9999)，確保絕對安全
                if (currentTick - m.CreatedTick < gracePeriodTicks) return 9999f;
                // 2. 計算時間衰減 (Time Decay)
                // 公式：Exp(-elapsedDays / halfLife)
                float elapsedDays = (currentTick - m.CreatedTick) / 60000f;
                if (elapsedDays < 0) elapsedDays = 0; // 防呆
                float effectiveDecayDays = Math.Max(0, elapsedDays - gracePeriodDays);
                float rawDecay = (float)Math.Exp(-effectiveDecayDays / timeDecayHalfLifeDays);
                // 3. 階梯式保底機制 (Stepped Floor)
                // 即使時間久遠，極重要的記憶也不應完全衰減為 0
                float minFloor = m.Importance switch
                {
                    >= 5 => 0.5f, // 刻骨銘心：保留 50% 底限
                    4 => 0.3f, // 重大事件：保留 30% 底限
                    _ => 0f    // 普通記憶：可自然衰減至 0
                };
                // 取二者最大值作為最終衰減係數
                float finalDecay = Math.Max(rawDecay, minFloor);
                // 4. 總分公式
                // 總分 = (本質價值 * 衰減) + (實用價值 * 衰減)
                // 這樣即使是常被用到的記憶，如果本身不重要，也會隨時間慢慢變得沒那麼"強勢"
                return (m.Importance * weightImportance * finalDecay)
                     + (m.AccessCount * weightAccess * rawDecay); // 這裡其實是用 rawDecay 還是 finalDecay 可自行決定，使用 finalDecay 對老記憶更友善
            }

            // 計算分數並排序
            var candidates = ltm
                .Select(m => new { Memory = m, Score = CalculateRetentionScore(m) })
                .OrderBy(x => x.Score) // 分數低的排前面 (準備被刪除)
                .ToList();
            // 執行移除
            for (int i = 0; i < removeCount; i++)
            {
                if (i < candidates.Count)
                {
                    // [SAFETY] 如果連最低分的候選者都處於保護期 (9999分)，代表所有記憶都還很新
                    // 此時停止刪除，暫時允許超過上限 (超額部分會在過期後被修正)
                    if (candidates[i].Score >= 9900f) break;
                    ltm.Remove(candidates[i].Memory);
                }
            }
        }

        // [MODIFY] Change visibility from private to public
        /// <summary>
        /// 將遊戲 Tick 轉換為相對時間描述 (例如 "3天前", "1季前")。
        /// </summary>
        public static string GetTimeAgo(int createdTick)
        {
            if (createdTick <= 0) return "RimTalk.TimeAgo.Unknown".Translate();

            int diff = GenTicks.TicksGame - createdTick;
            if (diff < 0) diff = 0; // 防呆

            float hours = diff / 2500f; // 1 hour = 2500 ticks

            if (hours < 1f) return "RimTalk.TimeAgo.JustNow".Translate();
            if (hours < 24f) return "RimTalk.TimeAgo.HoursAgo".Translate((int)hours);

            float days = hours / 24f; // 1 day = 24 hours

            if (days < 15f) return "RimTalk.TimeAgo.DaysAgo".Translate((int)days);

            float seasons = days / 15f; // 1 season = 15 days

            if (seasons < 4.0f) return "RimTalk.TimeAgo.SeasonsAgo".Translate((int)seasons); // "{0}季前"

            float years = seasons / 4f; // 1 year = 4 seasons

            return "RimTalk.TimeAgo.YearsAgo".Translate((int)years);
        }

        /// <summary>
        /// [NEW] 格式化個人回憶字串，供 BuildContext 使用
        /// </summary>
        public static string FormatRecalledMemories(List<MemoryRecord> memories)
        {
            if (memories.NullOrEmpty()) return "";
            var sb = new StringBuilder();
            sb.AppendLine("Recalled Memories:"); // 可翻譯
            foreach (var m in memories)
            {
                string timeAgo = GetTimeAgo(m.CreatedTick);
                // - [3天前] Summary
                sb.AppendLine($"- [{timeAgo}] {m.Summary}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// [NEW] 編輯記憶內容
        /// </summary>
        public static void EditMemory(Pawn pawn, MemoryRecord memory, string newSummary, List<string> newKeywords, int newImportance)
        {
            if (pawn == null || memory == null) return;
            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();
            if (comp == null) return;
            lock (comp.PawnMemories)
            {
                // 但為了線程安全，我們在 lock 範圍內修改它的屬性

                // 直接修改屬性
                lock (memory) // 額外鎖定記憶物件本身，防止讀取時發生撕裂 (雖然字串引用替換是原子的)
                {
                    memory.Summary = newSummary;
                    memory.Keywords = newKeywords;
                    memory.Importance = Mathf.Clamp(newImportance, 1, 5);
                }
            }
        }

        /// <summary>
        /// [NEW] 刪除記憶
        /// </summary>
        public static void DeleteMemory(Pawn pawn, MemoryRecord memory)
        {
            if (pawn == null || memory == null) return;
            var comp = Find.World?.GetComponent<RimTalkWorldComponent>();
            if (comp == null) return;
            lock (comp.PawnMemories)
            {
                if (comp.PawnMemories.TryGetValue(pawn.thingIDNumber, out var data))
                {
                    lock (data)
                    {
                        // 嘗試從三個列表中移除 (因為我們不知道它在哪一層)
                        if (data.ShortTermMemories?.Remove(memory) == true) return;
                        if (data.MediumTermMemories?.Remove(memory) == true) return;
                        if (data.LongTermMemories?.Remove(memory) == true) return;
                    }
                }
            }
        }
    }
}
