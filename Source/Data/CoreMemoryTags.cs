using System.Collections.Generic;

namespace RimTalk.Data;
public static class CoreMemoryTags
{
    // 定義核心標籤映射字典
    // Key: 關鍵字 (小寫), Value: 標籤
    // ==========================================
    // Core Memory Tags (Atomic Concepts)
    // ==========================================
    public static readonly HashSet<string> KeywordMap =
    [
        // A. 生理与生存 (Physiology & Survival)
        "健康", "受伤", "生病", "疼痛", "残疾", "治疗", "手术",
        "生存", "饥饿", "流血", "昏迷", "虚弱",
        "成瘾", "中毒",
        "不适", "恶心", "尸体", "严重", "危险",

        // B. 环境与感知 (Environment & Perception)
        "寒冷", "高温", "黑暗", "光亮",
        "肮脏", "舒适", "潮湿",
        "灾难", "火焰",
        "艺术",

        // C. 社会与关系 (Social & Relationships)

        "互动", "交谈", "争吵", "羞辱", "婚姻", "分手",
        "仪式", "聚会", "劝说", "浪漫", "性爱",
        "羁绊", "敌人", "仇人", "家人", "爱人", "朋友", "挚友", "囚犯", "奴隶",
        "背叛", "荣耀", "宠物",
        "事故",
        "获救", "胜利",

        // D. 威胁与战斗 (Threats & Combat)
        "冲突", "袭击", "战斗", "攻击",
        "恐惧",
        "精神崩溃", "压力",
        "开心", "幸福", "悲伤", "愤怒", "焦虑", "无助", "绝望", // Basic Emotions

        // E. 工作与行为 (Work & Achievement)
        "生产", "建造", "种植", "采矿", "制作", "劳动", "成长", // Work basics
        "科研", "医护", "驯兽", "招募", "交易", "学习", "娱乐", // Activities
        "任务", "绑架",

        // F. 科技与超凡 (Tech & Supernatural)
        "科技", "机械", "工业", "电力", "觉醒",
        "超自然", "灵能", "异种", "虫族", "稀有", "希望",
        "善意", "平静" // Extended tags from JobDefs
    ];
}
