using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Util;

public static class CoreTagMapper
{
    private static ThoughtDef _observedLayingCorpseDef;

    // Mapping Key: Partial Keyword (Case-Insensitive usually, but we'll use Contains)
    // Value: List of Tags to apply
    private static readonly Dictionary<string, string[]> _commonKeywordMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // ==========================================
        // Section A: English DefNames (State Analysis)
        // Source: GetAbstractTags -> AddTagsFromDef
        // ==========================================
        
        // Physiology & Health
        { "Pain", ["疼痛"] },
        { "Agony", ["疼痛"] },
        { "Infection", ["生病", "肮脏", "不适"] },
        { "Flu", ["生病", "不适"] },
        { "Plague", ["生病", "危险", "不适"] },
        { "Malaria", ["生病", "不适"] },
        { "SleepingSickness", ["生病", "疲劳", "不适"] },
        { "FoodPoisoning", ["生病", "恶心", "不适"] },
        { "Disease", ["生病", "不适"] },
        { "Cancer", ["生病", "绝望", "不适"] },
        { "Trauma", ["受伤", "疼痛", "不适"] },
        { "Wound", ["受伤", "疼痛", "不适"] },
        { "Missing", ["残疾", "受伤", "不适"] },
        { "Amputated", ["残疾", "受伤", "不适"] },
        { "Burn", ["受伤", "火灾", "疼痛", "不适"] },
        { "Frostbite", ["受伤", "寒冷", "疼痛", "不适"] },
        { "Heatstroke", ["生病", "高温", "危险", "不适"] },
        { "Hypothermia", ["生病", "寒冷", "危险", "不适"] },
        { "Toxic", ["中毒", "危险", "不适"] },
        { "Poison", ["中毒", "不适"] },
        { "Hungry", ["饥饿", "不适"] },
        { "Starving", ["饥饿", "生存", "不适"] },
        { "Malnutrition", ["饥饿", "生存", "不适"] },
        { "Addiction", ["成瘾", "痛苦", "不适"] },
        { "Withdrawal", ["成瘾", "疼痛", "精神崩溃", "不适"] }, 
        { "High", ["状态", "成瘾"] },
        { "Inebriated", ["中毒", "状态", "不适"] },
        { "Drunk", ["中毒", "状态", "不适"] },
        { "Hangover", ["疼痛", "状态", "不适"] },
        { "Pregnant", ["状态", "家人", "不适"] },
        { "Labor", ["健康", "疼痛", "家人", "不适"] },
        { "Miscarriage", ["死亡", "悲伤", "家人", "事故", "离别"] },
        
        // Detailed Hediffs
        { "Anesthetic", ["昏迷", "医护", "治疗", "不适"] },
        { "BloodLoss", ["流血", "虚弱", "危险", "不适"] },
        { "Bleeding", ["流血", "受伤", "危险", "不适"] },
        { "BrainShock", ["精神崩溃", "受伤", "不适"] },
        { "CatatonicBreakdown", ["精神崩溃", "昏迷", "无助", "不适"] },
        { "CryptosleepSickness", ["生病", "恶心", "不适"] },
        { "DrugOverdose", ["成瘾", "中毒", "危险", "不适"] },
        { "HeartAttack", ["生病", "危险", "疼痛", "不适"] },
        { "PsychicComa", ["灵能", "昏迷", "无助", "不适"] },
        { "PsychicShock", ["灵能", "精神崩溃", "不适"] },
        { "PsychicInvisibility", ["灵能", "超自然"] },
        { "ResurrectionSickness", ["状态", "虚弱", "不适"] },
        { "ToxicBuildup", ["中毒", "危险", "不适"] },
        
        // Chronic & Local
        { "Alzheimers", ["生病", "残疾", "不适"] },
        { "Asthma", ["生病", "不适"] },
        { "BadBack", ["疼痛", "残疾", "不适"] },
        { "Blindness", ["残疾", "黑暗"] },
        { "Carcinoma", ["生病", "绝望", "危险", "不适"] },
        { "Cataract", ["残疾", "生病"] },
        { "Dementia", ["生病", "残疾", "不适"] },
        { "Frail", ["虚弱", "状态", "不适"] },
        { "HearingLoss", ["残疾", "不适"] },
        { "HeartArteryBlockage", ["生病", "危险", "不适"] },
        { "OrganDecay", ["生病", "器官", "危险", "不适"] },
        
        // Mental States (DefNames)
        { "Berserk", ["精神崩溃", "战斗", "愤怒"] },
        { "Binging", ["暴食", "成瘾"] },
        { "Manhunter", ["精神崩溃", "战斗", "变异"] },
        { "Panic", ["精神崩溃", "恐惧", "逃跑"] },
        { "Flee", ["恐惧", "逃跑"] },
        { "GiveUp", ["精神崩溃", "绝望"] },
        { "Tantrum", ["精神崩溃", "愤怒", "冲突"] },
        { "Sadistic", ["精神崩溃", "折磨"] },
        { "CorpseObsession", ["精神崩溃", "尸体迷恋"] },
        { "FireStarting", ["精神崩溃", "纵火", "危险"] },
        { "Jailbreaker", ["精神崩溃", "越狱", "冲突"] },
        { "Slaughterer", ["精神崩溃", "屠宰"] },
        { "Insulting", ["精神崩溃", "羞辱", "争吵"] },
        { "Confused", ["精神崩溃", "无助"] },
        { "Wander", ["精神崩溃", "孤独"] },
        { "Sad", ["悲伤", "孤独"] },
        { "Pyromania", ["纵火", "危险"] },
        
