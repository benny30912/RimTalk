using System.Collections.Generic;
using System.Linq;
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
                                                   Output JSONL. Keys: "name", "text".
                                                   [FINAL OBJECT - REQUIRED]
                                                   End with ONE metadata object (NO name/text):
                                                   {{"summary": "...", "keywords": ["..."], "importance": N}}
                                                   [SUMMARY RULES - 简体中文]
                                                   - 第三人称生动概括对话，记录独有细节（绰号、玩笑、承诺、语气）
                                                   - 转述而非复制原话（如"Ray嘲笑Benny是胆小鬼"而非引用原句）
                                                   - 但若 'importance' 达 4 或 5 级，请保留『闪光灯式』的具体强烈情绪细节。
                                                   - 捕捉情感氛围，不仅是事实
                                                   - 禁止相对时间（"昨天"等），允许模糊跨度（"近期"）或绝对时间（"5501年"）
                                                   [KEYWORDS RULES]
                                                   Select 3-5 from: [context words] OR [reference tags: {0}]
                                                   - Anchor (必选): 1-2 实体名词（人名/物品/地名）从context提取
                                                   - Link (必选): 1-2 概念/动作，优先从tags选
                                                   - Optional: 1 情感/状态
                                                   禁止: 创造新词, 包含[{1}]
                                                   [IMPORTANCE SCALE - Strict]
                                                   1=琐碎(闲聊/天气) | 2=普通(工作/轻微不适) | 3=值得记住(友谊/争吵/轻伤)
                                                   4=重大(崩溃/战斗/恋爱/重伤) | 5=刻骨铭心(死亡/结婚/残疾)
                                                   日常对话通常≤2，仅危及生命或改变关系的事件≥4
                                                   """;

    private const string SocialInstruction = """
                                           Optional keys (Include only if social interaction occurs):
                                           "act": Insult, Slight, Chat, Kind
                                           "target": targetName
                                           """;

    // [New] 支援注入常識的指令生成方法
    // [NEW] 新增參數：existingKeywords（現有關鍵詞列表字串）、initiator（發話者名稱列表）
    public static string GetInstruction(List<string> knowledge, string existingKeywords = null, List<string> initiator = null)
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

        // [NEW] 處理關鍵詞字串
        string keywordsList = string.IsNullOrEmpty(existingKeywords) ? "(none)" : existingKeywords;

        string initiatorList = initiator.NullOrEmpty()
        ? ""
        : string.Join(", ", initiator.Distinct());

        // 使用 String.Format 替換佔位符
        // 注意：因為 JSON 中使用了 {{}} 來轉義大括號
        string JsonInstruction = string.Format(JsonInstructionTemplate, keywordsList, initiatorList);

        return baseInstruction + knowledgeBlock + "\n" + JsonInstruction + (settings.ApplyMoodAndSocialEffects ? "\n" + SocialInstruction : "");
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
