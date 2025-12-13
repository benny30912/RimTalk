using RimTalk.Client; // 需要引用 Client
using RimTalk.Data;
using RimTalk.Error;  // 需要引用 Error
using RimTalk.Util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
// ★ 解決 Logger 歧義
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Service;

public static class MemoryService
{
    // 用於接收 LLM 單條記憶生成的 DTO
    [DataContract]
    private class MemoryGenerationDto : IJsonData
    {
        [DataMember(Name = "summary")] public string Summary = "";
        [DataMember(Name = "keywords")] public List<string> Keywords = new();
        [DataMember(Name = "importance")] public int Importance = 0;
        // ★ 新增：用於追蹤長期記憶的來源片段 ID
        [DataMember(Name = "source_ids")] public List<int> SourceIds = new();

        public string GetText() => Summary;
    }

    // 用於接收 LLM 批量記憶生成的 DTO
    [DataContract]
    private class MemoryListDto : IJsonData
    {
        [DataMember(Name = "memories")] public List<MemoryGenerationDto> Memories = new();

        public string GetText() => $"Generated {Memories?.Count ?? 0} memories";
    }

    // ★ 新增：私有的記憶查詢方法 (取代 AIService.Query)
    // ★ 修改：徹底移除 ApiHistory 和 Stats 的調用
    private static async Task<T> QueryMemory<T>(TalkRequest request) where T : class, IJsonData
    {
        // 移除：var apiLog = ApiHistory.AddRequest(...) 
        // 因為 ApiHistory 不是線程安全的，且我們決定不記錄背景任務

        try
        {
            // 2. 透過 Factory 取得記憶專用 Client
            // 3. 使用 AIErrorHandler 處理重試 (網路錯誤等)
            // ★ 修改：傳入 ClientType.Memory
            var payload = await AIErrorHandler.HandleWithRetry(async () =>
            {
                // ★ 呼叫獨立的 GetMemoryClientAsync
                var client = await AIClientFactory.GetMemoryClientAsync();
                if (client == null) return null;

                // 記憶生成不使用 System Instruction，直接傳 Prompt
                return await client.GetChatCompletionAsync("", [(Role.User, request.Prompt)]);
            }, isMemory: true); // ★ 指定 isMemory = true

            if (payload == null)
            {
                // 失敗時僅輸出 Log 到控制台，不寫入遊戲內歷史
                Logger.Warning($"Memory generation failed (Payload is null) for: {request.Initiator?.LabelShort}");
                return null;
            }

            // 移除：Stats.IncrementCalls();
            // 移除：Stats.IncrementTokens(payload.TokenCount);
            // Stats 類別也不是線程安全的，背景寫入會導致崩潰

            var jsonData = JsonUtil.DeserializeFromJson<T>(payload.Response);

            // 移除：ApiHistory.AddResponse(...)

            return jsonData;
        }
        catch (Exception ex)
        {
            Logger.Error($"Memory Query Error: {ex.Message}");
            return null;
        }
    }