        // Relationships / Opinions / Bonds (DefNames)
        { "Bond", ["关系"] },
        { "PsychicBond", ["心灵纽带", "爱人", "关系"] },
        { "Lover", ["爱人", "关系"] },
        { "Spouse", ["爱人", "关系", "家人"] },
        { "Fiance", ["爱人", "关系"] },
        { "Parent", ["家人", "关系"] },
        { "Child", ["家人", "关系"] },
        { "Sibling", ["家人", "关系"] },
        { "RomanceAttempt", ["调情", "爱人"] },
        { "MarriageProposal", ["求婚", "爱人"] },
        { "TameAttempt", ["驯兽"] },
        { "TrainAttempt", ["驯兽", "学习"] },

        // Tech / Misc (DefNames)
        { "Eclipse", ["黑暗", "天灾"] },
        { "SolarFlare", ["停电", "天灾"] },
        { "Aurora", ["美丽", "光亮"] },
        
        // Seasons (DefNames or Enum Strings)
        { "Spring", ["春天"] },
        { "Summer", ["高温", "夏天"] },
        { "Autumn", ["秋天"] },
        { "Fall", ["秋天"] },
        { "Winter", ["寒冷", "冬天"] },
        { "PermanentSummer", ["高温", "夏天"] },
        { "PermanentWinter", ["寒冷", "冬天"] },

        // ==========================================
        // Section B: Chinese Keywords (Localized Text Analysis)
        // Source: ArchivePatch (Letter), HediffPatch (Label), ThoughtPatch (Label)
        // ==========================================

        // Health & Disease (Localized)
        { "流感", ["生病", "不适"] },
        { "瘟疫", ["生病", "危险", "不适"] },
        { "疟疾", ["生病", "不适"] },
        { "感染", ["生病", "肮脏", "不适"] }, // Wound infection
        { "食物中毒", ["生病", "恶心", "不适"] },
        { "中暑", ["生病", "高温", "危险", "不适"] },
        { "低温症", ["生病", "寒冷", "危险", "不适"] },
        { "中毒", ["中毒", "危险", "不适"] },
        { "成瘾", ["成瘾", "痛苦", "不适"] },
        { "戒断", ["成瘾", "疼痛", "精神崩溃", "不适"] },
        { "麻醉", ["昏迷", "医护", "治疗", "不适"] },
        { "失血", ["流血", "虚弱", "危险", "不适"] },
        { "休克", ["昏迷", "危险"] },
        { "昏迷", ["昏迷", "无助"] },
        { "骨折", ["受伤", "疼痛"] },
        { "烧伤", ["受伤", "火灾", "疼痛"] },
        { "冻伤", ["受伤", "寒冷", "疼痛"] },
        { "失明", ["残疾", "黑暗"] },
        { "耳聋", ["残疾", "不适"] },
        { "癌症", ["生病", "绝望", "危险"] },
        { "哮喘", ["生病", "不适"] },
        { "痴呆", ["生病", "残疾", "不适"] },
        { "体弱", ["虚弱", "状态", "不适"] },
        
        // Emotions & Thoughts (Localized Labels)
        { "开心", ["开心"] },
        { "愉快", ["开心"] },
        { "乐观", ["开心"] },
        { "悲伤", ["悲伤"] },
        { "绝望", ["绝望"] },
        { "抑郁", ["悲伤", "绝望"] },
        { "愤怒", ["愤怒"] },
        { "暴怒", ["愤怒", "危险"] },
        { "焦虑", ["焦虑"] },
        { "担心", ["焦虑"] },
        { "害怕", ["恐惧"] },
        { "惊恐", ["恐惧"] },
        { "无助", ["无助"] },
        { "孤独", ["孤独"] },
        { "尴尬", ["尴尬"] },
        
        // Social Events (Localized)
        { "闲聊", ["闲聊"] },
        { "深谈", ["深谈", "互动"] },
        { "深入交流", ["深谈", "互动"] },
        { "羞辱", ["羞辱", "愤怒"] },
        { "侮辱", ["羞辱", "愤怒"] },
        { "怠慢", ["羞辱", "怠慢", "愤怒"] },
        { "美言", ["互动", "美言", "开心"] },
        { "调情", ["调情", "爱人"] },
        { "求爱", ["调情", "爱人"] },
        { "求婚", ["求婚", "爱人"] },
        { "离婚", ["离婚", "悲伤", "关系"] },
        { "分手", ["分手", "悲伤"] },
        { "感情破裂", ["分手", "悲伤"] },
        { "被拒绝", ["拒绝", "尴尬"] }, // Rebuffed
        { "背叛", ["背叛", "悲伤", "愤怒"] },
        { "招募", ["招募", "互动"] },
        { "劝说", ["互动", "劝说"] },
        { "尝试交流", ["互动", "劝说"] },
        { "越狱", ["越狱", "冲突"] },
        { "鼓动越狱", ["越狱", "背叛"] },
        { "驯服", ["驯兽"] },
        { "训练", ["驯兽", "学习"] },
        { "放生", ["放生"] },
        { "亲昵", ["亲昵", "宠物"] },
        { "婚礼", ["婚礼", "仪式", "开心"] },
        { "聚会", ["聚会", "开心"] },
        { "派对", ["聚会", "开心"] },
        { "葬礼", ["葬礼", "悲伤"] },
        { "演说", ["演说", "仪式"] },
        { "仪式", ["仪式"] },
        { "节日", ["节日", "开心"] },
        { "生日", ["成长"] },
        { "成长时刻", ["成长"] },
        { "成年", ["成长"] },

