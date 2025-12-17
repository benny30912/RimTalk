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
    // Mapping Key: Partial Keyword (Case-Insensitive usually, but we'll use Contains)
    // Value: List of Tags to apply
    private static readonly Dictionary<string, string[]> _commonKeywordMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // ==========================================
        // Section A: English DefNames (State Analysis)
        // ==========================================
        
        // Physiology & Health
        { "Pain", ["疼痛"] },
        { "Agony", ["疼痛"] },
        { "Infection", ["生病", "肮脏"] },
        { "Flu", ["生病"] },
        { "Plague", ["生病"] },
        { "Malaria", ["生病"] },
        { "SleepingSickness", ["生病", "虚弱"] },
        { "FoodPoisoning", ["不适", "恶心", "生病"] },
        { "Disease", ["生病"] },
        { "Cancer", ["生病", "绝望"] },
        { "Trauma", ["受伤", "疼痛"] },
        { "Wound", ["受伤", "疼痛"] },
        { "Missing", ["残疾", "受伤"] },
        { "Amputated", ["残疾", "受伤"] },
        { "Burn", ["受伤", "火焰", "疼痛"] },
        { "Frostbite", ["受伤", "寒冷", "疼痛"] },
        { "Gunshot", ["受伤", "战斗", "疼痛"] },
        { "Cut", ["受伤", "疼痛"] },
        { "Stab", ["受伤", "战斗", "疼痛"] },
        { "Scratch", ["受伤", "疼痛"] },
        { "Bite", ["受伤", "战斗", "疼痛"] },
        { "Crush", ["受伤", "疼痛", "事故"] },
        { "Crack", ["受伤", "疼痛", "事故"] },
        { "Shredded", ["受伤", "疼痛", "战斗"] },
        { "Bruise", ["受伤", "疼痛"] },
        { "SurgicalCut", ["受伤", "手术", "疼痛"] },
        { "ExecutionCut", ["死亡", "攻击", "仪式"] },
        { "TraumaSavant", ["觉醒", "残疾", "严重"] },
        { "Heatstroke", ["高温", "不适"] },
        { "Hypothermia", ["生病", "寒冷"] },
        { "Toxic", ["中毒"] },
        { "Poison", ["中毒"] },
        { "Hungry", ["饥饿"] },
        { "Starving", ["饥饿", "生存"] },
        { "Malnutrition", ["饥饿", "生存", "不适"] },
        { "Addiction", ["成瘾"] },
        { "Withdrawal", ["成瘾", "疼痛", "精神崩溃"] },
        { "High", ["成瘾"] },
        { "Inebriated", ["中毒"] },
        { "Drunk", ["中毒"] },
        { "Hangover", ["疼痛", "不适"] },
        { "Pregnant", ["家人", "不适"] },
        { "Labor", ["疼痛", "家人", "不适"] },
        { "Miscarriage", ["死亡", "悲伤", "家人", "事故"] },
        
        // Detailed Hediffs
        { "Anesthetic", ["昏迷", "医护", "治疗", "不适"] },
        { "BloodLoss", ["流血", "虚弱", "不适"] },
        { "Bleeding", ["流血", "受伤", "不适"] },
        { "BrainShock", ["精神崩溃", "受伤", "不适"] },
        { "CatatonicBreakdown", ["精神崩溃", "昏迷", "无助", "不适"] },
        { "CryptosleepSickness", ["生病", "恶心", "不适"] },
        { "DrugOverdose", ["成瘾", "中毒", "危险", "不适"] },
        { "HeartAttack", ["生病", "危险", "疼痛"] },
        { "PsychicComa", ["灵能", "昏迷", "无助", "不适"] },
        { "PsychicShock", ["灵能", "精神崩溃", "不适"] },
        { "PsychicInvisibility", ["灵能", "超自然"] },
        { "ResurrectionSickness", ["虚弱", "不适"] },
        { "ToxicBuildup", ["中毒", "不适"] },
        
        // Infections & Parasites
        { "GutWorms", ["生病", "恶心", "饥饿"] },
        { "MuscleParasites", ["生病", "虚弱", "疼痛"] },
        { "FibrousMechanites", ["生病", "疼痛", "科技"] },
        { "SensoryMechanites", ["生病", "疼痛", "科技"] },
        { "WoundInfection", ["生病", "疼痛", "肮脏"] },
        { "Scaria", ["生病", "愤怒"] },
        { "LungRotExposure", ["生病", "不适"] },
        { "Cirrhosis", ["生病", "成瘾", "不适"] },
        { "ChemicalDamage", ["生病", "中毒", "残疾"] },
        
        // Chronic & Local
        { "Alzheimers", ["生病", "残疾", "不适"] },
        { "Asthma", ["生病", "不适"] },
        { "BadBack", ["疼痛", "残疾", "不适"] },
        { "Blindness", ["残疾", "黑暗"] },
        { "Carcinoma", ["生病", "绝望", "危险", "不适"] },
        { "Cataract", ["残疾", "生病"] },
        { "Dementia", ["生病", "残疾", "不适"] },
        { "Frail", ["虚弱", "不适"] },
        { "HearingLoss", ["残疾", "不适"] },
        { "HeartArteryBlockage", ["生病", "不适"] },
        { "OrganDecay", ["生病", "不适"] },
        
        // Mental States (DefNames)
        { "Berserk", ["精神崩溃", "攻击", "愤怒"] },
        { "Binging", ["饥饿", "成瘾"] },
        { "Manhunter", ["精神崩溃", "攻击"] },
        { "Panic", ["精神崩溃", "恐惧"] },
        { "Flee", ["恐惧"] },
        { "GiveUp", ["精神崩溃", "绝望"] },
        { "Tantrum", ["精神崩溃", "愤怒", "冲突"] },
        { "Sadistic", ["精神崩溃", "折磨"] },
        { "CorpseObsession", ["精神崩溃", "尸体"] },
        { "FireStarting", ["精神崩溃", "火焰", "危险"] },
        { "Jailbreaker", ["精神崩溃", "冲突"] },
        { "Slaughterer", ["精神崩溃", "攻击"] },
        { "Confused", ["精神崩溃", "无助"] },
        { "Wander", ["精神崩溃"] },
        { "Sad", ["悲伤"] },
        { "Cry", ["悲伤"] }, // Restored logic moved to map
        { "Grief", ["悲伤"] }, // Restored logic moved to map
        { "Terrified", ["恐惧"] }, // Restored logic moved to map
        { "Pyromania", ["火焰", "危险"] },
        
        // Relationships / Opinions / Bonds (DefNames)
        { "Bond", ["羁绊"] },
        { "PsychicBond", ["爱人", "羁绊"] },
        { "Lover", ["爱人", "羁绊"] },
        { "Spouse", ["爱人", "羁绊", "家人", "婚姻"] },
        { "Fiance", ["爱人", "羁绊", "婚姻"] },
        { "Parent", ["家人", "羁绊"] },
        { "Child", ["家人", "羁绊"] },
        { "Sibling", ["家人", "羁绊"] },
        { "My", ["家人"] }, // Logic from Step 3b
        { "Kin", ["家人"] }, // Logic from Step 3b
        { "Family", ["家人"] }, // Logic from Step 3b
        { "RomanceAttempt", ["交谈", "浪漫", "爱人"] },
        { "MarriageProposal", ["交谈", "婚姻", "浪漫", "爱人"] },
        { "TameAttempt", ["驯兽"] },
        { "TrainAttempt", ["驯兽", "学习"] },

        // Tech / Misc (DefNames)
        // Weather & Conditions (English DefNames)
        { "Eclipse", ["黑暗", "灾难"] },
        { "SolarFlare", ["电力", "灾难"] },
        { "Aurora", ["开心", "光亮", "平静"] },
        { "DryThunderstorm", ["灾难", "危险", "火焰"] },
        { "RainyThunderstorm", ["不适"] },
        { "SnowHard", ["寒冷"] },
        { "SnowGentle", ["寒冷"] },
        { "Clear", ["光亮", "舒适"] },

        // ==========================================
        // Section B: Chinese Keywords (Localized Text Analysis)
        // ==========================================

        // Health & Disease (Localized)
        { "流感", ["生病"] },
        { "瘟疫", ["生病"] },
        { "疟疾", ["生病"] },
        { "感染", ["生病", "肮脏"] }, // Wound infection
        { "食物中毒", ["生病", "恶心", "不适"] },
        { "中暑", ["生病", "高温"] },
        { "低温症", ["生病", "寒冷"] },
        { "中毒", ["生病", "中毒"] },
        { "戒断", ["成瘾", "疼痛", "精神崩溃"] },
        { "麻醉", ["昏迷", "医护", "治疗"] },
        { "失血", ["流血", "虚弱"] },
        { "休克", ["昏迷", "危险"] },
        { "昏迷", ["无助"] },
        { "骨折", ["受伤", "疼痛"] },
        { "烧伤", ["受伤", "火焰", "疼痛"] },
        { "冻伤", ["受伤", "寒冷", "疼痛"] },
        { "失明", ["残疾", "黑暗"] },
        { "耳聋", ["残疾", "不适"] },
        { "癌症", ["生病", "绝望"] },
        { "哮喘", ["生病", "不适"] },
        { "痴呆", ["生病", "残疾", "不适"] },
        { "体弱", ["虚弱"] },
        
        // Emotions & Thoughts (Localized Labels)
        { "愉快", ["开心"] },
        { "乐观", ["开心"] },
        { "抑郁", ["悲伤", "绝望"] },
        { "暴怒", ["愤怒", "危险"] },
        { "担心", ["焦虑"] },
        { "害怕", ["恐惧"] },
        { "惊恐", ["恐惧"] },
        
        // Social Events (Localized)
        { "闲聊", ["交谈"] },
        { "深谈", ["交谈", "羁绊"] },
        { "深入交流", ["交谈", "羁绊"] },
        { "羞辱", ["交谈", "愤怒"] },
        { "侮辱", ["交谈", "羞辱", "愤怒"] },
        { "怠慢", ["交谈", "羞辱", "愤怒"] },
        { "美言", ["交谈", "开心"] },
        { "调情", ["交谈", "爱人", "浪漫"] },
        { "求爱", ["交谈", "爱人", "浪漫"] },
        { "求婚", ["交谈", "婚姻", "浪漫", "爱人"] },
        { "离婚", ["婚姻", "分手", "悲伤"] },
        { "分手", ["悲伤"] },
        { "感情破裂", ["分手", "悲伤"] },
        { "背叛", ["悲伤", "愤怒"] },
        { "尝试交流", ["交谈", "劝说"] },
        { "越狱", ["冲突"] },
        { "鼓动越狱", ["背叛"] },
        { "驯服", ["驯兽"] },
        { "训练", ["驯兽", "学习"] },
        { "放生", ["善意"] },
        { "亲昵", ["宠物", "善意"] },
        { "婚礼", ["婚姻", "仪式", "幸福"] },
        { "聚会", ["开心"] },
        { "派对", ["聚会", "开心"] },
        { "葬礼", ["死亡", "仪式", "悲伤"] },
        { "演说", ["仪式"] },
        { "节日", ["开心"] },
        { "生日", ["成长"] },
        { "成年", ["成长"] },
        
        // Job Verbs (Chinese)
        { "搬运", ["劳动"] },
        { "掠夺", ["背叛", "敌人"] },
        { "俘虏", ["囚犯", "战斗"] },
        { "拘捕", ["囚犯", "冲突"] },
        { "押送", ["囚犯"] },
        { "剪毛", ["驯兽", "生产"] },
        { "割除", ["种植", "生产"] },
        { "处决", ["死亡", "攻击", "仪式"] },
        { "骇入", ["科技", "劳动"] },
        { "粉饰", ["建造", "艺术"] },
        { "打磨", ["建造"] },
        { "塌顶", ["事故"] }, // Roof collapsed
        { "开采失败", ["事故", "采矿"] }, // Mining fail
        { "调查", ["科研", "学习"] }, // Study/Analyze
        { "分析", ["科研", "学习"] },

        // Joy Verbs (Chinese)
        { "凝视天空", ["娱乐", "舒适"] },
        { "冥想", ["娱乐", "仪式"] },
        { "祈祷", ["娱乐", "仪式", "平静"] },
        { "散步", ["娱乐", "舒适", "平静"] },
        { "堆雪人", ["娱乐", "开心"] },
        { "扫墓", ["娱乐", "悲伤", "仪式"] },
        { "观赏艺术", ["娱乐", "艺术"] },
        { "看望病人", ["互动", "医护", "善意"] },
        { "看电视", ["娱乐", "舒适"] },
        { "演奏", ["娱乐", "艺术"] },
        { "下棋", ["娱乐", "科研"] },
        { "打牌", ["娱乐", "互动"] },

        // Major Events (Letters - Tooltip Text)
        { "袭击", ["战斗", "危险"] },
        { "猎杀", ["袭击", "战斗", "危险"] }, // Manhunter
        { "围攻", ["战斗", "危险"] },
        { "坠落", ["事故", "危险"] }, // Drop pods / Crashed ship
        { "飞船", ["科技", "危险"] }, // Ship part
        { "虫害", ["虫族", "灾难", "危险"] },
        { "请求", ["互动"] }, // Quest request
        { "任务", ["任务", "互动"] },
        { "商船", ["交易", "互动", "交谈"] },
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
        { "短路", ["事故", "电力"] },
        { "电气爆炸", ["事故", "电力"] },
        { "间谍", ["背叛", "敌人", "危险"] },
        { "阿尔法海狸", ["灾难", "种植"] },
        { "自行驯服", ["驯兽", "开心"] },
        { "罕见的敲击兽", ["驯兽", "开心", "稀有"] }, // Thrumbo
        { "索要赎金", ["互动"] },
        { "绑架", ["危险", "敌人"] }, // Kidnap -> Ransom potential
        { "发现飞船", ["科技", "希望"] },
        { "远行队相遇", ["互动", "交易"] },
        { "远行队被", ["袭击", "战斗", "危险"] }, // Caravan ambushed
        
        // Misc Concepts (Localized)
        { "屠宰", ["尸体"] },
        { "尸体", ["恐惧"] },
        { "奴隶", ["压力"] },
        { "自由", ["开心"] },
        { "文章", ["艺术"] }, // Art title?
        { "雕塑", ["艺术"] },
        
        // Restored logic from old GetTextTags (Manual Checks)
        { "攻击", ["战斗"] },
        { "贸易", ["交易"] },
        { "商人", ["交易"] },
        { "救助", ["治疗"] }, // Was "治疗" in old code for "rescue/tend"
        { "结婚", ["婚姻", "仪式", "幸福", "浪漫"] },
        { "典礼", ["仪式"] },
        { "演讲", ["聚会", "仪式"] },
        { "发狂", ["精神崩溃"] },
        { "迷茫", ["无助"] },
        
        // ==========================================
        // Section C: English Template Phrases (Function-Specific)
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
        { "Social", ["互动"] },
        { "Intellectual", ["科研"] },
        
        // Vocal Link (CompTargetEffect)
        { "gained the ability to speak", ["觉醒", "超自然"] },
        
        // BattleLog (Fallback Templates)
        { "hit", ["攻击"] },
        { "missed", ["战斗"] },
        { "deflected", ["战斗"] },
        { "shot", ["攻击"] },
        { "bit", ["攻击"] }, // Bite
        { "scratched", ["攻击"] },
        
        // Archive/Letters (Common English words in templates if any)
        { "Quest", ["任务"] },
        { "Mission", ["任务"] },
        { "Bounty", ["任务"] },
        { "Decree", ["任务", "荣耀"] }, // Royal Decree
        { "Charity", ["任务", "善意"] }, // Ideology Charity
        { "Opportunity", ["任务"] },
        { "Ritual", ["仪式"] },
        { "Conversion", ["仪式", "劝说"] },

        // ==========================================
        // Section D: Extended Coverage (Restored from Old)
        // ==========================================

        // 1. Bionics & Tech (Restored)
        { "Bionic", ["仿生", "科技"] },
        { "Archotech", ["仿生", "科技", "超自然", "觉醒", "稀有"] },
        { "Implant", ["仿生", "手术"] },
        { "Prosthetic", ["仿生"] },
        
        // Quality/Rarity
        { "Legendary", ["稀有", "艺术"] },
        { "Masterwork", ["稀有", "艺术"] },
        { "Relic", ["稀有", "仪式"] },
        { "Artifact", ["稀有", "科技"] },

        // 2. Social Roles (Restored)
        { "Soldier", ["战斗"] },
        { "Warden", ["囚犯"] },
        { "Miner", ["采矿"] },

        // 3. Events & Concepts (Restored)
        { "Raid", ["袭击", "战斗", "危险"] },
        { "Ambush", ["袭击", "战斗"] },
        { "Siege", ["战斗", "危险"] },
        { "Infestation", ["虫族", "灾难", "危险"] },
        { "ManhunterPack", ["战斗", "灾难", "危险"] },
        { "Manhunter pack", ["战斗", "灾难", "危险"] },
        { "Mad animal", ["危险", "战斗"] },
        { "Blight", ["灾难", "种植"] },
        { "Miracle", ["治疗", "幸福"] },
        { "Transport pod crash", ["事故", "获救"] },
        { "Refugee", ["获救", "互动", "善意"] },
        { "Wanderer joins", ["互动", "招募", "善意"] },
        { "Cargo pod", ["生产", "开心"] },
        { "Roof collapsed", ["事故"] },
        { "Short circuit", ["事故", "电力"] },
        { "Agent revealed", ["背叛", "敌人", "危险"] },
        { "Beavers", ["灾难", "种植"] },
        { "Self-tame", ["驯兽", "开心"] },
        { "Thrumbo", ["驯兽", "开心", "稀有"] }, // Singular generic
        { "Ransom", ["绑架", "互动"] }, // Generic English mapping
        { "Kidnap", ["绑架", "危险", "敌人"] },
        { "Ship found", ["科技", "希望"] },
        { "EscapeShip", ["科技", "希望", "胜利"] },
        { "Archonexus", ["超自然", "希望", "胜利"] },
        { "RoyalAscent", ["荣耀", "希望", "胜利"] },
        { "ReactorReady", ["科技", "希望", "电力"] },
        { "Caravan meeting", ["互动", "交易"] },
        { "Caravan demand", ["袭击", "战斗", "危险"] },
        { "Speech", ["聚会", "仪式", "交谈"] },
        { "Festival", ["聚会"] },
        { "Party", ["聚会", "互动", "开心"] },
        { "Marriage", ["婚姻", "仪式", "幸福", "浪漫"] },
        { "Wedding", ["婚姻", "仪式", "幸福", "浪漫"] },
        { "Funeral", ["死亡", "仪式", "悲伤"] },
        
        // Life Stages (Growth)
        { "Birthday", ["成长"] },
        { "Growth moment", ["成长"] },
        { "Became an adult", ["成长"] },

        // 4. Time & Weather (Restored)
        { "Morning", ["光亮"] },
        { "Evening", ["黑暗"] },
        { "Night", ["黑暗"] },
        
        // Chinese Weather Terms (Localized)
        { "旱天雷", ["灾难", "危险", "火焰"] }, // DryThunderstorm
        { "暴风雨", ["不适"] },         // RainyThunderstorm
        { "晴", ["光亮", "舒适"] },            // Clear
        { "雪", ["寒冷"] },

        // 5. Relationships (Restored)
        { "Friend", ["朋友"] },
        { "Enemy", ["敌人"] },

        // 6. Social Misc (Restored)
        { "Gossip", ["交谈"] },
        { "Joy", ["娱乐", "开心"] },
        { "Recreation", ["娱乐", "舒适"] },
        { "Filth", ["肮脏"] },
        { "Dirty", ["肮脏"] },
        { "Insulted", ["羞辱", "愤怒"] },

        // Interactions (English DefNames/Labels)
        { "Chitchat", ["交谈"] },
        { "Deep Talk", ["交谈", "羁绊"] },
        { "DeepTalk", ["交谈", "羁绊"] },
        { "Slight", ["交谈", "羞辱", "愤怒"] },
        { "Inhuman", ["觉醒"] },
        { "Insult", ["交谈", "羞辱", "愤怒"] },
        { "Kind words", ["交谈", "开心"] },
        { "Romance", ["交谈", "浪漫", "爱人"] },
        { "Proposal", ["交谈", "婚姻", "浪漫", "爱人"] },
        { "breakup", ["分手", "悲伤"] },
        { "BuildRapport", ["交谈", "劝说"] },
        { "try to get to know", ["交谈", "劝说"] },
        { "RecruitAttempt", ["招募", "交谈"] },
        { "recruit attempt", ["招募", "交谈"] },
        { "spark jailbreak", ["背叛"] },
        { "AnimalChat", ["驯兽", "互动"] },
        { "animal chat", ["驯兽", "互动"] },
        { "Nuzzle", ["宠物", "善意", "开心", "羁绊"] },
        { "release to the wild", ["善意"] },

        // Additional BattleLog
        { "Hit", ["攻击"] },
        { "Attack", ["攻击"] },
        { "Kill", ["攻击", "死亡"] },
        { "Destroy", ["攻击", "破坏"] },
        { "Defend", ["战斗"] },
        { "Defense", ["战斗"] },

        // Mined Thoughts (English Keys only, CN keys handled in Section B)
        { "butchered humanlike", ["恐惧", "尸体", "恶心"] },
        { "butchered up", ["恐惧", "尸体", "恶心"] },
        { "witnessed death", ["死亡", "悲伤"] },
        { "witnessed ally's death", ["死亡", "悲伤"] },
        { "witnessed family", ["死亡", "悲伤", "家人"] },
        { "ObservedLaying", ["尸体", "恐惧"] }, // Partial match safety
        { "observed rotting corpse", ["尸体", "恐惧", "恶心"] },
        { "botched my surgery", ["手术", "事故", "受伤"] },

        { "divorced", ["婚姻", "分手", "悲伤"] },
        { "cheated on", ["背叛", "悲伤", "愤怒"] },
        { "rejected my proposal", ["婚姻", "拒绝", "悲伤"] },
        { "broke up", ["分手", "悲伤"] },
        { "rebuffed", ["拒绝"] },
        { "failed to romance", ["拒绝"] },

        { "Lovin", ["性爱", "互动", "浪漫", "开心"] },
        { "honeymoon phase", ["浪漫", "幸福"] },
        { "rescued", ["获救", "幸福", "善意"] },
        { "defeated", ["胜利", "荣耀", "战斗"] },
        { "freed from slavery", ["奴隶", "幸福"] },
        { "catharsis", ["开心", "平静"] },
        { "soaked", ["潮湿", "不适"] },
        { "soaking wet", ["潮湿", "不适"] },

        // Moved from Step 3b (Logic integration)
        { "Died", ["悲伤", "死亡"] },
        { "Death", ["悲伤", "死亡"] },
        { "Lost", ["悲伤"] },
        { "Killed", ["悲伤", "死亡"] },
        { "Beat", ["愤怒", "冲突"] },
        { "Fight", ["愤怒", "冲突"] },
        { "Harmed", ["愤怒", "冲突"] },
        { "Betray", ["背叛", "愤怒"] },
        { "Fear", ["恐惧"] },
        { "Phobia", ["恐惧"] },
        { "Nightmare", ["恐惧"] },
        { "Terrified", ["恐惧"] },
        { "Worry", ["焦虑"] },
        { "Anxious", ["焦虑"] },
        { "Lonely", ["压力"] },
        { "Isolation", ["压力"] },
        { "Prison", ["压力", "囚犯"] },
        { "Confined", ["压力", "囚犯"] },
        { "Hospital", ["生病"] },
        { "Sick", ["生病"] },

        // Work
        { "Research", ["科研"] },
        { "Study", ["学习"] },
        { "Operate", ["劳动"] },
        { "Construct", ["建造", "制作"] },
        { "Build", ["建造", "制作"] },
        { "Repair", ["劳动"] },
        { "Mine", ["采矿", "生产"] },
        { "Drill", ["采矿", "科技"] },
        { "Ingest", ["生存"] }, // Eating
        { "LayDown", ["舒适"] }, // Sleeping/Resting
        { "Sleep", ["舒适"] },
        { "Sow", ["种植", "生产"] },
        { "Harvest", ["种植"] },
        { "Hunt", ["攻击", "生存"] },
        { "Tame", ["驯兽"] },
        { "Train", ["驯兽", "学习"] },
        { "Cook", ["制作", "生存"] },
        { "Butcher", ["尸体"] },
        { "Clean", ["劳动", "肮脏"] },
        { "Wash", ["肮脏", "舒适"] },
        { "Haul", ["劳动"] },
        { "Breed", ["驯兽", "生产"] },
        
        // Joy / Recreation (Jobs)
        { "Skygaze", ["娱乐", "舒适", "开心"] },
        { "Meditate", ["娱乐", "仪式", "平静"] },
        { "Pray", ["娱乐", "仪式", "平静"] },
        { "GoForWalk", ["娱乐", "舒适"] },
        { "BuildSnowman", ["娱乐", "开心"] },
        { "VisitGrave", ["娱乐", "仪式", "悲伤", "死亡"] },
        { "ViewArt", ["娱乐", "艺术", "开心"] },
        { "VisitSickPawn", ["互动", "医护", "善意"] },
        { "Play", ["娱乐", "开心"] }, // General Play
        { "WatchTelevision", ["娱乐", "舒适"] },
        { "UseTelescope", ["娱乐", "科技"] },
        { "Poker", ["娱乐", "互动"] },
        { "Chess", ["娱乐", "科研"] },
        { "Hoopstone", ["娱乐"] },
        
        // Specific Work Jobs
        { "Steal", ["背叛", "敌人"] },
        { "Capture", ["囚犯", "绑架"] },
        { "Arrest", ["囚犯", "冲突"] },
        { "ReleasePrisoner", ["善意"] },
        { "EscortPrisoner", ["囚犯"] },
        { "Milk", ["驯兽", "生产"] },
        { "Shear", ["驯兽", "生产"] },
        { "CutPlant", ["种植", "生产"] },
        { "Execution", ["死亡", "攻击", "仪式"] },
        { "Execute", ["死亡", "攻击", "仪式"] },
        { "Hack", ["科技", "危险"] },
        { "Roof", ["建造"] },
        { "Smooth", ["建造"] },
        { "Paint", ["建造", "艺术"] },

        { "Art", ["艺术"] },
        { "Sculpt", ["艺术"] },

        // Royalty
        { "Royal", ["荣耀"] },
        { "Title", ["荣耀"] },
        { "Honor", ["荣耀"] },

        // Tech Mod
        { "Reactor", ["工业", "科技", "危险"] },
        { "Radiation", ["中毒", "危险", "灾难"] },
        { "Nuke", ["工业", "灾难"] },
        { "Oil", ["工业"] },
        { "Drilling", ["工业", "采矿"] },
        { "Chemfuel", ["工业"] },
        { "Pipeline", ["工业", "建造"] },
        { "Robot", ["机械", "科技"] },
        { "Mech", ["机械", "科技"] },
        { "Droid", ["机械", "科技"] },
        { "Laser", ["科技"] },
        { "Plasma", ["科技"] },
        { "Power", ["电力"] },
        { "Battery", ["电力"] },
        { "Generator", ["电力"] },

        // Magic
        { "Magic", ["灵能", "超自然", "觉醒"] },
        { "Mana", ["灵能", "超自然", "觉醒"] },
        { "Spell", ["灵能", "超自然", "觉醒"] },
        { "Arcane", ["灵能", "超自然", "觉醒"] }
    };

    /// <summary>
    /// Analyzes the Pawn's current state to extract Abstract Abstract Tags.
    /// </summary>
    public static List<string> GetAbstractTags(Pawn p)
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
            if (pain > 0.8f) { tags.Add("严重"); } // Extreme pain (Shock)

            // Consciousness / Capacities
            if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness))
            {
                tags.Add("昏迷");
                tags.Add("无助");
            }

            // Bleeding
            if (p.health.hediffSet.BleedRateTotal > 0.1f) { tags.Add("流血"); }
            if (p.health.hediffSet.BleedRateTotal > 0.3f) { tags.Add("严重"); } // Moderate bleeding
            if (p.health.hediffSet.BleedRateTotal > 0.6f) { tags.Add("危险"); } // Significant bleeding is danger

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
                        if (hediff.CurStage.lifeThreatening)
                        {
                            tags.Add("危险");
                        }
                    }

                    // Generic Severity Check
                    if (hediff.Severity > 0.6f) tags.Add("严重");
                }

                // Specific Logic (Independent of isBad)
                if (hediff is Hediff_Injury) tags.Add("受伤");
                if (hediff is Hediff_MissingPart) tags.Add("残疾");

                if (hediff.def.countsAsAddedPartOrImplant) tags.Add("仿生");
                if (hediff.def.IsAddiction) tags.Add("成瘾");
            }
        }

        // 2. Mental State (Detailed)
        if (p.InMentalState)
        {
            tags.Add("精神崩溃");
            AddTagsFromDef(p.MentalStateDef, tags); // Use Def for generic keywords

            // Emotional mapping based on state type
            if (p.MentalStateDef.IsAggro) { tags.Add("愤怒"); tags.Add("战斗"); }
            if (p.MentalStateDef.IsExtreme) { tags.Add("绝望"); } // Extreme break
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

            if (curMood < breakMajor) { tags.Add("绝望"); tags.Add("严重"); } // High risk
            else if (curMood < breakMinor) { tags.Add("压力"); tags.Add("焦虑"); } // Moderate risk
            else if (curMood > 0.9f) { tags.Add("幸福"); } // Extreme Happiness
            else if (curMood > 0.65f) { tags.Add("开心"); } // High mood

            foreach (var thought in thoughts)
            {
                AddTagsFromDef(thought.def, tags); // Use Basic Keywords

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
        }

        // 4. Current Job
        if (p.CurJob != null)
        {
            AddTagsFromDef(p.CurJob.def, tags); // Use Def
            // Contextual jobs
            if (p.CurJob.def == JobDefOf.AttackMelee || p.CurJob.def == JobDefOf.AttackStatic) { tags.Add("攻击"); tags.Add("战斗"); }
            if (p.CurJob.def == JobDefOf.SocialFight) { tags.Add("争吵"); tags.Add("战斗"); }
            if (p.CurJob.def == JobDefOf.PredatorHunt) { tags.Add("战斗"); }
            if (p.CurJob.def == JobDefOf.PrisonerAttemptRecruit) { tags.Add("招募"); tags.Add("互动"); }
            if (p.CurJob.def == JobDefOf.Tame) { tags.Add("驯兽"); }
        }

        // 5. Relations/Status
        if (p.IsSlave) tags.Add("奴隶");
        if (p.IsPrisoner) tags.Add("囚犯");
        if (p.RaceProps.IsMechanoid) tags.Add("机械");

        // Static Relations (Context-Aware: Only if nearby)
        if (p.relations != null)
        {
            foreach (var rel in p.relations.DirectRelations)
            {
                AddTagsFromDef(rel.def, tags); // Base keywords

                // Distinguish Bonds
                if (rel.def.defName == "Bond")
                {
                    if (rel.otherPawn != null && rel.otherPawn.RaceProps.Animal) tags.Add("宠物");
                }

                // Family (Granular)
                if (rel.def.defName == "Parent") { tags.Add("家人"); }
                if (rel.def.defName == "Child") { tags.Add("家人"); }
                if (rel.def.defName == "Sibling") tags.Add("家人");

                if (rel.def.defName == "Spouse" || rel.def.defName == "Lover" || rel.def.defName == "Fiance") tags.Add("爱人");

                // Opinion Check for Relations
                if (rel.otherPawn != null)
                {
                    int opinion = p.relations.OpinionOf(rel.otherPawn);
                    // Differentiate Friend/Rival based on opinion
                    if (opinion >= 50) { tags.Add("朋友"); }
                    if (opinion >= 90) { tags.Add("挚友"); }
                    if (opinion <= -20) { tags.Add("敌人"); }
                    if (opinion <= -50) { tags.Add("仇人"); }
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
            if (hour >= 6 && hour < 10) tags.Add("光亮"); // Morning
            else if (hour >= 22 || hour < 5) tags.Add("黑暗"); // Night

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
        return generatedTags.Where(t => CoreMemoryTags.KeywordMap.Contains(t)).ToList();
    }
}
