using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimWorld;
using Verse;

namespace RimTalk.Util;

public static class CoreTagMapper
{
    private static ThoughtDef _observedLayingCorpseDef;

    /// <summary>
    /// 分析 Pawn 的当前状态 (健康、精神、工作、想法)，提取核心标签。
    /// </summary>
    public static List<string> GetAbstractTags(Pawn p)
    {
        var tags = new HashSet<string>();
        if (p == null) return [];

        // =========================================================
        // 1. 健康状态分析 (Hediff Analysis)
        // =========================================================
        if (p.health != null)
        {
            foreach (var hediff in p.health.hediffSet.hediffs)
            {
                if (!hediff.Visible) continue;
                string defName = hediff.def.defName;

                // --- 通用关键字匹配 ---
                if (defName.Contains("Pain") || defName.Contains("Agony")) tags.Add("痛苦");
                if (defName.Contains("Infection") || defName.Contains("Flu") || defName.Contains("Plague") || defName.Contains("Sickness") || defName.Contains("Disease")) tags.Add("生病");
                if (defName.Contains("Burn") || defName.Contains("Fire") || defName.Contains("Flame")) { tags.Add("火焰"); tags.Add("烧伤"); }
                if (defName.Contains("Freeze") || defName.Contains("Frost") || defName.Contains("Hypothermi") || defName.Contains("Cryo") || defName.Contains("Ice")) { tags.Add("寒冷"); tags.Add("冻伤"); }
                if (defName.Contains("Heat") || defName.Contains("Hyperthermi")) { tags.Add("过热"); tags.Add("危险"); }
                if (defName.Contains("Toxic") || defName.Contains("Poison") || defName.Contains("Venom") || defName.Contains("Gas")) { tags.Add("中毒"); tags.Add("毒气"); }
                if (defName.Contains("Acid")) { tags.Add("酸蚀"); tags.Add("痛苦"); }
                if (defName.Contains("EMP") || defName.Contains("Shock") || defName.Contains("Electric")) { tags.Add("电击"); tags.Add("瘫痪"); }
                if (defName.Contains("Radiation") || defName.Contains("Nuclear")) { tags.Add("辐射"); tags.Add("危险"); }
                if (defName.Contains("Coma") || defName.Contains("Unconscious") || defName.Contains("Anesthetic")) { tags.Add("昏迷"); tags.Add("无助"); }
                if (defName.Contains("Scar") || defName.Contains("Old")) tags.Add("疤痕");
                if (defName.Contains("Blind")) { tags.Add("残疾"); tags.Add("黑暗"); }
                if (defName.Contains("Deaf") || defName.Contains("Missing")) tags.Add("残疾");

                // --- 模组与DLC特定逻辑 ---
                if (defName.StartsWith("VPE")) // Vanilla Psycasts Expanded
                {
                    tags.Add("灵能");
                    if (defName.Contains("MindControl")) tags.Add("控制");
                    if (defName.Contains("Regrow")) tags.Add("再生");
                    if (defName.Contains("Invisibility")) tags.Add("隐形");
                }
                else if (defName.StartsWith("VREA_")) // Androids
                {
                    tags.Add("机器人"); tags.Add("高科技");
                    if (defName.Contains("Reactor")) tags.Add("能量");
                    if (defName.Contains("Overheating") || defName.Contains("Freezing")) tags.Add("故障");
                }
                else if (defName.StartsWith("VRE_")) // Biotech Races
                {
                    tags.Add("基因");
                    if (defName.Contains("Sanguophage") || defName.Contains("Hemogen")) tags.Add("血族");
                    if (defName.Contains("Insect")) tags.Add("虫族");
                }
                else if (defName.Contains("Addiction") || defName.Contains("Withdrawal"))
                {
                    tags.Add("成瘾"); tags.Add("痛苦");
                }
                else if (defName.Contains("High"))
                {
                    tags.Add("开心"); tags.Add("迷幻");
                }
            }
        }

        // =========================================================
        // 2. 精神状态 (Mental State)
        // =========================================================
        if (p.InMentalState)
        {
            var ms = p.MentalStateDef;
            string msName = ms.defName;

            tags.Add("崩溃");
            if (ms.IsAggro) tags.Add("愤怒");

            if (msName.Contains("Berserk")) tags.Add("狂暴");
            else if (msName.Contains("Tantrum")) tags.Add("发脾气");
            else if (msName.Contains("Binging")) tags.Add("放纵");
            else if (msName.Contains("Manhunter")) { tags.Add("猎杀"); tags.Add("狂暴"); }
            else if (msName.Contains("Flee") || msName.Contains("Panic")) { tags.Add("恐惧"); tags.Add("逃跑"); }
            else if (msName.Contains("CorpseObsession")) { tags.Add("尸体"); tags.Add("迷恋"); }
            else if (msName.Contains("FireStarting")) tags.Add("纵火");
            else if (msName.Contains("GiveUp")) { tags.Add("放弃"); tags.Add("绝望"); }
            else if (msName.Contains("Insulting")) tags.Add("侮辱");
            else if (msName.Contains("Murderous")) tags.Add("杀戮");
            else if (msName.Contains("Sadistic")) tags.Add("施虐");
            else if (msName.Contains("Wander")) { tags.Add("徘徊"); tags.Add("迷茫"); }
            else if (msName.Contains("DarkVisions") || msName.Contains("Hallucinations")) { tags.Add("幻觉"); tags.Add("恐怖"); }
        }

        // =========================================================
        // 3. 想法 (Thoughts)
        // =========================================================
        if (p.needs?.mood?.thoughts != null)
        {
            var thoughts = new List<Thought>();
            p.needs.mood.thoughts.GetAllMoodThoughts(thoughts);

            foreach (var thought in thoughts)
            {
                string defName = thought.def.defName;
                if (defName == "NeedPyromania") tags.Add("纵火");
                else if (defName.Contains("Slave") && thought.CurStage.baseMoodEffect < 0) { tags.Add("奴役"); tags.Add("压迫"); }
                else if (defName.Contains("Inhuman")) tags.Add("人性");
                else if (defName.Contains("OrganizedWorkspace")) tags.Add("效率");
                else if (defName.Contains("Darkness")) tags.Add("黑暗");
                else if (defName.Contains("Spacious")) tags.Add("舒适");
                else if (defName.Contains("Beautiful")) tags.Add("美观");
                else if (defName.Contains("Ugly")) tags.Add("肮脏");
            }
        }

        // =========================================================
        // 4. 当前工作 (Jobs)
        // =========================================================
        if (p.CurJob != null)
        {
            string jobName = p.CurJob.def.defName;

            if (jobName.Contains("Attack") || jobName.Contains("Hunt") || jobName.Contains("ManTurret")) tags.Add("战斗");
            else if (jobName.Contains("Capture") || jobName.Contains("Arrest") || jobName.Contains("EscortPrisoner")) { tags.Add("抓捕"); tags.Add("监狱"); }
            else if (jobName.Contains("Rescue") || jobName.Contains("Tend") || jobName.Contains("Treat")) { tags.Add("救援"); tags.Add("医疗"); }
            else if (jobName.Contains("Construct") || jobName.Contains("Build") || jobName.Contains("Repair")) { tags.Add("建造"); tags.Add("修理"); }
            else if (jobName.Contains("Sow") || jobName.Contains("Harvest") || jobName.Contains("CutPlant")) { tags.Add("农业"); tags.Add("种植"); }
            else if (jobName.Contains("Mine") || jobName.Contains("Drill")) tags.Add("采矿");
            else if (jobName.Contains("Research")) { tags.Add("研究"); tags.Add("智识"); }
            else if (jobName.Contains("Clean")) tags.Add("清洁");
            else if (jobName.Contains("Haul") || jobName.Contains("Carry")) tags.Add("搬运");
            else if (jobName.Contains("Cook") || jobName.Contains("Make")) tags.Add("制作");
            else if (jobName.Contains("Ingest") || jobName.Contains("Eat")) tags.Add("饮食");
            else if (jobName.Contains("Sleep") || jobName.Contains("LayDown")) tags.Add("睡眠");
            else if (jobName.Contains("Lovin")) { tags.Add("爱欲"); tags.Add("开心"); }
            else if (jobName.Contains("Pray") || jobName.Contains("Meditate")) { tags.Add("信仰"); tags.Add("平静"); }
            else if (jobName.Contains("Suppress")) { tags.Add("抑制"); tags.Add("收容"); }
            else if (jobName.Contains("Extract")) tags.Add("抽取");
        }

        // ==========================================
        // 5. 身份与需求 (Needs & Identity)
        // ==========================================
        if (p.IsPrisoner) tags.Add("监狱");
        if (p.IsSlave) tags.Add("奴役");

        if (p.needs != null)
        {
            foreach (var need in p.needs.AllNeeds)
            {
                if (need.CurLevelPercentage < 0.1f)
                {
                    if (need.def.defName == "Food") tags.Add("饥饿");
                    else if (need.def.defName == "Rest") tags.Add("疲劳");
                    else if (need.def.defName == "Joy") tags.Add("无聊");
                    else if (need.def.defName.Contains("Chemical")) tags.Add("成瘾");
                }
            }
        }

        // ==========================================
        // 6. 灵感状态 (Inspirations)
        // ==========================================
        if (p.mindState?.inspirationHandler?.CurState != null)
        {
            var inspName = p.mindState.inspirationHandler.CurState.def.defName;
            tags.Add("灵感");
            if (inspName.Contains("Frenzy")) tags.Add("狂热");
            if (inspName.Contains("Creativity")) tags.Add("创作");
            if (inspName.Contains("Trade")) tags.Add("商贸");
            if (inspName.Contains("Surgery")) tags.Add("医疗");
        }

        // ==========================================
        // 7. 关系 (Relationships)
        // ==========================================
        if (p.relations != null)
        {
            foreach (var relation in p.relations.DirectRelations)
            {
                if (relation.otherPawn == null || relation.otherPawn.Map != p.Map) continue;
                string relName = relation.def.defName;
                if (relName.Contains("Spouse") || relName.Contains("Lover")) tags.Add("爱情");
                if (relName.Contains("Parent") || relName.Contains("Child") || relName.Contains("Sibling")) tags.Add("家庭");
                if (relName.Contains("Bond")) tags.Add("牵绊");
            }
        }

        // ==========================================
        // 8. 观察屍體 (Memories)
        // ==========================================
        if (_observedLayingCorpseDef == null)
            _observedLayingCorpseDef = DefDatabase<ThoughtDef>.GetNamed("ObservedLayingCorpse", false);

        if (p.needs?.mood?.thoughts?.memories != null && _observedLayingCorpseDef != null)
        {
            if (p.needs.mood.thoughts.memories.Memories.Any(m => m.def == _observedLayingCorpseDef))
            {
                tags.Add("死亡"); tags.Add("恐惧"); tags.Add("尸体");
            }
        }

        // 過濾並返回
        return tags.Where(t => Constant.CoreMemoryTags.Contains(t)).ToList();
    }

