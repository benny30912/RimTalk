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

    public static readonly HashSet<string> CoreMemoryTags =
    [
        // ==========================================
        // 核心生理與狀態 (Status & Bio)
        // ==========================================
        "生病", "痛苦", "受伤", "死亡", "健康", "危险", "生存", "恢复",
        "昏迷", "瘫痪", "残疾", "疤痕", "流血", "感染", "虚弱", "疲劳",
        "饥饿", "口渴", "成瘾", "中毒", "怀孕", "繁殖", "手术", "治疗",
        "义肢", "仿生", "超凡", "强化", "基因", "进化", "变异", "异种",
        "睡眠", "饮食", "美食", "甜食", "药水", "魔法", "舒适", "肮脏",
        "卫生", "清洁", "排泄", "输血", "抽血", "胚芽", "胚胎", "受精",
        "育儿", "抚养", "营养膏",

        // ==========================================
        // 情緒與精神 (Mood & Mental)
        // ==========================================
        "开心", "悲伤", "愤怒", "恐惧", "焦虑", "绝望", "无助", "孤独",
        "疯狂", "幻觉", "灵能", "心灵", "平静", "爱欲", "厌恶", "折磨",
        "耻辱", "悔痛", "本能", "强迫", "压力", "希望", "信任", "迷恋",
        "胡言乱语", "迷茫", "怀疑", "惊慌", "失神", "冷漠", "安抚",
        "灵感", "狂热", "创作", "嗜血", "荣耀", "意志", "启灵",

        // ==========================================
        // 環境與災害 (Environment & Disaster)
        // ==========================================
        "火焰", "烧伤", "寒冷", "冻伤", "过热", "高温", "低温", "中暑",
        "电击", "酸蚀", "毒气", "辐射", "污染", "黑暗", "阳光", "真空",
        "水", "温暖", "光明", "狭窄", "压迫", "光", "雨", "风暴", "闪电",
        "雷暴", "飓风", "大风", "迷雾", "酸雾", "灰烬", "火山", "干旱",
        "缺水", "熔岩", "流星", "坠落", "耀斑", "停电", "日蚀", "极光",
        "烟雾", "末日", "毁灭", "轰炸", "战争", "时空", "震动", "地库",
        "血雨", "血月", "死灵", "尸体", "吟诵", "花朵", "衰老", "祝福",

        // ==========================================
        // 戰鬥與威脅 (Combat & Threats)
        // ==========================================
        "战斗", "攻击", "防御", "护盾", "装甲", "坚固", "脆弱", "爆炸",
        "刺伤", "割伤", "钝伤", "抓握", "吞食", "强敌", "精英", "处决",
        "抓捕", "释放", "绑架", "猎杀", "破坏", "炮击", "逃兵", "追捕",
        "悬赏", "伏击", "前哨", "据点", "海盗", "雇佣兵", "机械族",
        "机械集群", "虫族", "虫巢", "威胁", "围攻", "复仇", "冲突", "入侵",
        "战棺", "机甲", "射击", "格斗", "突击", "爆破",

        // ==========================================
        // 社交與關係 (Social & Relations)
        // ==========================================
        "父亲", "母亲", "兄弟", "姐妹", "祖父母", "孙子女", "家庭",
        "叔舅", "姑姨", "侄子", "侄女", "堂表亲", "继父", "继母", "继子",
        "丈夫", "妻子", "情人", "未婚妻", "前任", "岳父母", "女婿", "儿媳",
        "监管者", "创造者", "主人", "奴仆", "密友", "族群", "牵绊",
        "婚礼", "配偶", "求婚", "恋爱", "分手", "外遇", "私奔", "联姻",
        "外交", "和平", "谈判", "交易", "商贸", "礼物", "友善", "闲聊",
        "争吵", "羞辱", "八卦", "秘密", "玩笑", "性话题", "调情", "安慰",
        "鼓励", "背叛", "发泄", "指控", "辩护", "审判", "定罪", "劝说",
        "招待", "宾客", "难民", "乞讨", "施舍", "加入", "收留", "孤儿",
        "收养", "儿童", "团聚", "回归", "同情", "审讯", "奴役", "转化",

        // ==========================================
        // 職業與角色 (Roles & Professions)
        // ==========================================
        "猎人", "侠客", "女侠", "特工", "总督", "战士", "觉醒人形",
        "发明家", "将军", "狂战士", "弓手", "农民", "酋长", "商人", "掠夺者",
        "游击手", "矛手", "农奴", "掷火兵", "剑士", "旗兵", "弩手", "大剑士",
        "公会长", "长戟兵", "火铳手", "骑士", "领主", "民兵", "破墙手", "厨师",
        "主播", "执法者", "流浪者", "拾荒者", "黑衣人", "游骑兵", "工匠", "法师",
        "医师", "守卫", "统帅", "贵族", "亲王", "刺客", "信众", "教徒",

        // ==========================================
        // 派系與種族 (Factions & Races)
        // ==========================================
        "帝国", "公民", "部落", "部众", "外来者", "联盟", "王国", "氏族", "商会",
        "公会", "猪猡", "污骸", "毛绒", "尼人", "炎魔", "爬蜥", "化狼", "狼人",
        "血族", "吸血鬼", "超凡种", "机器人", "古代人", "失落", "打捞者",
        "矮人", "巨人", "约顿", "食人魔", "地精", "蛇人", "蝰蛇", "猫鹰",
        "异种人", "小人族", "妖", "百鬼", "恶魔", "天使", "亡灵", "僵尸",
        "史莱姆", "粘液", "虫族", "异虫", "异象", "实体", "霍拉克斯",

        // ==========================================
        // 生物與怪物 (Creatures & Monsters)
        // ==========================================
        "陆行鸟", "沙狮", "潜行者", "沙鱿", "角马", "电击羊", "闪现猎犬",
        "空鳗", "刺牛羊", "眼灵", "焦油", "畸变", "嬗变", "蛞蝓", "蚁蛳",
        "雷兽", "雷牛", "不眠之眼", "工厂机", "真菌兽", "风兽", "獠牙血鱿",
        "巨型甲虫", "巨型蜘蛛", "猛犸虫", "虫帝", "虫皇", "虫后", "幼虫",
        "步甲", "爆炸蜱", "盾背虫", "蝗虫", "毒螨", "地狱甲虫", "铁卫",
        "工蝇", "巨型蜈蚣", "蓟马", "黄蜂", "主父", "蠹鱼", "坦克螂", "主宰螳螂",
        "泰坦", "寒冰爬行虫", "霜蠓", "盲眼", "血肉兽", "指刺", "韧刺", "三刺",
        "肉博", "金属恐怖", "蹒跚者", "视界", "吞噬者", "夜行兽", "拟态",
        "巨蟹", "加拉特", "晶体", "螺旋", "猛兽", "开膛", "猎犬",

        // ==========================================
        // 聚會與活動 (Gatherings & Activities)
        // ==========================================
        "聚会", "派对", "音乐会", "狂欢", "生日", "庆祝",
        "雪人", "堆雪人", "葬礼", "缅怀", "悼念",
        "散步", "喝酒", "酒席", "进餐", "聚餐", "电影", "观影",
        "野营", "篝火", "观星", "苍穹", "赏艺", "鉴赏",
        "演讲", "仪式", "祭坛", "钓鱼", "酿造", "酿酒", "书写", "阅读",
        "油炸", "烘焙", "烧烤", "工作", "学习", "研究", "采矿", "种植",
        "驯兽", "畜牧", "建造", "修理", "拆除", "制作", "制造", "搬运",
        "灭火", "远行", "旅行", "探索", "远征", "搜刮", "骇入", "扫描",
        "授勋", "典礼", "抑制", "收容", "抽取", "充电"
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