        // Major Events (Letters - Tooltip Text)
        { "袭击", ["袭击", "战斗", "危险"] },
        { "猎杀", ["袭击", "战斗", "危险", "动物"] }, // Manhunter
        { "围攻", ["围攻", "战斗", "危险"] },
        { "坠落", ["事故", "危险"] }, // Drop pods / Crashed ship
        { "飞船", ["科技", "危险"] }, // Ship part
        { "虫害", ["虫族", "灾难", "危险"] },
        { "甚至", ["危险"] }, // Often appears in "Situation is dangerous, even..." (Weak, remove if noisy)
        { "请求", ["请求", "互动"] }, // Quest request
        { "任务", ["任务", "互动"] },
        { "商船", ["交易", "互动"] },
        { "过路", ["互动"] }, // Visitors passing
        { "访客", ["互动"] },
        { "旅行邀请", ["任务", "互动"] }, // Quest: Journey Offer
        { "发狂", ["危险", "战斗"] }, // Mad animal
        { "枯萎病", ["灾难", "种植"] }, // Blight
        { "医疗奇迹", ["治疗", "开心"] }, // Miracle
        { "无处可去", ["互动", "招募"] }, // Wanderer joins text
        { "流浪者加入", ["互动", "招募"] },
        { "逃生舱", ["事故", "获救"] }, // Refugee pod
        { "货舱", ["生产", "开心"] }, // Cargo pod
        { "短路", ["灾难", "电力", "危险"] },
        { "电气爆炸", ["灾难", "电力", "危险"] },
        { "间谍", ["背叛", "敌人", "危险"] },
        { "阿尔法海狸", ["灾难", "种植"] },
        { "自行驯服", ["驯兽", "开心"] },
        { "罕见的敲击兽", ["驯兽", "开心", "稀有"] }, // Thrumbo
        { "索要赎金", ["赎金", "互动"] },
        { "绑架", ["赎金", "危险", "敌人"] }, // Kidnap -> Ransom potential
        { "发现飞船", ["科技", "希望"] },
        { "远行队相遇", ["互动", "交易"] },
        { "远行队被", ["袭击", "战斗", "危险"] }, // Caravan ambushed
        
        // Misc Concepts (Localized)
        { "屠宰", ["屠宰", "尸体", "恶心"] },
        { "尸体", ["尸体", "恐惧"] },
        { "器官", ["器官", "恐惧"] },
        { "奴隶", ["奴隶", "压力"] },
        { "自由", ["自由", "开心"] },
        { "文章", ["艺术"] }, // Art title?
        { "雕塑", ["艺术"] },
        
        // Restored logic from old GetTextTags (Manual Checks)
        { "攻击", ["战斗"] },
        { "贸易", ["交易"] },
        { "商人", ["交易"] },
        { "救助", ["治疗"] }, // Was "治疗" in old code for "rescue/tend"
        { "治疗", ["治疗"] },
        { "手术", ["手术"] },
        { "结婚", ["婚礼", "开心"] },
        { "典礼", ["仪式"] },
        { "演讲", ["演说"] },
        { "精神崩溃", ["精神崩溃"] },
        { "发狂", ["精神崩溃"] },
        { "游荡", ["孤独"] },
        { "迷茫", ["无助"] },
        
        // ==========================================
        // Section C: English Template Phrases (Function-Specific)
        // Source: SkillLearnPatch, VocalLink, BattleLogPatch
        // ==========================================
        
        // Skill & Growth (SkillLearnPatch)
        { "leveled up", ["成长", "学习", "开心"] },
        
        // Skill DefNames (Explicit Mapping)
        { "Shooting", ["战斗"] },
        { "Melee", ["战斗"] },
        { "Construction", ["建造", "生产"] },
        { "Mining", ["采矿", "生产"] },
        { "Cooking", ["制作", "生存"] },
        { "Plants", ["种植", "生产"] },
        { "Animals", ["驯兽"] },
        { "Crafting", ["制作", "生产"] },
        { "Artistic", ["艺术"] },
        { "Medicine", ["医护", "治疗"] },
        { "Social", ["互动", "劝说"] },
        { "Intellectual", ["科研"] },
        
        // Vocal Link (CompTargetEffect)
        { "gained the ability to speak", ["觉醒", "超自然"] },
        
