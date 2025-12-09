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

    public static readonly HashSet<string> CoreMemoryTags = new HashSet<string>
    {
    // --- 情緒 (對應 MentalState & Mood) ---
    "开心",
    "悲伤",
    "愤怒",
    "焦虑", // 對應：壓力大、戒斷反應、生病前兆
    "恐惧", // 對應：逃跑、被俘、精神崩潰(Panic)
    "平静",
    "孤独",
    "绝望", // 對應：極低心情、崩潰邊緣
    "无助", // 對應：倒地(Downed)、無法行動
    "厌恶", // [新增] 對應：看到屍體、醜陋環境、吃生食

    // --- 社交 (對應 InteractionDef) ---
    "闲聊",
    "深谈",
    "争吵", // 對應：侮辱、社交鬥毆(SocialFight)
    "爱情", // 對應：求愛、Lovin'、結婚
    "仇恨", // 對應：宿敵、傷害行為
    "友谊",
    "劝说", // [新增] 對應：招募囚犯、傳教(Conversion)、安撫

    // --- 生理與健康 (對應 Health & Needs) ---
    "受伤", // 對應：有 Hediff (Wound)
    "生病", // 對應：有 Hediff (Flu, Plague...)
    "治疗", // 對應：Job (TendPatient)
    "死亡", // 對應：看到屍體、親友去世
    "饥饿", // [新增] 對應：Food level low
    "疲劳", // [新增] 對應：Rest level low
    "成瘾", // [新增] 對應：Addiction / Withdrawal

    // --- 關鍵事件與狀態 (對應 Game Mechanics) ---
    "战斗", // 對應：Drafted, Job (Attack)
    "袭击", // 對應：Map condition (Raid)
    "逃跑", // 對應：Job (Flee)
    "崩溃", // 對應：MentalState (任何 Break)
    "囚犯", // [新增] 對應：IsPrisoner
    "仪式", // [新增] 對應：LordJob (Ritual) - 婚禮/葬禮/演講
    "聚会", // 對應：Party
    "工作", // [新增] 對應：通用工作狀態 (如果需要觸發「抱怨工作累」)
    "火灾", // [新增] 對應：Map condition (Fire)，非常高頻的驚慌源

    // --- 抽象概念 (長期記憶用) ---
    "家园", "生存", "自由", "复仇", "信念", "艺术"
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