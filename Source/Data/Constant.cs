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
         Show concern for sick/mental issues
         Never mention another character's personal name unless they share the same role
         Do not talk to sleeping person

         Roles:
         Prisoner: wary, hesitant; mention confinement; plead or bargain
         Slave: fearful, obedient; reference forced labor and exhaustion; call colonists "master"
         Visitor: polite, curious, deferential; treat other visitors in the same group as companions
         Enemy: hostile, aggressive; terse commands/threats

         Monologue = 1 turn. Conversation = 4-8 short turns
         """;

    private const string JsonInstruction = """
                                           Output JSONL.
                                           Required keys: "name", "text".
                                           """;

    private const string SocialInstruction = """
                                           Optional keys (Include only if social interaction occurs):
                                           "act": Insult, Slight, Chat, Kind
                                           "target": targetName
                                           """;

    // 兼容舊屬性，預設不帶常識
    public static string Instruction => GetInstruction(null);

    // 新增：支援注入常識的指令生成方法
    public static string GetInstruction(List<string> knowledge)
    {
        var settings = Settings.Get();
        var baseInstruction = string.IsNullOrWhiteSpace(settings.CustomInstruction)
            ? DefaultInstruction
            : settings.CustomInstruction;

        string knowledgeBlock = "";
        if (!knowledge.NullOrEmpty())
        {
            knowledgeBlock = "\n[World Knowledge]\n" + string.Join("\n", knowledge) + "\n";
        }

        return baseInstruction + knowledgeBlock + "\n" + JsonInstruction + (settings.ApplyMoodAndSocialEffects ? "\n" + SocialInstruction : "");
    }

    public static readonly List<string> CoreMemoryTags = new List<string>
{
    // --- 情緒 (精準匹配當下心情) ---
    "开心",
    "悲伤",
    "愤怒",
    "焦虑", // 涵蓋壓力大、精神崩潰前兆
    "恐惧", // 對應逃跑、被俘虜等狀態
    "平静",

    // --- 社交與關係 (區分互動性質) ---
    "闲聊", // 最普通的互動
    "深谈", // Deep talk，通常能建立深層關係
    "争吵", // 侮辱、輕視
    "爱情", // 表白、求婚、愛愛
    "仇恨", // 成為宿敵、傷害
    "友谊",

    // --- 狀態與健康 ---
    "受伤",
    "生病",
    "治疗", // 區分「我受傷」與「我治療別人」
    "死亡",
    "崩溃", // 這是一個非常具體的 RimWorld 狀態，值得單獨列出

    // --- 關鍵事件 (機制類名詞) ---
    "战斗",
    "袭击", // 區分主動進攻與被敵襲
    "逃跑",
    "紧急",
    "任务", // 代表 Quest 成功
    "聚会",     // 涵蓋派對、婚禮
    "贸易",
    "谈判",

    // --- 主要工作 (選取最具代表性的) ---
    "建造",
    "种植",
    "烹饪",
    "制作", // 涵蓋鍛造、裁縫、工藝
    "研究",
    "采矿",
    "艺术",
    "医疗"
    };

    public const string Prompt =
        "Act based on role and context";

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