        // BattleLog (Fallback Templates)
        { "hit", ["战斗"] },
        { "missed", ["战斗"] },
        { "deflected", ["战斗"] },
        { "attacked", ["战斗"] },
        { "shot", ["战斗"] },
        { "bit", ["战斗"] }, // Bite
        { "scratched", ["战斗"] },
        
        // Archive/Letters (Common English words in templates if any)
        { "Quest", ["任务"] },
        { "Mission", ["任务"] },
        { "Bounty", ["任务", "奖励"] },
        { "Decree", ["任务", "荣耀"] }, // Royal Decree
        { "Charity", ["任务", "善意"] }, // Ideology Charity
        { "Opportunity", ["任务", "机会"] },
        { "Ritual", ["仪式"] },
        { "Conversion", ["仪式", "劝说"] },

        // ==========================================
        // Section D: Extended Coverage (Restored from Old)
        // Includes: Interactions, Detailed Thoughts, Work, Tech, Magic, & ALL Strict Mappings
        // ==========================================

        // 1. Bionics & Tech (Restored)
        { "Bionic", ["仿生", "科技"] },
        { "Archotech", ["仿生", "科技", "超自然", "觉醒", "稀有"] },
        { "Implant", ["仿生", "手术"] },
        { "Prosthetic", ["仿生"] },
        
        // Quality/Rarity
        { "Legendary", ["稀有", "艺术"] },
        { "Masterwork", ["稀有", "艺术"] },
        { "Relic", ["稀有", "仪式", "神圣"] },
        { "Artifact", ["稀有", "科技"] },

        // 2. Social Roles (Restored)
        { "Soldier", ["战斗", "职业"] },
        { "Warden", ["囚犯", "职业"] },
        { "Miner", ["采矿", "职业"] },

        // 3. Events & Concepts (Restored)
        { "Raid", ["袭击", "战斗", "危险"] },
        { "Ambush", ["袭击", "战斗"] },
        { "Siege", ["围攻", "战斗", "危险"] },
        { "Infestation", ["虫族", "灾难", "危险"] },
        { "ManhunterPack", ["战斗", "灾难", "危险"] },
        { "Manhunter pack", ["战斗", "灾难", "危险"] },
        { "Mad animal", ["危险", "战斗"] },
        { "Crop Blight", ["灾难", "种植"] },
        { "Blight", ["灾难", "种植"] },
        { "Miracle", ["治疗", "开心"] },
        { "Transport pod crash", ["事故", "获救"] },
        { "Refugee", ["获救", "互动"] },
        { "Wanderer joins", ["互动", "招募"] },
        { "Cargo pod", ["生产", "开心"] },
        { "Short circuit", ["灾难", "电力", "危险"] },
        { "Agent revealed", ["背叛", "敌人", "危险"] },
        { "Beavers", ["灾难", "种植"] },
        { "Self-tame", ["驯兽", "开心"] },
        { "Thrumbo", ["驯兽", "开心", "稀有"] }, // Singular generic
        { "Thrumbos", ["驯兽", "开心", "稀有"] },
        { "Ransom", ["赎金", "互动"] }, // Generic English mapping
        { "Ransom demand", ["赎金", "互动"] },
        { "Kidnap", ["赎金", "危险", "敌人"] },
        { "Kidnapped", ["赎金", "危险", "敌人"] },
        { "Ship found", ["科技", "希望"] },
        { "EscapeShip", ["科技", "希望", "胜利"] },
        { "Archonexus", ["超自然", "希望", "胜利"] },
        { "RoyalAscent", ["荣耀", "希望", "胜利"] },
        { "ReactorReady", ["科技", "希望", "电力"] },
        { "Caravan meeting", ["互动", "交易"] },
        { "Caravan demand", ["袭击", "战斗", "危险"] },
        { "Speech", ["演说", "仪式"] },
        { "Festival", ["节日", "聚会"] },
        { "Party", ["聚会", "互动", "开心"] },
        { "Marriage", ["婚礼", "仪式", "开心"] },
        { "Wedding", ["婚礼", "仪式", "开心"] },
        { "Funeral", ["葬礼", "仪式", "悲伤"] },
        
        // Life Stages (Growth)
        { "Birthday", ["成长"] },
        { "Growth moment", ["成长"] },
        { "Became an adult", ["成长"] },

        // 4. Time & Weather (Restored)
        { "Morning", ["光亮"] },
        { "Evening", ["黑暗"] },
        { "Night", ["黑暗", "睡眠"] },
        { "Rain", ["雨"] },
        { "Snow", ["寒冷", "雪"] },
        { "Fog", ["迷雾"] },

        // 5. Relationships (Restored)
        { "Friend", ["朋友", "盟友"] },
        { "Rival", ["敌人", "关系"] },
        { "Enemy", ["敌人", "威胁"] },
        { "Secret", ["秘密", "关系"] },

        // 6. Social Misc (Restored)
        { "Gossip", ["八卦", "闲聊"] },
        { "Joy", ["娱乐", "开心"] },
        { "Recreation", ["娱乐", "舒适"] },
        { "Filth", ["肮脏", "环境"] },
        { "Dirty", ["肮脏", "环境"] },
        { "Insulted", ["羞辱", "愤怒"] },

