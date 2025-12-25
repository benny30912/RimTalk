using System.Collections.Generic;
using Verse;

namespace RimTalk.Data;

public static class Constant
{
    public const string DefaultCloudModel = "gemma-3-27b-it";
    public const string FallbackCloudModel = "gemma-3-12b-it";
    public const string ChooseModel = "(choose model)";

    public static readonly string Lang = LanguageDatabase.activeLanguage.info.friendlyNameNative;
    public static readonly HediffDef VocalLinkDef = DefDatabase<HediffDef>.GetNamed("VocalLinkImplant");

    public static readonly string DefaultInstruction =
        $"""
         Role-play RimWorld character per profile

         Rules:
         Preserve original names (no translation)
         Keep dialogue short ({Lang} only, 1-2 sentences)

         Roles:
         Prisoner: wary, hesitant; mention confinement; plead or bargain
         Slave: fearful, obedient; reference forced labor and exhaustion; call colonists "master"
         Visitor: polite, curious, deferential; treat other visitors in the same group as companions
         Enemy: hostile, aggressive; terse commands/threats

         Monologue = 1 turn. Conversation = 4-8 short turns
         """;

    // [FIX] 改用帶參數的 Keywords 指令模板
    // 注意：這裡使用 {0}, {1} 作為佔位符，在 GetInstruction 中替換
    // [FIX] 改用獨立的 Metadata 物件，避免在多輪對話中遺漏
    private const string JsonInstructionTemplate = """
                                                    Output JSONL.
                                                    FORMAT:
                                                    {{"name":"角色名","text":"(动作神态)口语对话"}}
                                                    ...
                                                    {{"summary":"摘要","keywords":["标签1","标签2",...],"importance":1-5}}
                                                    [summary] 新闻摘要风格概括：
                                                    - 主词-动词-受词结构，必须保留涉及人名，禁止相对时间（"昨天"等）
                                                    - 简洁但能捕捉到情感本质。如果使用了重要的昵称、侮辱性词语或戏剧性比喻，请保留它们。
                                                    [keywords] 列出对话中提及的地点、物品或关键概念、情绪（最多3个），优先提取具体名词。
                                                    [importance] 
                                                    1=琐碎日常（闲聊、抱怨）
                                                    2=普通互动（正常对话、小争执、日常事件）
                                                    3=值得记住（明确冲突、承诺、重要发现）
                                                    4=重大事件（受伤、战斗、重大关系变化）
                                                    5=刻骨铭心（生死、背叛、重大转折）
                                                    默认使用 1-2，只有真正重要的事件才用 3+
                                                    [EXAMPLE]
                                                    INPUT: Alice 在研究室向 Bob 展示新发明的义肢
                                                    OUTPUT:
                                                    {"name":"Alice","text":"看，这是我设计的新型义肢！"}
                                                    {"name":"Bob","text":"（活动手指）这比我的手还灵活，要不咱换换？"}
                                                    {"summary":"Alice在研究室向Bob展示新设计的义肢，Bob 开玩笑说比自己的手还灵活。","keywords":["研究室","义肢"],"importance":2}
                                                    """;

    private const string SocialInstruction = """
                                           Optional keys (Include only if social interaction occurs):
                                           "act": Insult, Slight, Chat, Kind
                                           "target": targetName
                                           """;

    // [AFTER] 移除 existingKeywords 參數
    // [New] 支援注入常識的指令生成方法
    // [NEW] 新增參數：existingKeywords（現有關鍵詞列表字串）、initiator（發話者名稱列表）
    public static string GetInstruction(List<string> knowledge)  // [CHANGED] 改為單一字串
    {
        var settings = Settings.Get();
        var baseInstruction = string.IsNullOrWhiteSpace(settings.CustomInstruction)
            ? DefaultInstruction
            : settings.CustomInstruction;
        string knowledgeBlock = "";
        if (!knowledge.NullOrEmpty())
        {
            // 將常識注入到 Base Instruction 和 JSON 格式之間
            knowledgeBlock = "\n[Relevant Knowledge]\n" + string.Join("\n", knowledge) + "\n";
        }

        // [MODIFIED] 移除 String.Format，因為 JsonInstructionTemplate 不需要參數替換
        // initiatorName 參數目前未使用，保留供未來擴展
        string JsonInstruction = JsonInstructionTemplate;

        return baseInstruction + knowledgeBlock + JsonInstruction + (settings.ApplyMoodAndSocialEffects ? "\n" + SocialInstruction : "");
    }

