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

        // ==========================================
        // Core Memory Tags (Atomic Concepts)
        // ==========================================
        public static readonly HashSet<string> CoreMemoryTags =
        [
            // A. 生理与生存 (Physiology & Survival)
            "健康", "受伤", "生病", "疼痛", "痛苦", "残疾", "治疗", "手术", "仿生",
            "生存", "饥饿", "暴食", "疲劳", "睡眠", "死亡", "流血", "昏迷", "虚弱",
            "状态", "成瘾", "中毒", "变异", "卫生", "压力", "危险",
            "不适", "恶心", "器官", "尸体",

            // B. 环境与感知 (Environment & Perception)
            "寒冷", "高温", "黑暗", "光亮",
            "美丽", "丑陋", "艺术",
            "肮脏", "清洁", "舒适", "拥挤", "潮湿",
            "灾难", "火灾", "爆炸", "辐射", "天灾", "停电",
            "雨", "雪", "迷雾", "春天", "夏天", "秋天", "冬天",

            // C. 社会与关系 (Social & Relationships)
            "互动", "闲聊", "深谈", "争吵", "羞辱", "怠慢", "美言", "调情", "性爱", "求婚", "离婚", "分手",
            "仪式", "婚礼", "聚会", "葬礼", "演说", "节日", "八卦", "劝说", "亲昵",
            "关系", "敌人", "仇人", "盟友", "家人", "父母", "孩子", "爱人", "朋友", "挚友", "囚犯", "奴隶",
            "背叛", "秘密", "荣耀", "宠物", "心灵纽带",
            "不开心", "贩卖", "放逐", "离别", "目睹", "事故",
            "尴尬", "蜜月", "获救", "感激", "胜利", "自由", "宣泄",

            // D. 威胁与战斗 (Threats & Combat)
            "冲突", "袭击", "战斗", "围攻", "防御", "越狱",
            "恐惧", "逃跑", "屠宰", "处决", "折磨",
            "精神崩溃", "纵火", "尸体迷恋", "孤独",
            "开心", "悲伤", "愤怒", "焦虑", "无助", "绝望", // Basic Emotions

            // E. 工作与行为 (Work & Achievement)
            "生产", "建造", "种植", "采矿", "制作", "工作", "职业", "成长", // Work basics
            "科研", "医护", "驯兽", "放生", "招募", "交易", "学习", "娱乐", // Activities
            "搬运", "清洁", "修理", "任务", "赎金", // Note: "清洁" appears in both Environment and Work, context determines usage 

            // F. 科技与超凡 (Tech & Supernatural)
            "科技", "机械", "核能", "工业", "电力", "武器", "觉醒",
            "超自然", "灵能", "异种", "虫族", "魔法", "稀有", "希望"
        ];

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