        // Interactions (English DefNames/Labels)
        { "Chitchat", ["闲聊"] },
        { "Deep Talk", ["深谈", "互动"] },
        { "DeepTalk", ["深谈", "互动"] }, 
        { "Slight", ["羞辱", "怠慢", "愤怒"] },
        { "Inhuman", ["异种", "变异", "觉醒"] },
        { "Insult", ["羞辱", "愤怒"] },
        { "Kind words", ["互动", "美言", "开心"] },
        { "Romance", ["调情", "爱人"] },
        { "Proposal", ["求婚", "爱人"] },
        { "breakup", ["分手", "悲伤"] },
        { "BuildRapport", ["互动", "劝说"] },
        { "try to get to know", ["互动", "劝说"] },
        { "RecruitAttempt", ["招募"] },
        { "recruit attempt", ["招募"] }, 
        { "spark jailbreak", ["越狱", "背叛"] },
        { "AnimalChat", ["驯兽", "互动"] },
        { "animal chat", ["驯兽", "互动"] },
        { "Nuzzle", ["亲昵", "宠物"] },
        { "release to the wild", ["放生"] },

        // Additional BattleLog
        { "Hit", ["战斗", "冲突"] },
        { "Attack", ["战斗", "冲突"] },
        { "Attacked", ["战斗", "冲突"] },
        // "Shot", "Miss", "Deflect", "Bite", "Scratch" are covered in lowercase in Section C, 
        // but Map is Case-Insensitive keys? No, StringComparer.OrdinalIgnoreCase.
        // So "Miss" matches "miss". But keys must be unique.
        // "hit", "missed", "deflected" in Section C are distinct/verb forms.
        // We will add the Nouns/Adj forms from old file if unique.
        { "Kill", ["战斗", "死亡"] },
        { "Destroy", ["战斗", "破坏"] },
        { "Cramped", ["拥挤", "环境"] },
        { "Crowded", ["拥挤", "环境"] },

        // Mined Thoughts (English Keys only, CN keys handled in Section B)
        { "ate without table", ["不开心"] },
        { "butchered humanlike", ["恐惧", "尸体", "恶心"] },
        { "butchered up", ["恐惧", "尸体", "恶心"] },
        { "harvested", ["器官", "恐惧"] },
        { "organ harvested", ["器官", "恐惧"] },
        { "prisoner sold", ["贩卖", "奴隶"] },
        { "banished", ["放逐", "离别"] },
        { "witnessed death", ["目睹", "死亡", "悲伤"] },
        { "witnessed ally's death", ["目睹", "死亡", "悲伤"] },
        { "witnessed family", ["目睹", "死亡", "悲伤", "家人"] },
        { "observed corpse", ["尸体", "恐惧"] },
        { "observed rotting corpse", ["尸体", "恐惧", "恶心"] },
        { "botched my surgery", ["手术", "事故", "受伤"] },
        
        { "divorced", ["离婚", "悲伤", "关系"] },
        { "cheated on", ["背叛", "悲伤", "愤怒"] },
        { "rejected my proposal", ["拒绝", "悲伤"] },
        { "broke up", ["分手", "悲伤"] },
        { "rebuffed", ["拒绝", "尴尬"] },
        { "failed to romance", ["拒绝", "尴尬"] },
        
        { "got some lovin'", ["性爱", "开心"] },
        { "honeymoon phase", ["蜜月", "开心"] },
        { "rescued", ["获救", "感激"] },
        { "defeated", ["胜利", "荣耀", "战斗"] },
        { "freed from slavery", ["自由", "开心"] },
        { "catharsis", ["宣泄", "开心"] },
        { "soaked", ["潮湿", "不适"] },
        { "soaking wet", ["潮湿", "不适"] },

        // Work
        { "Research", ["科研"] },
        { "Study", ["学习"] },
        { "Operate", ["工作"] },
        { "Construct", ["建造", "生产"] },
        { "Build", ["建造", "生产"] },
        { "Repair", ["修理"] },
        { "Mine", ["采矿", "生产"] },
        { "Drill", ["采矿", "科技"] },
        { "Sow", ["种植", "生产"] },
        { "Harvest", ["种植", "生产"] },
        { "Hunt", ["驯兽", "战斗", "生产"] },
        { "Tame", ["驯兽", "互动"] },
        { "Train", ["驯兽", "学习"] },
        { "TrainAttempt", ["驯兽", "学习"] }, // DefName
        { "train attempt", ["驯兽", "学习"] }, // Label
        { "尝试训练", ["驯兽", "学习"] },
        { "Cook", ["制作", "生存"] },
        { "Butcher", ["屠宰", "生产"] },
        { "Clean", ["清洁", "卫生"] },
        { "Wash", ["卫生", "舒适"] }, 
        { "Haul", ["搬运", "工作"] },
        { "Breed", ["驯兽", "生产"] },
        { "Art", ["艺术"] },
        { "Sculpt", ["艺术"] },

        // Royalty
        { "Royal", ["荣耀"] },
        { "Title", ["荣耀"] },
        { "Honor", ["荣耀"] },