    /// <summary>
    /// 根据 RimTalk 传入的互动标签文本或 HistoryEventDef.label 提取核心标签。
    /// </summary>
    public static List<string> GetEventTags(string text)
    {
        var tags = new HashSet<string>();
        if (string.IsNullOrEmpty(text)) return [];

        // ==========================================
        // 1. 環境與災害 (Environment & Disaster)
        // ==========================================
        if (text.Contains("寒流") || text.Contains("极度深寒")) { tags.Add("寒流"); tags.Add("寒冷"); tags.Add("低温"); }
        else if (text.Contains("热浪") || text.Contains("异常高温")) { tags.Add("热浪"); tags.Add("高温"); tags.Add("中暑"); }
        else if (text.Contains("闪电") || text.Contains("雷暴") || text.Contains("风暴")) { tags.Add("闪电"); tags.Add("风暴"); tags.Add("雷暴"); }
        else if (text.Contains("有毒尘埃") || text.Contains("污染") || text.Contains("酸雾")) { tags.Add("污染"); tags.Add("有毒"); tags.Add("酸雾"); }
        else if (text.Contains("日蚀") || text.Contains("黑幕") || text.Contains("黑暗")) { tags.Add("黑暗"); tags.Add("日蚀"); }
        else if (text.Contains("极光")) { tags.Add("极光"); tags.Add("美丽"); }
        else if (text.Contains("火山") || text.Contains("熔岩") || text.Contains("灰烬")) { tags.Add("火山"); tags.Add("熔岩"); tags.Add("灰烬"); }
        else if (text.Contains("血雨") || text.Contains("血月")) { tags.Add("血雨"); tags.Add("血月"); tags.Add("恐惧"); }
        else if (text.Contains("死灵") || text.Contains("尸体") || text.Contains("复活")) { tags.Add("死灵"); tags.Add("尸体"); tags.Add("复活"); }
        else if (text.Contains("耀斑") || text.Contains("电磁") || text.Contains("停电")) { tags.Add("耀斑"); tags.Add("停电"); tags.Add("故障"); }
        else if (text.Contains("流星") || text.Contains("坠落")) { tags.Add("流星"); tags.Add("坠落"); }
        else if (text.Contains("时空") || text.Contains("震动")) { tags.Add("时空"); tags.Add("震动"); }
        else if (text.Contains("干旱") || text.Contains("缺水")) { tags.Add("干旱"); tags.Add("缺水"); }

        // ==========================================
        // 2. 聚會與儀式 (Gatherings & Rituals)
        // ==========================================
        if (text.Contains("结婚") || text.Contains("婚礼")) { tags.Add("婚礼"); tags.Add("爱情"); tags.Add("庆祝"); }
        else if (text.Contains("聚会") || text.Contains("派对") || text.Contains("狂欢")) { tags.Add("聚会"); tags.Add("派对"); tags.Add("狂欢"); }
        else if (text.Contains("音乐会")) { tags.Add("音乐会"); tags.Add("艺术"); tags.Add("娱乐"); }
        else if (text.Contains("葬礼") || text.Contains("悼念") || text.Contains("缅怀")) { tags.Add("葬礼"); tags.Add("死亡"); tags.Add("悲伤"); }
        else if (text.Contains("演讲") || text.Contains("领袖")) { tags.Add("演讲"); tags.Add("领袖"); }
        else if (text.Contains("仪式") || text.Contains("典礼") || text.Contains("祭坛")) { tags.Add("仪式"); tags.Add("信仰"); tags.Add("神圣"); }
        else if (text.Contains("观星") || text.Contains("苍穹")) { tags.Add("观星"); tags.Add("宁静"); }
        else if (text.Contains("野营") || text.Contains("篝火")) { tags.Add("野营"); tags.Add("自然"); }
        else if (text.Contains("电影") || text.Contains("观影")) { tags.Add("电影"); tags.Add("娱乐"); }
        else if (text.Contains("散步") || text.Contains("进餐")) { tags.Add("散步"); tags.Add("进餐"); tags.Add("社交"); }
        else if (text.Contains("赏艺") || text.Contains("鉴赏")) { tags.Add("赏艺"); tags.Add("艺术"); }

        // ==========================================
        // 3. 工作與活動 (Work & Activities)
        // ==========================================
        if (text.Contains("研究") || text.Contains("智识") || text.Contains("扫描")) { tags.Add("研究"); tags.Add("智识"); tags.Add("科技"); }
        else if (text.Contains("医疗") || text.Contains("手术") || text.Contains("治疗") || text.Contains("输血")) { tags.Add("医疗"); tags.Add("手术"); tags.Add("治疗"); }
        else if (text.Contains("建造") || text.Contains("修理") || text.Contains("拆除")) { tags.Add("建造"); tags.Add("修理"); }
        else if (text.Contains("采矿") || text.Contains("钻井") || text.Contains("矿脉")) { tags.Add("采矿"); tags.Add("资源"); }
        else if (text.Contains("种植") || text.Contains("收获") || text.Contains("农业")) { tags.Add("种植"); tags.Add("农业"); tags.Add("食物"); }
        else if (text.Contains("制作") || text.Contains("制造") || text.Contains("加工")) { tags.Add("制作"); tags.Add("制造"); }
        else if (text.Contains("烹饪") || text.Contains("做饭") || text.Contains("美食")) { tags.Add("烹饪"); tags.Add("美食"); }
        else if (text.Contains("驯兽") || text.Contains("驯服") || text.Contains("训练")) { tags.Add("驯兽"); tags.Add("动物"); }
        else if (text.Contains("艺术") || text.Contains("创作") || text.Contains("雕塑")) { tags.Add("艺术"); tags.Add("创作"); }
        else if (text.Contains("清洁") || text.Contains("打扫")) { tags.Add("清洁"); tags.Add("卫生"); }
        else if (text.Contains("搬运") || text.Contains("运输")) { tags.Add("搬运"); tags.Add("工作"); }
        else if (text.Contains("骇入") || text.Contains("终端")) { tags.Add("骇入"); tags.Add("数据"); }
        else if (text.Contains("抑制") || text.Contains("收容")) { tags.Add("抑制"); tags.Add("收容"); tags.Add("异象"); }
        else if (text.Contains("抽取") || text.Contains("活铁")) { tags.Add("抽取"); tags.Add("活铁"); tags.Add("痛苦"); }
        else if (text.Contains("酿造") || text.Contains("发酵")) { tags.Add("酿造"); tags.Add("酿酒"); }
        else if (text.Contains("书写") || text.Contains("阅读")) { tags.Add("书写"); tags.Add("阅读"); tags.Add("书籍"); }

        // ==========================================
        // 4. 派系與關係 (Factions & Relations)
        // ==========================================
        // --- 貿易與外交 ---
        if (text.Contains("交易") || text.Contains("商贸") || text.Contains("商人")) { tags.Add("交易"); tags.Add("商贸"); tags.Add("财富"); }
        else if (text.Contains("和平") || text.Contains("外交") || text.Contains("谈判")) { tags.Add("和平"); tags.Add("外交"); tags.Add("谈判"); }
        else if (text.Contains("盟友") || text.Contains("友善") || text.Contains("礼物")) { tags.Add("盟友"); tags.Add("友善"); tags.Add("礼物"); }
        else if (text.Contains("加入") || text.Contains("收留") || text.Contains("招募")) { tags.Add("加入"); tags.Add("新成员"); }
        else if (text.Contains("乞讨") || text.Contains("施舍")) { tags.Add("乞讨"); tags.Add("贫困"); tags.Add("慈善"); }
        else if (text.Contains("难民") || text.Contains("救援")) { tags.Add("难民"); tags.Add("救援"); tags.Add("生存"); }
        else if (text.Contains("朝圣") || text.Contains("访客")) { tags.Add("朝圣"); tags.Add("访客"); }

        // --- 敵對與戰鬥 ---
        else if (text.Contains("袭击") || text.Contains("攻击") || text.Contains("敌人")) { tags.Add("袭击"); tags.Add("战斗"); tags.Add("危险"); }
        else if (text.Contains("海盗") || text.Contains("掠夺") || text.Contains("强盗")) { tags.Add("海盗"); tags.Add("掠夺"); tags.Add("暴力"); }
        else if (text.Contains("背叛") || text.Contains("逃兵")) { tags.Add("背叛"); tags.Add("逃兵"); tags.Add("愤怒"); }
        else if (text.Contains("围攻") || text.Contains("炮击")) { tags.Add("围攻"); tags.Add("炮击"); tags.Add("战争"); }
        else if (text.Contains("前哨") || text.Contains("据点")) { tags.Add("战斗"); tags.Add("据点"); }
        else if (text.Contains("猎杀") || text.Contains("追捕")) { tags.Add("猎杀"); tags.Add("追捕"); }
        else if (text.Contains("监狱") || text.Contains("囚犯") || text.Contains("抓捕")) { tags.Add("监狱"); tags.Add("囚犯"); tags.Add("抓捕"); }
        else if (text.Contains("奴役") || text.Contains("奴隶")) { tags.Add("奴役"); tags.Add("压迫"); }

        // --- 特定派系 ---
        else if (text.Contains("帝国") || text.Contains("星系主宰")) { tags.Add("帝国"); tags.Add("荣誉"); tags.Add("高傲"); }
        else if (text.Contains("部落") || text.Contains("酋长")) { tags.Add("部落"); tags.Add("原始"); tags.Add("自然"); }
        else if (text.Contains("外来者") || text.Contains("联盟")) { tags.Add("外来者"); tags.Add("科技"); }
        else if (text.Contains("机械") && (text.Contains("族") || text.Contains("巢穴"))) { tags.Add("机械族"); tags.Add("冷酷"); tags.Add("杀戮"); }
        else if (text.Contains("虫族") || text.Contains("虫巢")) { tags.Add("虫族"); tags.Add("地下"); tags.Add("感染"); }
        else if (text.Contains("古代人") || text.Contains("失落")) { tags.Add("古代人"); tags.Add("神秘"); tags.Add("强敌"); }
        else if (text.Contains("维京") || text.Contains("氏族")) { tags.Add("维京"); tags.Add("氏族"); tags.Add("野蛮"); }
        else if (text.Contains("中世纪") || text.Contains("王国") || text.Contains("骑士")) { tags.Add("王国"); tags.Add("骑士"); tags.Add("荣耀"); }

        // ==========================================
        // 5. 生物與實體 (Creatures & Entities)
        // ==========================================
        // --- 異象與恐怖 ---
        if (text.Contains("实体") || text.Contains("异象")) { tags.Add("实体"); tags.Add("异象"); tags.Add("未知"); }
        else if (text.Contains("食尸鬼") || text.Contains("蹒跚者")) { tags.Add("食尸鬼"); tags.Add("蹒跚者"); tags.Add("恐怖"); }
        else if (text.Contains("视界") || text.Contains("吞噬")) { tags.Add("视界"); tags.Add("吞噬"); tags.Add("隐形"); }
        else if (text.Contains("血肉兽") || text.Contains("肉博")) { tags.Add("血肉兽"); tags.Add("变异"); tags.Add("血肉"); }
        else if (text.Contains("金属恐怖")) { tags.Add("金属恐怖"); tags.Add("寄生"); tags.Add("背叛"); }
        else if (text.Contains("夜行兽") || text.Contains("黑暗")) { tags.Add("夜行兽"); tags.Add("黑暗"); tags.Add("恐惧"); }
        else if (text.Contains("亡灵") || text.Contains("僵尸") || text.Contains("尸潮")) { tags.Add("亡灵"); tags.Add("僵尸"); tags.Add("复活"); }
        else if (text.Contains("恶魔") || text.Contains("地狱") || text.Contains("撒旦")) { tags.Add("恶魔"); tags.Add("地狱"); tags.Add("邪恶"); }
        else if (text.Contains("天使") || text.Contains("天国") || text.Contains("神圣")) { tags.Add("天使"); tags.Add("天国"); tags.Add("神圣"); }
        else if (text.Contains("妖怪") || text.Contains("百鬼")) { tags.Add("妖怪"); tags.Add("灵异"); tags.Add("神秘"); }

        // --- 奇幻與異種 ---
        else if (text.Contains("巨人") || text.Contains("约顿") || text.Contains("食人魔")) { tags.Add("巨人"); tags.Add("强壮"); tags.Add("贪吃"); }
        else if (text.Contains("矮人") || text.Contains("地精")) { tags.Add("矮人"); tags.Add("工匠"); tags.Add("顽固"); }
        else if (text.Contains("小人族") || text.Contains("小民")) { tags.Add("小人族"); tags.Add("机敏"); }
        else if (text.Contains("狼人") || text.Contains("化狼")) { tags.Add("狼人"); tags.Add("变身"); tags.Add("野兽"); }
        else if (text.Contains("吸血") || text.Contains("血族")) { tags.Add("吸血鬼"); tags.Add("血族"); tags.Add("永生"); }
        else if (text.Contains("蛇人") || text.Contains("蝰蛇")) { tags.Add("蛇人"); tags.Add("冷血"); tags.Add("狡猾"); }
        else if (text.Contains("史莱姆") || text.Contains("粘液")) { tags.Add("史莱姆"); tags.Add("粘液"); tags.Add("分裂"); }
        else if (text.Contains("异种") || text.Contains("变异")) { tags.Add("异种"); tags.Add("变异"); tags.Add("基因"); }
        else if (text.Contains("树精") || text.Contains("母树")) { tags.Add("树精"); tags.Add("自然"); tags.Add("共生"); }

        // --- 動物與機械 ---
        else if (text.Contains("陆行鸟")) { tags.Add("陆行鸟"); tags.Add("坐骑"); tags.Add("速度"); }
        else if (text.Contains("沙狮") || text.Contains("潜行者")) { tags.Add("沙狮"); tags.Add("潜伏"); tags.Add("危险"); }
        else if (text.Contains("雷兽") || text.Contains("电击")) { tags.Add("雷兽"); tags.Add("电击"); tags.Add("麻痹"); }
        else if (text.Contains("机械") && (text.Contains("蜈蚣") || text.Contains("静螳"))) { tags.Add("机械族"); tags.Add("重甲"); tags.Add("火力"); }

        // 过滤并返回
        return tags.Where(t => Constant.CoreMemoryTags.Contains(t)).ToList();
    }
}