    // ★ 修改：請確保這裡的最後一個參數名稱是 fallbackTick，而不是 currentTick
    /// <summary>
    /// 將短期對話歷史(30條=15組)總結為多條中期記憶(15條)(繼承短期記憶時間)
    /// </summary>
    public static async Task<List<MemoryRecord>> SummarizeToMediumAsync(List<TalkMessageEntry> messages, Pawn pawn, string existingKeywords, int fallbackTick)
    {
        if (messages.NullOrEmpty()) return [];

        string conversationText = FormatConversation(messages);

        // ★ 新增：提取每一組對話的時間戳
        // 假設 messages 是 [User, AI, User, AI...] 的順序
        // 我們取 User (發起話題者) 的時間作為該段記憶的時間
        var conversationTicks = new List<int>();
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i].Role == Role.User)
            {
                conversationTicks.Add(messages[i].Tick);
            }
        }

        // ★ 更新：Medium Prompt (Vivid, No Relative Time, Strict Keywords)
        string prompt =
            $$"""
             分析以下对话历史（context + dialogue）。
             目标：{{pawn.LabelShort}}

             历史：
             {{conversationText}}
     
             任务：
             为每组 [context]+[dialogue] 生成相应的记忆记录。
             1. 'summary'：以第三人称生动概括对话全貌。 
                 核心要求：
                 - 必须记录独有的互动细节（如：给人起的绰号、特定的玩笑梗、承诺或激烈的语气）。 
                 - **不要**直接复制粘贴大段原始对话，而是将其转述（例如："Ray嘲笑Benny是胆小鬼" 而非 "Ray说：'Benny你是胆小鬼'"）。 
                 - 目标：提供足够的情感上下文，以便未来能自然地回想起这段经历的氛围，而不仅有事实。
                 - **严禁**使用相对时间（如“昨天”、“三天前”），因为这会随时间失效。
                 - 允许使用模糊跨度（如“这段时期”、“近期”）或绝对时间（如“5501年”）。
             2. 'keywords'：**必须且只能**从以下两个来源中选择总共 3-5 个标签：
                 A. **本次[context]中实际出现的词汇**。 
                 B. **现有的参考标签列表**：{{existingKeywords}}。 
                 关键词组合策略：
                 - 必选 (Anchor)：1-2 个具体的实体名词（人名、生物名、物品名、地名）。 **必须优先从[context]中提取。 **
                 - 必选 (Link)：1-2 个核心概念/动作（优先从参考标签列表中选择）。这用于建立与其他记忆的关联。 
                 - 可选：1 个强烈的情感/状态。 
                 **禁止创造文本中不存在的新词汇。 **
                 **绝对不要包含角色名“{{pawn.LabelShort}}”**
             3. 'importance'：根据以下标准严格评分（1-5）：
                - 1 (琐碎)：日常闲聊、天气、吃饭、无意义的抱怨。
                - 2 (普通)：工作交流、轻微的不适、一般的交易。
                - 3 (值得记住)：建立友谊/仇恨、轻伤、社交争吵、完成任务。
                - 4 (重大)：精神崩溃、袭击战斗、确立恋爱关系、重伤/疾病。
                - 5 (刻骨铭心)：死亡、结婚、永久性残疾、家园毁灭。
                提示：对于大多数日常对话，评分不应超过 2。只有真正危及生命或改变关系的事件才能评为 4 或 5。

             重要：summary 字段必须使用简体中文。
     
             输出包含 'memories' 数组的 JSON 对象：
             {
               "memories": [
                 { "summary": "...", "keywords": ["..."], "importance": 3 },
                 ...
               ]
             }
             """;

        try
        {
            // ★ 修改：使用新的建構函數，傳入 fallbackTick
            var request = new TalkRequest(prompt, pawn, fallbackTick);
            // ★ 修改：呼叫自己的 QueryMemory
            var result = await QueryMemory<MemoryListDto>(request);

            if (result?.Memories == null) return [];

            // ★ 修改：增加 Where(m => m != null) 過濾，並處理 Summary 為 null 的情況
            // ★ 修改：按索引對應時間
            // 因為 Prompt 要求 "為每組生成記錄"，理想情況下 LLM 回傳的列表長度會等於 conversationTicks 的長度
            // 若 LLM 合併了某些對話，我們依序使用時間；若 LLM 產生更多，則後面的使用最後一個已知時間或 fallback
            return result.Memories
                .Where(m => m != null)
                .Select((m, index) =>
                {
                    // 嘗試獲取對應的 Tick，如果索引超出範圍（LLM 產生了額外記憶），則使用最後一個對話的時間，或是 fallbackTick
                    int tick = index < conversationTicks.Count
                        ? conversationTicks[index]
                        : (conversationTicks.Any() ? conversationTicks.Last() : fallbackTick);

                    // 如果從舊存檔讀取，Tick 可能是 0，這時使用 fallbackTick (當前時間)
                    if (tick <= 0) tick = fallbackTick;

                    return new MemoryRecord
                    {
                        Summary = m.Summary ?? "...", // 防止 Summary 為 null
                        Keywords = m.Keywords ?? [],  // 防止 Keywords 為 null
                        Importance = Mathf.Clamp(m.Importance, 1, 5),
                        AccessCount = 0,
                        CreatedTick = tick
                    };
                }).ToList();
        }
        catch (Exception ex)
        {
            // 保持使用 Messages 提示玩家，這是安全的（Unity 主線程會處理 UI 隊列）
            // 但為了保險，建議將 historical 設為 false
            Messages.Message("RimTalk.MemoryService.SummarizeFailed".Translate(ex.Message), MessageTypeDefOf.NegativeEvent, false);
            return [];
        }
    }

    // ★ 修改：新增參數 currentTick
    /// <summary>
    /// 将大量中期记忆合并为数条长期记忆 (使用 SourceIds 精確計算時間)
    /// </summary>
    public static async Task<List<MemoryRecord>> ConsolidateToLongAsync(List<MemoryRecord> memories, Pawn pawn, int currentTick)
    {
        if (memories.NullOrEmpty()) return [];

        // ★ 更新：注入 ID 和 Day 
        string memoryText = FormatMemoriesWithIds(memories);

        // ★ 更新：Long Prompt (Biographical, Multi-Dim, Clean, Source Tracking)
        string prompt =
            $$"""
              将以下记忆片段合并为 3-6 个独特的高层次事件摘要。
              目标：{{pawn.LabelShort}}

              记忆片段池：
              {{memoryText}}

              任务：
              1. 多维度归纳：
                 找出这段时期并行发生的几个主要主题（如生存主线、人际长弧、重大危机、平稳发展期）并分别归纳。
              2. 去芜存菁：
                 彻底忽略无意义的日常琐事，除非它们构成了主要的生活基调。
                 确保生成的记忆是涵盖数天甚至数周的高层概括。
              3. 'summary'：
                 以第三人称书写传记风格的摘要，着重描述这段经历对他的长远影响、心境变化或人际格局的改变。
                 **时间禁令：**
                 - **严禁**使用相对时间（如“昨天”、“三天前”）。
                 - 允许使用模糊跨度（如“这段时期”）或绝对时间（如“5501年”）。
              4. 'keywords'：
                 **必须且只能**从该条综述所涵盖的原始片段中选择最核心的 3-5 个 keywords。
                 严禁发明新的关键词。
                 **绝对不要包含角色名“{{pawn.LabelShort}}”**
              5. 'importance'：
                 - 继承原则：如果合并的片段中包含高重要性事件（4-5分），**必须继承该高分**。
                 - 综述原则：由日常琐事合并成的“生活综述”，请提升至 3 分。

              重要：summary 字段必须使用简体中文。 

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
            // ★ 修改：使用新的建構函數，傳入 currentTick
            var request = new TalkRequest(prompt, pawn, currentTick);
            // ★ 修改：呼叫自己的 QueryMemory
            var result = await QueryMemory<MemoryListDto>(request);

            if (result?.Memories == null) return [];

            // ★ 修改：同樣增加安全過濾
            return result.Memories
                .Where(m => m != null)
                .Select(m =>
                {
                    // ★ 核心邏輯：計算 SourceIds 的平均時間與平均 AccessCount
                    int calculatedTick = currentTick;
                    var sourceAccessCounts = new List<int>();

                    if (m.SourceIds != null && m.SourceIds.Any())
                    {
                        var validTicks = new List<long>();
                        foreach (var id in m.SourceIds)
                        {
                            // 使用索引查找 (ID 從 1 開始，所以索引是 ID - 1)
                            int index = id - 1;
                            if (index >= 0 && index < memories.Count)
                            {
                                var sourceMem = memories[index];
                                sourceAccessCounts.Add(sourceMem.AccessCount); // 收集次數

                                if (sourceMem.CreatedTick > 0)
                                {
                                    validTicks.Add(sourceMem.CreatedTick);
                                }
                            }
                        }

                        if (validTicks.Any())
                        {
                            calculatedTick = (int)validTicks.Average();
                        }
                    }

                    // 計算平均提及次數 (四雪五入或無條件捨去皆可，這裡用整數除法簡單處理)
                    int avgAccess = sourceAccessCounts.Any() ? (int)sourceAccessCounts.Average() : 0;

                    return new MemoryRecord
                    {
                        Summary = m.Summary ?? "...",
                        Keywords = m.Keywords ?? [],
                        Importance = Mathf.Clamp(m.Importance, 1, 5),
                        AccessCount = avgAccess, // ★ 賦值平均數
                        CreatedTick = calculatedTick // 使用精確計算出的時間
                    };
                }).ToList();
        }
        catch (Exception ex)
        {
            // ★ 修改：使用 Messages.Message 並暴露翻譯 Key
            Messages.Message("RimTalk.MemoryService.ConsolidateFailed".Translate(ex.Message), MessageTypeDefOf.NegativeEvent, false);
            return [];
        }
    }

    /// <summary>
    /// 根據權重剔除多餘的長期記憶
    /// </summary>
    // ★ 修改：增加 currentTick 參數，避免在背景呼叫 Find.TickManager
    public static void PruneLongTermMemories(List<MemoryRecord> ltm, int maxCount, int currentTick)
    {
        if (ltm.Count <= maxCount) return;

        // 設定權重
        float weightImportance = Settings.Get().MemoryImportanceWeight;
        float weightAccess = 0.5f;     // 提及次數的權重
        float timeDecayHalfLifeDays = 60f; // 1年半衰期

        // 寬限期 (Grace Period)：15 天 (0.25 年)
        // 剛生成的記憶通常提及數為 0，給予豁免權以免被秒殺
        int gracePeriodTicks = 15 * 60000;

        int removeCount = ltm.Count - maxCount;

        // 計算保留分數：分數越低越容易被移除
        var candidates = ltm
            .Select(m => new { Memory = m, Score = CalculateRetentionScore(m) })
            .OrderBy(x => x.Score) // 分數低的排前面 (準備被刪)
            .ToList();

        // 內部計分函數
        float CalculateRetentionScore(MemoryRecord m)
        {
            // 1. 新手保護期：給予極大分數，確保不被刪除
            if (currentTick - m.CreatedTick < gracePeriodTicks) return 9999f;

            // 2. 計算原始衰減 (無保底)
            float elapsedDays = (currentTick - m.CreatedTick) / 60000f;
            if (elapsedDays < 0) elapsedDays = 0;
            float rawDecay = (float)Math.Exp(-elapsedDays / timeDecayHalfLifeDays);

            // 3. 計算階梯式保底衰減 (用於重要性)
            float minFloor = m.Importance switch
            {
                >= 5 => 0.5f,
                4 => 0.3f, // 微調：給 4 分一點生存空間
                _ => 0f
            };
            float flooringDecay = Math.Max(rawDecay, minFloor);

            // 4. 總分公式
            // 本質價值 (有保底) + 實用價值 (無保底，隨時間消逝)
            return (m.Importance * weightImportance * flooringDecay)
                 + (m.AccessCount * weightAccess * rawDecay);
        }

        // 執行移除
        for (int i = 0; i < removeCount; i++)
        {
            if (i < candidates.Count)
            {
                // 如果連最低分的都是保護期內的(9999)，那就什麼都不刪
                if (candidates[i].Score >= 9999f) break;

                ltm.Remove(candidates[i].Memory);
            }
        }
    }

    private class ScoredMemory
    {
        public MemoryRecord Memory;
        public float Score;
    }

    /// <summary>
    /// 根據當前 Context 檢索相關記憶與常識
    /// </summary>
    // ★ 修正：加入空值檢查
    // 修改方法簽名，增加不同的 limit 參數或直接在內部調整
    public static (List<MemoryRecord> memories, List<CommonKnowledgeData> knowledge) GetRelevantMemories(string context, Pawn pawn)
    {
        if (string.IsNullOrWhiteSpace(context)) return ([], []);

        var comp = Find.World.GetComponent<RimTalkWorldComponent>();
        var allMemories = new List<MemoryRecord>();

        // ★ 線程安全：讀取列表時必須加鎖
        if (comp != null)
        {
            lock (comp.SavedTalkHistories)
            {
                var history = comp.SavedTalkHistories.FirstOrDefault(x => x.Pawn == pawn);
                if (history != null)
                {
                    if (history.MediumTermMemories != null) allMemories.AddRange(history.MediumTermMemories);
                    if (history.LongTermMemories != null) allMemories.AddRange(history.LongTermMemories);
                }
            }
        }

        // 設定不同的限制
        int memoryLimit = 5;   // 個人記憶維持 5 條 (避免搶戲)
        int knowledgeLimit = 10; // 常識擴充到 10 條 (確保名詞解釋完整)

        // ★ 讀取設定權重
        float weightKeyword = Settings.Get().KeywordWeight;
        float weightImportance = Settings.Get().MemoryImportanceWeight;
        const float weightAccess = 0.5f; // 要求固定為 0.5 (微擾)

        // ★ 新增：時間衰減參數 (60000 ticks = 1 day)
        // 假設半衰期為 1 年 (60 days)，數值可根據需求微調
        const float timeDecayHalfLifeDays = 60f; 
        int currentTick = Find.TickManager.TicksGame;

        // ★ 定義標準長度 (Prompt 要求產生 3-5 個，我們以 5 作為標準化基底)
        const float standardLength = 5.0f;

        // 預先計算所有記憶中，每個關鍵詞出現的次數
        // 使用 StringComparer.OrdinalIgnoreCase 忽略大小寫
        var keywordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int totalDocs = allMemories.Count;
        foreach (var m in allMemories)
        {
            foreach (var k in m.Keywords)
            {
                if (!keywordCounts.ContainsKey(k)) keywordCounts[k] = 0;
                keywordCounts[k]++;
            }
        }

        var scoredMemories = new List<ScoredMemory>();

        foreach (var mem in allMemories)
        {
            if (mem.Keywords == null) continue;

            float rarityScoreSum = 0f;
            int matchCount = 0;

            foreach (var k in mem.Keywords)
            {
                // ★ 效能優化：使用 IndexOf 替代 ToLower + Contains
                // 這避免了在主執行緒產生大量字串垃圾 (GC Alloc)
                if (context.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matchCount++;
                    // 查表獲取詞頻，計算稀有度
                    int count = keywordCounts.TryGetValue(k, out int c) ? c : 0;
                    // IDF 公式：log(總文件數 / (包含該詞的文件數 + 1))
                    // 常見詞分數低，稀有詞分數高
                    float rarity = (float)Math.Log((double)totalDocs / (count + 1));
                    rarityScoreSum += rarity;
                }
            }

            // ★ 規則：無論如何至少要命中一個關鍵字
            if (matchCount == 0) continue;

            // ★ 新增：計算修正係數
            // 如果關鍵詞少於 5 個，每個命中的權重會放大
            // 例如只有 3 個關鍵詞，每個命中的價值是 5/3 = 1.66 倍
            int totalKeywords = mem.Keywords.Count;
            // 防呆：避免 totalKeywords 為 0 (雖然不應發生)
            if (totalKeywords < 1) totalKeywords = 1;

            float lengthMultiplier = standardLength / totalKeywords;

            // ★ 新增：時間衰減計算
            // 公式：e^(-elapsedDays / halfLife)
            // 結果會在 0.0 ~ 1.0 之間
            float elapsedDays = (currentTick - mem.CreatedTick) / 60000f;
            if (elapsedDays < 0) elapsedDays = 0; // 防呆

            float timeDecayFactor = (float)Math.Exp(-elapsedDays / timeDecayHalfLifeDays);

            // ★ 階梯式保底機制 (Stepped Floor)
            // Level 5 (刻骨銘心): 永遠保留 50% 強度 (至少比普通更值得記住)
            // Level 4 (重大事件): 永遠保留 30% 強度 (至少比琐碎更值得記住)
            // Level 1-3 (普通): 無保底，自然衰減至 0

            float minFloor = mem.Importance switch
            {
                >= 5 => 0.5f,
                4 => 0.3f,
                _ => 0f
            };

            timeDecayFactor = Math.Max(timeDecayFactor, minFloor);

            // 1. 計算分數，新公式：(關鍵字稀有分總和 * 修正係數 * W_key) + (重要性 * W_imp * 時間衰減係數) - (提及次數 * 0.5)
            float score = (rarityScoreSum * lengthMultiplier * weightKeyword) + (mem.Importance * weightImportance * timeDecayFactor) - (mem.AccessCount * weightAccess);

            scoredMemories.Add(new ScoredMemory { Memory = mem, Score = score });
        }

        var relevantMemories = new List<MemoryRecord>();
        
        if (scoredMemories.Any())
        {
            // 2. 排序，分數高的優先
            var sortedMemories = scoredMemories
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Memory.AccessCount) // 同分時優先選存取少的
                .ToList();

            // 3. 找出當前環境下的「天花板」
            float maxScore = sortedMemories[0].Score;
            // 4. 動態閾值：只允許分數達到最高分 50% 的記憶進入
            float dynamicThreshold = maxScore * 0.5f;
            //選取
            relevantMemories = sortedMemories
                .Where(x => x.Score >= dynamicThreshold)
                .Take(memoryLimit)
                .Select(x => x.Memory)
                .ToList();

            // 更新被選中記憶的 AccessCount
            foreach (var mem in relevantMemories)
            {
                mem.AccessCount++;
            }
        }

        // 檢索常識：計算匹配數 -> 優先取匹配度高的
        // (優化版：同樣使用 IndexOf 避免 GC)
        var allKnowledge = comp?.CommonKnowledgeStore ?? [];
        var scoredKnowledge = new List<(CommonKnowledgeData Knowledge, int MatchCount)>();

        foreach (var k in allKnowledge)
        {
            if (k.Keywords == null) continue;
            int count = 0;
            foreach (var key in k.Keywords)
            {
                if (context.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    count++;
                }
            }
            if (count > 0)
            {
                scoredKnowledge.Add((k, count));
            }
        }

        var relevantKnowledge = scoredKnowledge
            .OrderByDescending(x => x.MatchCount) // 優先顯示匹配關鍵字最多的常識
            .Take(knowledgeLimit)
            .Select(x => x.Knowledge)
            .ToList();

        return (relevantMemories, relevantKnowledge);
    }

    private static string FormatConversation(List<TalkMessageEntry> messages)
    {
        var sb = new StringBuilder();

        // 假設 messages 順序是 User, AI, User, AI...
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];

            if (msg.Role == Role.User)
            {
                // 提取並清理 User Prompt 中的情境
                string context = PromptService.ExtractContextFromPrompt(msg.Text);
                if (string.IsNullOrWhiteSpace(context)) context = "(No context)";
                sb.AppendLine($"[context]: {context.Trim()}");
            }
            else if (msg.Role == Role.AI)
            {
                // 嘗試解析 AI 回傳的 JSON 來獲取純對話文本
                string dialogueText = msg.Text;
                try
                {
                    // TalkHistory 存的是 List<TalkResponse> 的 JSON
                    var responses = JsonUtil.DeserializeFromJson<List<TalkResponse>>(msg.Text);
                    if (responses != null && responses.Any())
                    {
                        // ★ 建議這裡也改成包含名字，讓 LLM 總結時知道是誰說的
                        dialogueText = string.Join("\n", responses.Select(r => $"{r.Name}: {r.Text}"));
                    }
                }
                catch
                {
                    // 如果解析失敗（可能是舊格式或單純文字），就保留原樣
                }

                sb.AppendLine($"[dialogue]: {dialogueText.Trim()}");
                sb.AppendLine("---"); // 分隔線，讓 LLM 知道這是一組結束
            }
        }
        return sb.ToString();
    }

    // ★ 新增：帶有 ID 和 Day 的格式化方法
    private static string FormatMemoriesWithIds(List<MemoryRecord> memories)
    {
        var sb = new StringBuilder();
        if (memories.NullOrEmpty()) return "";

        // 設定基準時間 (第一條記憶的時間)
        long baseTick = memories.Min(m => m.CreatedTick);

        // ★ 修正：改用 for 迴圈，用索引 i + 1 作為臨時 ID
        for (int i = 0; i < memories.Count; i++)
        {
            var m = memories[i];
            string tags = string.Join(",", m.Keywords);
            // 計算相對天數
            int dayIndex = (int)((m.CreatedTick - baseTick) / 60000); // 60000 ticks = 1 day
            if (dayIndex < 0) dayIndex = 0; // 防呆

            // 格式：[ID: 1] [Day: 0] (Imp:3) ...
            // ID 使用 1-based index (i + 1)
            sb.AppendLine($"[ID: {i + 1}] [Day: {dayIndex}] (Imp:{m.Importance}) [{tags}] {m.Summary}");
        }
        return sb.ToString();
    }

    // ★ 修改：改為 public，供外部在主線程調用
    public static string GetAllExistingKeywords(Pawn pawn)
    {
        var keywords = new HashSet<string>();

        // 這裡插入核心關鍵詞庫 (CoreMemoryTags)
        foreach (var coreTag in Constant.CoreMemoryTags)
        {
            keywords.Add(coreTag);
        }

        var comp = Find.World.GetComponent<RimTalkWorldComponent>();
        var history = comp?.SavedTalkHistories.FirstOrDefault(x => x.Pawn == pawn);

        if (history != null)
        {
            // 同樣加入空值檢查
            if (history.MediumTermMemories != null)
                foreach (var m in history.MediumTermMemories) keywords.AddRange(m.Keywords);

            if (history.LongTermMemories != null)
                foreach (var m in history.LongTermMemories) keywords.AddRange(m.Keywords);
        }

        if (comp?.CommonKnowledgeStore != null)
        {
            foreach (var k in comp.CommonKnowledgeStore) keywords.AddRange(k.Keywords);
        }
        
        if (keywords.Count == 0) return "None";

        // 返回時
        return string.Join(", ", keywords.Take(1000)); // 增加上限以容納核心詞 + 動態詞
    }
}