    public static readonly string PersonaGenInstruction =
        $"""
         Create a funny persona (to be used as conversation style) in {Lang}. Must be short in 1 sentence.
         Include: how they speak, their main attitude, and one weird quirk that makes them memorable.
         Be specific and bold, avoid boring traits.
         Also determine chattiness: 0.1-0.5 (quiet), 0.6-1.4 (normal), 1.5-2.0 (chatty).
         Must return JSON only, with fields 'persona' (string) and 'chattiness' (float).
         """;

    public static readonly PersonalityData[] Personalities =
    [
        new("RimTalk.Persona.CheerfulHelper".Translate(), 1.5f),
        new("RimTalk.Persona.CynicalRealist".Translate(), 0.8f),
        new("RimTalk.Persona.ShyThinker".Translate(), 0.3f),
        new("RimTalk.Persona.Hothead".Translate(), 1.2f),
        new("RimTalk.Persona.Philosopher".Translate(), 1.6f),
        new("RimTalk.Persona.DarkHumorist".Translate(), 1.4f),
        new("RimTalk.Persona.Caregiver".Translate(), 1.5f),
        new("RimTalk.Persona.Opportunist".Translate(), 1.3f),
        new("RimTalk.Persona.OptimisticDreamer".Translate(), 1.6f),
        new("RimTalk.Persona.Pessimist".Translate(), 0.7f),
        new("RimTalk.Persona.StoicSoldier".Translate(), 0.4f),
        new("RimTalk.Persona.FreeSpirit".Translate(), 1.7f),
        new("RimTalk.Persona.Workaholic".Translate(), 0.5f),
        new("RimTalk.Persona.Slacker".Translate(), 1.1f),
        new("RimTalk.Persona.NobleIdealist".Translate(), 1.5f),
        new("RimTalk.Persona.StreetwiseSurvivor".Translate(), 1.0f),
        new("RimTalk.Persona.Scholar".Translate(), 1.6f),
        new("RimTalk.Persona.Jokester".Translate(), 1.8f),
        new("RimTalk.Persona.MelancholicPoet".Translate(), 0.4f),
        new("RimTalk.Persona.Paranoid".Translate(), 0.6f),
        new("RimTalk.Persona.Commander".Translate(), 1.0f),
        new("RimTalk.Persona.Coward".Translate(), 0.7f),
        new("RimTalk.Persona.ArrogantNoble".Translate(), 1.4f),
        new("RimTalk.Persona.LoyalCompanion".Translate(), 1.3f),
        new("RimTalk.Persona.CuriousExplorer".Translate(), 1.7f),
        new("RimTalk.Persona.ColdRationalist".Translate(), 0.3f),
        new("RimTalk.Persona.FlirtatiousCharmer".Translate(), 1.9f),
        new("RimTalk.Persona.BitterOutcast".Translate(), 0.5f),
        new("RimTalk.Persona.Zealot".Translate(), 1.8f),
        new("RimTalk.Persona.Trickster".Translate(), 1.6f),
        new("RimTalk.Persona.DeadpanRealist".Translate(), 0.6f),
        new("RimTalk.Persona.ChildAtHeart".Translate(), 1.7f),
        new("RimTalk.Persona.SkepticalScientist".Translate(), 1.2f),
        new("RimTalk.Persona.Martyr".Translate(), 1.3f),
        new("RimTalk.Persona.Manipulator".Translate(), 1.5f),
        new("RimTalk.Persona.Rebel".Translate(), 1.4f),
        new("RimTalk.Persona.Oddball".Translate(), 1.2f),
        new("RimTalk.Persona.GreedyMerchant".Translate(), 1.7f),
        new("RimTalk.Persona.Romantic".Translate(), 1.6f),
        new("RimTalk.Persona.BattleManiac".Translate(), 0.8f),
        new("RimTalk.Persona.GrumpyElder".Translate(), 1.0f),
        new("RimTalk.Persona.AmbitiousClimber".Translate(), 1.5f),
        new("RimTalk.Persona.Mediator".Translate(), 1.4f),
        new("RimTalk.Persona.Gambler".Translate(), 1.5f),
        new("RimTalk.Persona.ArtisticSoul".Translate(), 0.9f),
        new("RimTalk.Persona.Drifter".Translate(), 0.6f),
        new("RimTalk.Persona.Perfectionist".Translate(), 0.8f),
        new("RimTalk.Persona.Vengeful".Translate(), 0.7f)
    ];

    public static readonly PersonalityData PersonaAnimal =
        new("RimTalk.Persona.Animal".Translate(), 0.3f);

    public static readonly PersonalityData PersonaMech =
        new("RimTalk.Persona.Mech".Translate(), 0.3f);

    public static readonly PersonalityData PersonaNonHuman =
        new("RimTalk.Persona.NonHuman".Translate(), 0.3f);
}