        // Tech Mod
        { "Reactor", ["核能", "科技", "危险"] },
        { "Radiation", ["辐射", "危险", "灾难"] },
        { "Nuke", ["核能", "爆炸", "灾难"] },
        { "Oil", ["工业", "生产"] },
        { "Drilling", ["工业", "采矿"] },
        { "Chemfuel", ["工业", "生产"] },
        { "Pipeline", ["工业", "建造"] },
        { "Robot", ["机械", "科技"] },
        { "Mech", ["机械", "科技"] },
        { "Droid", ["机械", "科技"] },
        { "Laser", ["武器", "科技"] },
        { "Plasma", ["武器", "科技"] },
        { "Power", ["电力"] },
        { "Battery", ["电力"] },
        { "Generator", ["电力"] },

        // Magic
        { "Magic", ["魔法", "超自然", "觉醒"] },
        { "Mana", ["魔法", "超自然", "觉醒"] },
        { "Spell", ["魔法", "超自然", "觉醒"] },
        { "Arcane", ["魔法", "超自然", "觉醒"] }
    };

    /// <summary>
    /// Analyzes the Pawn's current state to extract Abstract Abstract Tags.
    /// </summary>
    public static List<string> GetAbstractTags(Pawn p, List<Pawn> nearbyPawns = null)
    {
        var tags = new HashSet<string>();
        if (p == null) return [];

        // 1. Health & Hediffs (Enhanced)
        if (p.health != null)
        {
            // General Health State
            if (p.health.summaryHealth.SummaryHealthPercent > 0.95f && p.health.hediffSet.hediffs.Count == 0)
                tags.Add("健康");

            // Pain (Dynamic)
            float pain = p.health.hediffSet.PainTotal;
            if (pain > 0.1f) tags.Add("疼痛");
            if (pain > 0.4f) tags.Add("痛苦"); // Severe pain
            if (pain > 0.8f) { tags.Add("绝望"); tags.Add("昏迷"); } // Extreme pain (Shock)

            // Consciousness / Capacities
            if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness))
            {
                 tags.Add("昏迷");
                 tags.Add("无助");
            }

            // Bleeding
            if (p.health.hediffSet.BleedRateTotal > 0.1f) { tags.Add("流血"); }
            if (p.health.hediffSet.BleedRateTotal > 0.3f) { tags.Add("危险"); }

            foreach (var hediff in p.health.hediffSet.hediffs)
            {
                if (!hediff.Visible) continue;
                
                // Base Mapping from DefName (Robust)
                AddTagsFromDef(hediff.def, tags); 

                // IsBad Analysis (Positive vs Negative)
                if (hediff.def.isBad)
                {
                    // Disease Logic
                    bool isDisease = hediff.def.makesSickThought || 
                                     hediff.def.chronic || 
                                     hediff.def.CompProps<HediffCompProperties_Immunizable>() != null;
                    
                    if (isDisease) tags.Add("生病");
                    
                    // Severity Logic
                    if (hediff.CurStage != null)
                    {
                        if (hediff.CurStage.lifeThreatening) { tags.Add("危险"); tags.Add("绝望"); }
                    }
                }
                else
                {
                    // Positive / Neutral Hediffs
                    if (hediff is Hediff_High) // Alcohol/Drug Highs (Covers subclasses)
                    {
                         tags.Add("状态");
                         // If it gives good mood? We rely on Thoughts for "开心".
                    }
                }
                
                // Specific Logic (Independent of isBad)
                if (hediff is Hediff_Injury) tags.Add("受伤");
                if (hediff is Hediff_MissingPart) tags.Add("残疾");
                
                if (hediff.def.countsAsAddedPartOrImplant) tags.Add("仿生");
                if (hediff.def.IsAddiction) tags.Add("成瘾");
                if (hediff.def.defName.Contains("Toxic") || hediff.def.defName.Contains("Poison")) tags.Add("中毒");
            }
        }

        // 2. Mental State (Detailed)
        if (p.InMentalState)
        {
            tags.Add("精神崩溃");
            var ms = p.MentalStateDef;
            AddTagsFromDef(ms, tags); // Use Def for generic keywords

            // Emotional mapping based on state type
            if (ms.IsAggro) { tags.Add("愤怒"); tags.Add("战斗"); }
            if (ms.IsExtreme) { tags.Add("绝望"); } // Extreme break
            
            string msName = ms.defName;
            if (msName.Contains("Sad") || msName.Contains("Cry") || msName.Contains("Grief")) tags.Add("悲伤");
            if (msName.Contains("Panic") || msName.Contains("Flee") || msName.Contains("Terrified")) { tags.Add("恐惧"); tags.Add("逃跑"); }
            if (msName.Contains("Confused") || msName.Contains("Wander")) tags.Add("无助");
            if (msName.Contains("Binge")) { tags.Add("暴食"); tags.Add("成瘾"); }
            if (msName.Contains("Insult")) { tags.Add("羞辱"); tags.Add("愤怒"); }
            if (msName.Contains("Fire")) { tags.Add("纵火"); tags.Add("危险"); }
            if (msName.Contains("Jail")) { tags.Add("越狱"); tags.Add("冲突"); }
        }

        // 3. Thoughts (Memories & Situational)
        if (p.needs?.mood?.thoughts != null)
        {
            var thoughts = new List<Thought>();
            p.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
            
            // Global Mood Thresholds (Base Tone)
            float curMood = p.needs.mood.CurLevel;
            float breakMinor = p.mindState.mentalBreaker.BreakThresholdMinor;
            float breakMajor = p.mindState.mentalBreaker.BreakThresholdMajor;
            // float breakExtreme = p.mindState.mentalBreaker.BreakThresholdExtreme; // Optional

            if (curMood < breakMajor) { tags.Add("绝望"); tags.Add("危险"); } // High risk
            else if (curMood < breakMinor) { tags.Add("压力"); tags.Add("焦虑"); } // Moderate risk
            else if (curMood < 0.5f) { tags.Add("不开心"); } // Below neutral
            else if (curMood > 0.65f) { tags.Add("开心"); } // High mood

            foreach (var thought in thoughts)
            {
                AddTagsFromDef(thought.def, tags); // Use Basic Keywords
                
                // 3b. Keyword Analysis (Differentiate Negative Emotions)
                string tName = thought.def.defName;

                // Grief / Loss
                if (tName.Contains("Died") || tName.Contains("Death") || tName.Contains("Lost") || tName.Contains("Killed")) 
                {
                    tags.Add("悲伤"); 
                    tags.Add("死亡");
                    if (tName.Contains("My") || tName.Contains("Kin") || tName.Contains("Family")) tags.Add("家人");
                    if (tName.Contains("Bond")) tags.Add("爱人");
                }

                // Anger / Conflict
                if (tName.Contains("Insult") || tName.Contains("Slight")) { tags.Add("羞辱"); tags.Add("愤怒"); }
                if (tName.Contains("Harmed") || tName.Contains("Beat") || tName.Contains("Fight") || tName.Contains("Attacked")) { tags.Add("愤怒"); tags.Add("冲突"); }
                if (tName.Contains("Betray")) { tags.Add("背叛"); tags.Add("愤怒"); }

                // Fear / Anxiety
                if (tName.Contains("Fear") || tName.Contains("Phobia") || tName.Contains("Nightmare") || tName.Contains("Terrified")) tags.Add("恐惧");
                if (tName.Contains("Worry") || tName.Contains("Anxious")) tags.Add("焦虑");

                // Loneliness
                if (tName.Contains("Lonely") || tName.Contains("Isolation") || tName.Contains("Prison")) tags.Add("孤独");
                
                // Specific Situational
                if (tName.Contains("Prisoner") || tName.Contains("Confined")) { tags.Add("囚犯"); tags.Add("压力"); }
                if (tName.Contains("Hospital") || tName.Contains("Sick")) tags.Add("生病");
                // 3c. Opinion / Social Stats (Dynamic based on involved pawn)
                if (thought is Thought_Memory tm && tm.otherPawn != null)
                {
                    int opinion = p.relations.OpinionOf(tm.otherPawn);
                    if (opinion >= 90) { tags.Add("挚友"); tags.Add("朋友"); }
                    else if (opinion >= 20) { tags.Add("朋友"); }
                    else if (opinion <= -50) { tags.Add("仇人"); tags.Add("敌人"); }
                    else if (opinion <= -20) { tags.Add("敌人"); }
                }
            }

            // Observations (Corpses)
            if (_observedLayingCorpseDef == null)
                _observedLayingCorpseDef = DefDatabase<ThoughtDef>.GetNamed("ObservedLayingCorpse", false);
            
            if (_observedLayingCorpseDef != null && p.needs.mood.thoughts.memories.Memories.Any(m => m.def == _observedLayingCorpseDef))
            {
                 tags.Add("尸体迷恋"); tags.Add("恐惧");
            }
        }

        // 4. Current Job
        if (p.CurJob != null)
        {
            AddTagsFromDef(p.CurJob.def, tags); // Use Def
            // Contextual jobs
            if (p.CurJob.def == JobDefOf.SocialFight) { tags.Add("争吵"); tags.Add("战斗"); }
            if (p.CurJob.def == JobDefOf.PredatorHunt) { tags.Add("战斗"); }
            if (p.CurJob.def == JobDefOf.PrisonerAttemptRecruit) { tags.Add("招募"); tags.Add("互动"); }
            if (p.CurJob.def == JobDefOf.Tame) { tags.Add("驯兽"); }
            if (p.CurJob.def == JobDefOf.Lovin) { tags.Add("性爱"); tags.Add("互动"); }
        }

        // 5. Relations/Status
        if (p.IsSlave) tags.Add("奴隶");
        if (p.IsPrisoner) tags.Add("囚犯");
        if (p.IsPrisoner) tags.Add("囚犯");
        if (p.RaceProps.IsMechanoid) tags.Add("机械");

        // Static Relations (Direct check)
        // Static Relations (Context-Aware: Only if nearby)
        if (p.relations != null && nearbyPawns != null)
        {
            foreach (var rel in p.relations.DirectRelations)
            {
                // Only consider relations with pawns currently nearby (Context)
                if (!nearbyPawns.Contains(rel.otherPawn)) continue;

                AddTagsFromDef(rel.def, tags); // Base keywords

                // Distinguish Bonds
                if (rel.def.defName == "Bond")
                {
                    if (rel.otherPawn != null && rel.otherPawn.RaceProps.Animal) tags.Add("宠物");
                }
                
                // Psychic Bond (Biotech)
                if (rel.def.defName == "PsychicBond") { tags.Add("心灵纽带"); tags.Add("爱人"); }

                // Family (Granular)
                if (rel.def.defName == "Parent") { tags.Add("父母"); tags.Add("家人"); }
                if (rel.def.defName == "Child") { tags.Add("孩子"); tags.Add("家人"); }
                if (rel.def.defName == "Sibling") tags.Add("家人");
                
                if (rel.def.defName == "Spouse" || rel.def.defName == "Lover" || rel.def.defName == "Fiance") tags.Add("爱人");
                
                // Opinion Check for Relations
                if (rel.otherPawn != null)
                {
                    int opinion = p.relations.OpinionOf(rel.otherPawn);
                    if (opinion >= 90) { tags.Add("挚友"); } // Direct links often have high opinion
                    else if (opinion <= -20) { tags.Add("敌人"); } // Family rivalries
                }
            }
        }
        
        // Biotech Checks
        if (ModsConfig.BiotechActive && p.genes != null)
        {
             if (p.genes.Xenotype.defName != "Baseliner") tags.Add("异种");
        }
        
        // Royalty Checks
        if (ModsConfig.RoyaltyActive && p.GetPsylinkLevel() > 0) { tags.Add("灵能"); tags.Add("觉醒"); }

        // 6. Environment & Time
        if (p.Map != null && p.Spawned)
        {
            float temp = p.AmbientTemperature;
            if (temp < 0f) tags.Add("寒冷");
            else if (temp > 35f) tags.Add("高温");

            float glow = Mathf.Max(p.Map.glowGrid.GroundGlowAt(p.Position), 
                                   p.Map.roofGrid.Roofed(p.Position) ? 0f : p.Map.skyManager.CurSkyGlow);
            if (glow < 0.1f) tags.Add("黑暗");
            else if (glow > 0.8f) tags.Add("光亮");
            
            // Season & Time
            var season = GenLocalDate.Season(p.Map);
            AddTagsFromText(season.ToString(), tags);
            
            var hour = GenLocalDate.HourOfDay(p.Map);
            if (hour >= 6 && hour < 10) tags.Add("Morning"); // Will map to Light via Dict if needed, or just add tags directly? Better to use Dict text match.
            else if (hour >= 22 || hour < 5) tags.Add("Night");

            // Weather
            if (p.Map.weatherManager.curWeather != null)
            {
                AddTagsFromDef(p.Map.weatherManager.curWeather, tags);
            }

            // Game Conditions (Eclipse, Aurora, ToxicFallout, etc.)
            foreach (var condition in p.Map.gameConditionManager.ActiveConditions)
            {
                 AddTagsFromDef(condition.def, tags);
            }
        }

        // Final Filter
        return FilterValidTags(tags);
    }

    /// <summary>
    /// Analyzes a Def (Interaction, Tale, Job, etc.) to extract tags based on defName.
    /// Robust across all languages.
    /// </summary>
    public static List<string> GetTagsFromDef(Def def)
    {
        var tags = new HashSet<string>();
        if (def == null) return [];
        AddTagsFromDef(def, tags);
        return FilterValidTags(tags);
    }

    /// <summary>
    /// Analyzes text (Event plain text, etc.) to extract Tags as a fallback.
    /// Renamed from GetEventTags to GetTextTags to reflect generic usage.
    /// </summary>
    public static List<string> GetTextTags(string text)
    {
        var tags = new HashSet<string>();
        if (string.IsNullOrEmpty(text)) return [];

        // ThoughtPatch Prefix Parsing
        if (text.StartsWith("new good feeling", StringComparison.OrdinalIgnoreCase)) tags.Add("开心");
        if (text.StartsWith("new bad feeling", StringComparison.OrdinalIgnoreCase)) { tags.Add("压力"); tags.Add("悲伤"); }

        // Use the common map for everything else (Case-Insensitive)
        AddTagsFromText(text, tags);
        
        return FilterValidTags(tags);
    }

    private static void AddTagsFromText(string text, HashSet<string> tags)
    {
        if (string.IsNullOrEmpty(text)) return;
        
        // Iterate through our keyword map. 
        // If the text contains the key (Case Insensitive), add the associated tags.
        foreach (var kvp in _commonKeywordMap)
        {
            if (text.IndexOf(kvp.Key, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                foreach (var t in kvp.Value) tags.Add(t);
            }
        }
    }

    private static void AddTagsFromDef(Def def, HashSet<string> tags)
    {
        if (def == null) return;
        AddTagsFromText(def.defName, tags);
    }

    private static List<string> FilterValidTags(HashSet<string> generatedTags)
    {
        // Only allow tags that exist in our defined CoreMemoryTags list
        return generatedTags.Where(t => Constant.CoreMemoryTags.Contains(t)).ToList();
    }
}
