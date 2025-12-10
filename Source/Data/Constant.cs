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
        // --- 核心状态 (Status) ---
        "生病", "痛苦", "受伤", "死亡", "健康", "危险", "生存", "恢复",
        "昏迷", "瘫痪", "残疾", "疤痕", "流血", "感染", "虚弱", "疲劳",

        // --- 情绪与精神 (Mood & Mental) ---
        "开心", "悲伤", "愤怒", "恐惧", "焦虑", "绝望", "无助", "孤独",
        "疯狂", "幻觉", "灵能", "心灵", "平静", "爱欲", "厌恶", "折磨",
        "耻辱", "悔痛", "本能", "强迫", "压力", "希望", "信任",
        "迷恋", "胡言乱语", "迷茫", "怀疑", "惊慌", "失神",

        // --- 元素与环境 (Elements) ---
        "火焰", "烧伤", "寒冷", "冻伤", "电击", "酸蚀", "毒气", "中毒",
        "辐射", "污染", "黑暗", "阳光", "真空", "水", "温暖", "光明",
        "狭窄", "压迫", "光",

        // --- 战斗与防御 (Combat) ---
        "战斗", "攻击", "防御", "护盾", "装甲", "坚固", "脆弱", "爆炸",
        "刺伤", "割伤", "钝伤", "抓握", "吞食", "强敌", "精英", "压迫",
        "处决", "抓捕", "释放", "绑架", "猎杀", "破坏", "炮击", "逃兵", "战争",

        // --- 身体与种族 (Body & Race) ---
        "义肢", "仿生", "超凡", "强化", "手术", "变异", "进化", "基因",
        "机器人", "高科技", "AI", "故障", "能量", "数据",
        "吸血", "血液", "虫族", "虫胶", "植物", "骸骨", "死灵", "灵魂",
        "巨人", "微小", "体型", "隐形", "飞行", "滑翔", "两栖", "蛇行", "触手",
        "怀孕", "繁殖", "幼体", "寄生", "共生", "死眠", "食尸鬼", "器官", "制造",

        // --- 生活与行为 (Life & Activities) ---
        "工作", "学习", "技能", "书籍", "智识", "艺术", "音乐",
        "饮食", "美食", "甜食", "饥饿", "成瘾", "醉酒", "药水", "魔法",
        "社交", "奴役", "控制", "叛逆", "监狱", "急救", "清洁", "卫生",
        "交易", "效率", "财富", "黄金", "玩耍", "娱乐", "穿戴", "睡眠",
        "喂食", "救援", "搬运", "修理", "拆除", "钻井", "伐木", "畜牧", "驯兽",
        "放纵", "越狱", "施虐", "屠夫", "闲逛", "盗窃", "反抗", "僵硬", "催眠",
        "阅读", "书写", "渔猎", "灭火", "审讯", "债务", "领养", "孤儿", "礼物", "支援",
        "排泄", "舒适", "美观", "无聊", "焦急", "肮脏", "口渴", "休眠",

        // --- 信仰与社会 (Ideology & Society) ---
        "信仰", "异端", "神圣", "奇迹", "仪式", "祭坛", "虚空", "诡异",
        "公司", "报告", "垃圾", "极乐", "仙树", "人性", "正义", "罪恶", "自由",
        "收容", "实体", "激活", "抽取", "调查", "演讲", "领袖", "责任",
        "和平", "外交", "谈判", "战败", "胜利", "荣耀", "慈善", "魔方", "预言", "灵质",
        "教化", "圣物", "搜寻", "怪异",

        // --- 社交互动细节 (Social Interaction) ---
        "八卦", "秘密", "玩笑", "性话题", "调情", "羞辱", "争辩", "分享",
        "安慰", "鼓励", "背叛", "发泄", "审判", "指控", "辩护", "定罪",
        "洗脑", "讲道", "尖叫", "非人", "混乱", "交流", "探望",
        "侮辱", "离开", "放弃", "浪漫", "匿名", "隐私", "堕落",

        // --- 关系 (Relationships) ---
        "父亲", "母亲", "兄弟", "姐妹", "祖父母", "孙子女",
        "叔舅", "姑姨", "侄子", "侄女", "堂表亲", "继父", "继母", "继子",
        "丈夫", "妻子", "情人", "未婚妻", "前任", "岳父母", "女婿", "儿媳",
        "监管者", "创造者", "主人", "奴仆", "密友", "族群", "牵绊",
        "婚礼", "家庭", "配偶",

        // --- 灵感 (Inspirations) ---
        "灵感", "狂热", "速度", "精准", "创作",
        "招募", "商贸", "安抚", "嗜血",
        "成长", "烹饪", "采集", "狩猎", "友善",
        "采矿", "种植", "研究", "旅行",

        // --- 天气与环境 (Weather & Environment) ---
"迷雾", "酸雾", "灰烬", "热浪", "寒流", "高温", "低温",
"干旱", "缺水", "熔岩", "飓风", "大风", "风暴", "闪电", "雷暴",
"日蚀", "极光", "黑暗", "停电", "耀斑", "烟雾", "流星",

// --- 灾难与异象 (Disaster & Anomaly) ---
"血雨", "血月", "死灵", "复活", "尸体", "吟诵", "诅咒",
"末日", "毁灭", "辐射", "污染", "躲避", "孢子", "花朵",
"轰炸", "战争", "时空", "衰老", "祝福", "奇迹", "机械",
"太空", "坠落", "地库",
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