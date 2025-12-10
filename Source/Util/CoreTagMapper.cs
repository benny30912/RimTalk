using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimWorld;
using Verse;

namespace RimTalk.Util;

public static class CoreTagMapper
{
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

                // --- 1.1 通用关键字匹配 (跨模组通用) ---
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
                if (defName.Contains("Deaf")) tags.Add("残疾");

                // --- 1.2 模组特定逻辑 (根据前缀分类) ---

                // [Vanilla Psycasts Expanded]
                if (defName.StartsWith("VPE"))
                {
                    tags.Add("灵能");
                    if (defName.Contains("MindControl") || defName.Contains("Puppet") || defName.Contains("Subjugation")) { tags.Add("控制"); tags.Add("奴役"); }
                    if (defName.Contains("Regrow") || defName.Contains("Regenerat") || defName.Contains("Heal")) { tags.Add("再生"); tags.Add("治疗"); }
                    if (defName.Contains("Invisibility") || defName.Contains("Obscured") || defName.Contains("Ghostwalk")) { tags.Add("隐形"); tags.Add("潜行"); }
                    if (defName.Contains("Hallucination")) { tags.Add("幻觉"); tags.Add("疯狂"); }
                    if (defName.Contains("Shield") || defName.Contains("Barrier")) { tags.Add("护盾"); tags.Add("防御"); }
                    if (defName.Contains("Age")) tags.Add("衰老");
                    if (defName.Contains("Lucky")) tags.Add(defName.Contains("UnLucky") ? "不幸" : "幸运");
                }
                // [Vanilla Races Expanded - Androids]
                else if (defName.StartsWith("VREA_"))
                {
                    tags.Add("机器人"); tags.Add("高科技");
                    if (defName.Contains("Memory") || defName.Contains("System") || defName.Contains("Processor")) tags.Add("数据");
                    if (defName.Contains("Reactor")) tags.Add("能量");
                    if (defName.Contains("Neutro")) { tags.Add("血液"); tags.Add("虚弱"); }
                    if (defName.Contains("Freezing") || defName.Contains("Overheating")) tags.Add("故障");
                    if (defName.Contains("Arm") || defName.Contains("Leg") || defName.Contains("Eye")) tags.Add("义肢");
                }
                // [Vanilla Races Expanded - Other Series]
                else if (defName.StartsWith("VRE_") || defName.StartsWith("VREH_") || defName.StartsWith("VRESaurids_"))
                {
                    tags.Add("基因");
                    if (defName.Contains("Hemogen") || defName.Contains("Sanguophage") || defName.Contains("Vampirism") || defName.Contains("Dracul")) { tags.Add("吸血"); tags.Add("血液"); }
                    if (defName.Contains("Insect") || defName.Contains("Jelly") || defName.Contains("Metapod")) { tags.Add("虫族"); tags.Add("变异"); }
                    if (defName.Contains("Pollution") || defName.Contains("Tox")) tags.Add("污染");
                    if (defName.Contains("Photosynthetic") || defName.Contains("Phytokin") || defName.Contains("Sapling")) { tags.Add("植物"); tags.Add("阳光"); }
                    if (defName.Contains("Lovin") || defName.Contains("Highmate")) { tags.Add("爱欲"); tags.Add("社交"); }
                    if (defName.Contains("Pregnancy") || defName.Contains("Spawn") || defName.Contains("Implantation")) tags.Add("繁殖");
                    if (defName.Contains("Deathrest") || defName.Contains("Dormant")) tags.Add("沉睡");
                }
                // [Alpha Implants / Animals]
                else if (defName.StartsWith("AI_"))
                {
                    tags.Add("动物"); tags.Add("强化");
                    if (defName.Contains("Bionic") || defName.Contains("Archotech")) { tags.Add("仿生"); tags.Add("超凡"); }
                    if (defName.Contains("Spitter") || defName.Contains("Breath") || defName.Contains("Spray") || defName.Contains("Gun") || defName.Contains("Cannon")) tags.Add("攻击");
                    if (defName.Contains("Wing") || defName.Contains("Gliding") || defName.Contains("Buoyancy")) { tags.Add("飞行"); tags.Add("滑翔"); }
                    if (defName.Contains("Stealth")) tags.Add("隐形");
                    if (defName.Contains("PseudoDeadlife")) { tags.Add("死灵"); tags.Add("复活"); }
                }
                // [Big and Small / Fantasy]
                else if (defName.StartsWith("BS_") || defName.StartsWith("LoS_") || defName.StartsWith("VU_"))
                {
                    if (defName.Contains("Giant") || defName.Contains("Colossal") || defName.Contains("Titan") || defName.Contains("Goliath")) { tags.Add("巨大"); tags.Add("强敌"); }
                    if (defName.Contains("Tiny") || defName.Contains("Small") || defName.Contains("Micro") || defName.Contains("Shrink")) { tags.Add("微小"); tags.Add("脆弱"); }
                    if (defName.Contains("Slime") || defName.Contains("Ooze")) tags.Add("粘液");
                    if (defName.Contains("Demon") || defName.Contains("Succubus")) { tags.Add("恶魔"); tags.Add("邪恶"); }
                    if (defName.Contains("Angel")) { tags.Add("天使"); tags.Add("神圣"); }
                    if (defName.Contains("Werewolf") || defName.Contains("Lycan")) { tags.Add("狼人"); tags.Add("变身"); }
                    if (defName.Contains("Undead") || defName.Contains("Skeletal") || defName.Contains("Lich")) { tags.Add("死灵"); tags.Add("骸骨"); }
                    if (defName.Contains("Soul")) tags.Add("灵魂");
                    if (defName.Contains("Engulfed")) tags.Add("吞食");
                    if (defName.Contains("Drunken")) tags.Add("醉酒");

                    if (defName.Contains("BATR_")) // Mechanical Chassis
                    {
                        tags.Add("机械");
                        if (defName.Contains("Bouncer")) { tags.Add("坚固"); tags.Add("笨重"); }
                        else if (defName.Contains("Roomba")) { tags.Add("清洁"); tags.Add("脆弱"); }
                        else if (defName.Contains("Jotun") || defName.Contains("Ogre")) { tags.Add("巨大"); tags.Add("笨重"); }
                        else if (defName.Contains("Mechanic")) { tags.Add("制作"); tags.Add("微小"); }
                    }
                }
                // [Vanilla Books Expanded]
                else if (defName.StartsWith("ABooks_"))
                {
                    tags.Add("书籍"); tags.Add("学习");
                    if (defName.Contains("Midas")) { tags.Add("黄金"); tags.Add("魔法"); }
                    if (defName.Contains("Lusty")) { tags.Add("爱欲"); tags.Add("开心"); }
                    // 技能映射
                    if (defName.Contains("Shooting")) tags.Add("射击");
                    if (defName.Contains("Melee")) tags.Add("格斗");
                    if (defName.Contains("Medical")) tags.Add("医疗");
                    if (defName.Contains("Cooking")) tags.Add("烹饪");
                    if (defName.Contains("Construction")) tags.Add("制作");
                    if (defName.Contains("Mining")) tags.Add("采矿");
                    if (defName.Contains("Plants")) tags.Add("农业");
                    if (defName.Contains("Animals")) tags.Add("驯兽");
                    if (defName.Contains("Social")) tags.Add("社交");
                    if (defName.Contains("Intellectual")) tags.Add("智识");
                    if (defName.Contains("Artistic")) tags.Add("艺术");
                }
                // [Dubs Bad Hygiene]
                else if (defName == "Washing") { tags.Add("清洁"); tags.Add("卫生"); }
                else if (defName == "BadHygiene" || defName == "Diarrhea") { tags.Add("肮脏"); tags.Add("生病"); }
                else if (defName == "DBHDehydration") { tags.Add("脱水"); tags.Add("生存"); }
                // [ReSplice]
                else if (defName.StartsWith("RS_"))
                {
                    if (defName.Contains("Thrall") || defName.Contains("Charm")) { tags.Add("奴役"); tags.Add("爱欲"); }
                    if (defName.Contains("Pregnancy") || defName.Contains("Impregnation")) tags.Add("怀孕");
                }
                // [Elite Powerups / Bosses]
                else if (defName.StartsWith("ElitePowerup_") || defName.Contains("Boss"))
                {
                    tags.Add("强敌"); tags.Add("精英"); tags.Add("危险");
                    if (defName.Contains("Shield")) tags.Add("护盾");
                    if (defName.Contains("Armor")) tags.Add("坚固");
                    if (defName.Contains("Toxic")) tags.Add("毒气");
                    if (defName.Contains("Temperature")) { tags.Add("寒冷"); tags.Add("过热"); }
                }
                // [Medieval Medicines / Potions]
                else if (defName.Contains("Draught") || defName.Contains("Elixir") || defName.Contains("Potion"))
                {
                    tags.Add("药水"); tags.Add("魔法");
                }
                // [Addictions & Drugs]
                else if (defName.Contains("Addiction") || defName.Contains("Tolerance") || defName.Contains("Withdrawal"))
                {
                    tags.Add("成瘾"); tags.Add("痛苦");
                }
                else if (defName.Contains("High")) // Drug high
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

            // --- Anomaly ---
            if (msName.Contains("DarkVisions")) { tags.Add("幻觉"); tags.Add("黑暗"); tags.Add("恐惧"); }
            else if (msName.Contains("EntityKiller")) { tags.Add("实体"); tags.Add("杀戮"); tags.Add("收容"); }
            else if (msName.Contains("EntityLiberator")) { tags.Add("实体"); tags.Add("释放"); tags.Add("背叛"); }
            else if (msName.Contains("InsaneRamblings")) { tags.Add("胡言乱语"); tags.Add("疯狂"); }
            else if (msName.Contains("TerrifyingHallucinations")) { tags.Add("幻觉"); tags.Add("恐惧"); tags.Add("怪物"); }
            else if (msName.Contains("CubeSculpting")) { tags.Add("魔方"); tags.Add("迷恋"); tags.Add("艺术"); }
            else if (msName.Contains("HumanityBreak")) { tags.Add("人性"); tags.Add("虚空"); tags.Add("冷漠"); }

            // --- Core / General ---
            else if (msName.Contains("Berserk"))
            {
                tags.Add("狂暴"); tags.Add("战斗");
                if (msName.Contains("Warcall")) tags.Add("呼唤");
                if (msName.Contains("Mechanoid") || msName.Contains("Core")) { tags.Add("机械"); tags.Add("故障"); }
            }
            else if (msName.Contains("Tantrum"))
            {
                tags.Add("发脾气"); tags.Add("破坏"); tags.Add("愤怒");
                if (msName.Contains("Technophobe")) tags.Add("科技");
            }
            else if (msName.Contains("Binging"))
            {
                tags.Add("放纵");
                if (msName.Contains("Drug")) tags.Add("成瘾");
                if (msName.Contains("Food")) tags.Add("饮食");
            }
            else if (msName.Contains("Manhunter") || msName.Contains("Hemohunter"))
            {
                tags.Add("猎杀"); tags.Add("狂暴");
                if (msName.Contains("Hemo")) { tags.Add("吸血"); tags.Add("血液"); }
            }
            else if (msName.Contains("PanicFlee") || msName.Contains("Flee"))
            {
                tags.Add("恐惧"); tags.Add("逃跑");
                if (msName.Contains("Fire")) tags.Add("火焰");
            }
            else if (msName.Contains("CorpseObsession")) { tags.Add("尸体"); tags.Add("迷恋"); tags.Add("诡异"); }
            else if (msName.Contains("FireStarting")) { tags.Add("纵火"); tags.Add("火焰"); tags.Add("破坏"); }
            else if (msName.Contains("GiveUp")) { tags.Add("放弃"); tags.Add("离开"); tags.Add("绝望"); }
            else if (msName.Contains("Insulting")) { tags.Add("侮辱"); tags.Add("愤怒"); tags.Add("争吵"); }
            else if (msName.Contains("Jailbreaker")) { tags.Add("越狱"); tags.Add("监狱"); tags.Add("反抗"); }
            else if (msName.Contains("Murderous")) { tags.Add("杀戮"); tags.Add("行凶"); tags.Add("愤怒"); }
            else if (msName.Contains("Sadistic")) { tags.Add("施虐"); tags.Add("折磨"); tags.Add("痛苦"); }
            else if (msName.Contains("Slaughterer")) { tags.Add("屠夫"); tags.Add("动物"); tags.Add("杀戮"); }

            // --- Ideology ---
            else if (msName.Contains("IdeoChange") || msName.Contains("Crisis")) { tags.Add("信仰"); tags.Add("怀疑"); tags.Add("迷茫"); }
            else if (msName.Contains("Rebellion")) { tags.Add("反抗"); tags.Add("奴役"); tags.Add("自由"); }

            // --- Mods Specific ---
            else if (msName.Contains("Kleptomaniac")) { tags.Add("盗窃"); tags.Add("欲望"); }
            else if (msName.Contains("Photosensitive") || msName.Contains("Photophobia")) { tags.Add("光"); tags.Add("痛苦"); tags.Add("恐惧"); }
            else if (msName.Contains("Thalassophobia")) { tags.Add("水"); tags.Add("恐惧"); tags.Add("逃跑"); }
            else if (msName.Contains("Claustrophobic")) { tags.Add("狭窄"); tags.Add("恐惧"); }
            else if (msName.Contains("Narcoleptic")) { tags.Add("睡眠"); tags.Add("昏迷"); }
            else if (msName.Contains("SolarFlared")) { tags.Add("太阳耀斑"); tags.Add("故障"); }

            // --- Wandering ---
            else if (msName.Contains("Wander"))
            {
                tags.Add("徘徊");
                if (msName.Contains("Sad")) tags.Add("悲伤");
                else if (msName.Contains("Psychotic")) { tags.Add("疯狂"); tags.Add("迷茫"); }
                else if (msName.Contains("Confused")) tags.Add("迷茫");
                else if (msName.Contains("OwnRoom")) tags.Add("孤独");
            }
            else if (msName.Contains("Roaming")) tags.Add("闲逛");
        }

        // =========================================================
        // 3. 想法与记忆分析 (Thoughts)
        // =========================================================
        if (p.needs?.mood?.thoughts != null)
        {
            var thoughts = new List<Thought>();
            p.needs.mood.thoughts.GetAllMoodThoughts(thoughts);

            foreach (var thought in thoughts)
            {
                string defName = thought.def.defName;

                if (defName == "NeedPyromania")
                {
                    tags.Add("纵火"); tags.Add("火焰");
                    if (thought.CurStage.label == "熄灭" || thought.CurStage.label == "急需") { tags.Add("渴望"); tags.Add("焦虑"); }
                    else { tags.Add("温暖"); tags.Add("开心"); }
                }
                else if (defName.StartsWith("VME_Slavery_Forbidden") || defName.Contains("Slave"))
                {
                    if (defName.Contains("Bought")) tags.Add("交易");
                    if (defName.Contains("Charity") || (thought.CurStage?.description?.Contains("救下") ?? false)) { tags.Add("正义"); tags.Add("自由"); }
                    else if (defName.Contains("HasBeenASlave")) { tags.Add("创伤"); tags.Add("厌恶"); }
                    else { tags.Add("奴役"); tags.Add("压迫"); }
                }
                else if (defName.Contains("OrganizedWorkspace")) { tags.Add("工作"); tags.Add("效率"); }
                else if (defName.StartsWith("Inhumanizing")) { tags.Add("人性"); if (defName.Contains("HumanShame")) { tags.Add("耻辱"); tags.Add("虚空"); } }
                else if (defName.StartsWith("VME_"))
                {
                    if (defName.Contains("Corporate")) { tags.Add("公司"); tags.Add("工作"); }
                    if (defName.Contains("Eldritch")) { tags.Add("诡异"); tags.Add("疯狂"); }
                    if (defName.Contains("Flesh")) { tags.Add("血肉"); tags.Add("变异"); }
                    if (defName.Contains("Junk")) { tags.Add("垃圾"); tags.Add("生存"); }
                    if (defName.Contains("Sweet")) { tags.Add("甜食"); tags.Add("开心"); }
                    if (defName.Contains("Anima")) { tags.Add("仙树"); tags.Add("自然"); }
                    if (defName.Contains("Vision")) { tags.Add("神圣"); tags.Add("奇迹"); }
                }
                else if (defName.StartsWith("IdeoDiverity_"))
                {
                    if (defName.Contains("Abhorrent") || defName.Contains("Disapproved")) { tags.Add("异端"); tags.Add("厌恶"); }
                }
                else if (defName.StartsWith("AG_"))
                {
                    tags.Add("基因");
                    if (defName.Contains("Eldritch")) tags.Add("恐怖");
                    if (defName.Contains("Depression")) tags.Add("悲伤");
                    if (defName.Contains("Unstable")) tags.Add("变异");
                }
            }
        }

        // =========================================================
        // 4. 当前工作 (Jobs)
        // =========================================================
        if (p.CurJob != null)
        {
            string jobName = p.CurJob.def.defName;

            if (jobName.Contains("Attack") || jobName.Contains("Hunt") || jobName.Contains("ManTurret")) tags.Add("战斗");
            if (jobName.Contains("Capture") || jobName.Contains("Arrest") || jobName.Contains("EscortPrisoner")) { tags.Add("监狱"); tags.Add("抓捕"); }
            if (jobName.Contains("Execution") || jobName.Contains("Slaughter")) { tags.Add("处决"); tags.Add("死亡"); }
            if (jobName.Contains("Flee")) { tags.Add("逃跑"); tags.Add("恐惧"); }
            if (jobName.Contains("Tend") || jobName.Contains("Treat") || jobName.Contains("Operate")) tags.Add("医疗");
            if (jobName.Contains("Rescue") || jobName.Contains("CarryToBed")) tags.Add("救援");
            if (jobName.Contains("Feed") || jobName.Contains("DeliverFood")) tags.Add("喂食");
            if (jobName.Contains("VisitSick") || jobName.Contains("CheerUp")) { tags.Add("探望"); tags.Add("友谊"); }
            if (jobName.Contains("Tame") || jobName.Contains("Train")) { tags.Add("驯兽"); tags.Add("动物"); }
            if (jobName.Contains("Shear") || jobName.Contains("Milk")) { tags.Add("畜牧"); tags.Add("动物"); }
            if (jobName.Contains("Sow") || jobName.Contains("Harvest") || jobName.Contains("CutPlant")) { tags.Add("农业"); tags.Add("植物"); }
            if (jobName.Contains("ChopWood")) tags.Add("伐木");
            if (jobName.Contains("Mine") || jobName.Contains("Drill")) { tags.Add("采矿"); tags.Add("钻井"); }
            if (jobName.Contains("Construct") || jobName.Contains("Build") || jobName.Contains("Smooth")) tags.Add("建造");
            if (jobName.Contains("Repair") || jobName.Contains("Fix")) tags.Add("修理");
            if (jobName.Contains("DoBill") || jobName.Contains("Make") || jobName.Contains("Fabricate")) tags.Add("制作");
            if (jobName.Contains("Research") || jobName.Contains("Study") || jobName.Contains("Analyze")) { tags.Add("研究"); tags.Add("智识"); }
            if (jobName.Contains("Clean") || jobName.Contains("Clear")) tags.Add("清洁");
            if (jobName.Contains("Haul") || jobName.Contains("Carry") || jobName.Contains("Deliver")) tags.Add("搬运");
            if (jobName.Contains("Deconstruct") || jobName.Contains("Uninstall")) tags.Add("拆除");
            if (jobName.Contains("Lovin")) { tags.Add("爱欲"); tags.Add("开心"); }
            if (jobName.Contains("Play") || jobName.Contains("Relax") || jobName.Contains("Watch") || jobName.Contains("View")) { tags.Add("玩耍"); tags.Add("娱乐"); }
            if (jobName.Contains("Meditate") || jobName.Contains("Pray")) { tags.Add("冥想"); tags.Add("信仰"); }
            if (jobName.Contains("Ingest") || jobName.Contains("Eat") || jobName.Contains("Drink")) tags.Add("饮食");
            if (jobName.Contains("Sleep") || jobName.Contains("LayDown")) tags.Add("睡眠");
            if (jobName.Contains("Wear") || jobName.Contains("Equip")) tags.Add("穿戴");
            if (jobName.Contains("Monolith") || jobName.Contains("Void") || jobName.Contains("Strange")) { tags.Add("异象"); tags.Add("诡异"); }
            if (jobName.Contains("EntityHolder") || jobName.Contains("HoldingPlatform")) { tags.Add("收容"); tags.Add("实体"); }
            if (jobName.Contains("Ritual")) { tags.Add("仪式"); tags.Add("信仰"); }
            if (jobName.Contains("Extract") || jobName.Contains("HarvestBioferrite")) { tags.Add("抽取"); tags.Add("工作"); }
            if (jobName.Contains("Activate")) tags.Add("激活");
            if (jobName.StartsWith("PE_"))
            {
                if (jobName.Contains("Class")) tags.Add("上课");
                if (jobName.Contains("Shooting")) tags.Add("射击");
                if (jobName.Contains("Melee")) tags.Add("格斗");
                if (jobName.Contains("Bell")) tags.Add("敲钟");
            }
        }

        // ==========================================
        // 5. 身份与需求 (Needs & Identity)
        // ==========================================

        if (p.IsPrisoner) tags.Add("监狱");
        if (p.IsSlave) tags.Add("奴役");

        if (p.needs != null)
        {
            var allNeeds = p.needs.AllNeeds;
            foreach (var need in allNeeds)
            {
                string defName = need.def.defName;
                float level = need.CurLevel;

                if (defName == "Food" && need.CurLevelPercentage <= 0.01f) { tags.Add("饥饿"); tags.Add("生存"); }
                else if (defName == "Rest" && need.CurLevelPercentage <= 0.05f) { tags.Add("疲劳"); tags.Add("睡眠"); }
                else if (defName == "Joy" && need.CurLevelPercentage <= 0.1f) { tags.Add("无聊"); tags.Add("娱乐"); }
                else if (defName == "Beauty") { if (level < 0.1f) tags.Add("厌恶"); else if (level > 0.8f) { tags.Add("美观"); tags.Add("艺术"); } }
                else if (defName == "Comfort") { if (level < 0.1f) tags.Add("痛苦"); else if (level > 0.8f) tags.Add("舒适"); }
                else if (defName == "Outdoors" && level < 0.1f) { tags.Add("被困"); tags.Add("自由"); }
                else if (defName == "Indoors" && level < 0.1f) { tags.Add("被困"); tags.Add("安全"); }
                else if (defName == "RoomSize" && level < 0.1f) { tags.Add("狭窄"); tags.Add("压迫"); }
                else if (defName.Contains("DrugDesire") || defName.Contains("Chemical")) { if (level < 0.1f) { tags.Add("渴望"); tags.Add("成瘾"); tags.Add("焦急"); } }

                // Mod/DLC Needs
                else if (defName == "Deathrest" && level < 0.1f) { tags.Add("死眠"); tags.Add("虚弱"); }
                else if (defName == "KillThirst" && level < 0.1f) { tags.Add("杀戮"); tags.Add("愤怒"); }
                else if (defName == "Learning") { if (level < 0.1f) tags.Add("无聊"); else if (level > 0.8f) tags.Add("学习"); }
                else if (defName == "Play" && level < 0.1f) { tags.Add("无聊"); tags.Add("悲伤"); }
                else if (defName == "MechEnergy" && level < 0.1f) { tags.Add("能量"); tags.Add("休眠"); }
                else if (defName == "Suppression" && level > 0.8f) { tags.Add("压制"); tags.Add("恐惧"); }
                else if (defName == "Bladder" && level < 0.2f) { tags.Add("排泄"); tags.Add("焦急"); }
                else if (defName == "Hygiene" && level < 0.2f) { tags.Add("肮脏"); tags.Add("生病"); }
                else if (defName == "DBHThirst" && level < 0.1f) { tags.Add("口渴"); tags.Add("生存"); }
                else if (defName == "VME_Desserts" && level < 0.1f) { tags.Add("甜食"); tags.Add("渴望"); }
                else if (defName == "VME_Anonymity") { tags.Add("匿名"); tags.Add("隐私"); }
                else if (defName == "VME_Corruption") { tags.Add("堕落"); tags.Add("信仰"); }
                else if (defName == "VRE_Lovin" && level < 0.1f) { tags.Add("爱欲"); tags.Add("焦急"); }
                else if (defName == "VRE_Pollution" && level < 0.1f) tags.Add("污染");
                else if (defName == "VREA_MemorySpace" && level < 0.1f) { tags.Add("数据"); tags.Add("故障"); }
                else if (defName == "VREA_ReactorPower" && level < 0.1f) { tags.Add("能量"); tags.Add("死亡"); }
                else if (defName.Contains("Romance") && level < 0.2f) { tags.Add("孤独"); tags.Add("浪漫"); }
            }
        }

        // ==========================================
        // 6. 灵感状态分析 (Inspirations)
        // ==========================================
        // 修正：使用正确的属性访问路径
        if (p.mindState?.inspirationHandler?.CurState != null)
        {
            var insp = p.mindState.inspirationHandler.CurState.def;
            string defName = insp.defName;

            tags.Add("灵感");
            if (defName.Contains("Frenzy")) tags.Add("狂热");

            if (defName.Contains("Go")) { tags.Add("速度"); tags.Add("移动"); }
            else if (defName.Contains("Shoot")) { tags.Add("射击"); tags.Add("精准"); }
            else if (defName.Contains("Work")) { tags.Add("工作"); tags.Add("效率"); }
            else if (defName.Contains("Creativity") || defName.Contains("Art")) { tags.Add("艺术"); tags.Add("创作"); }
            else if (defName.Contains("Recruitment")) { tags.Add("招募"); tags.Add("社交"); }
            else if (defName.Contains("Surgery")) { tags.Add("手术"); tags.Add("医疗"); }
            else if (defName.Contains("Taming")) { tags.Add("驯兽"); tags.Add("动物"); }
            else if (defName.Contains("Trade")) { tags.Add("交易"); tags.Add("商贸"); }
            else if (defName.Contains("Arsonism")) { tags.Add("纵火"); tags.Add("火焰"); }
            else if (defName.Contains("Bloodlust") || defName.Contains("Fighting") || defName.Contains("Brawl")) { tags.Add("战斗"); tags.Add("嗜血"); }
            else if (defName.Contains("Psychic"))
            {
                tags.Add("心灵");
                if (defName.Contains("Soothe")) tags.Add("安抚");
                if (defName.Contains("Strength")) tags.Add("冥想");
            }
            else if (defName.Contains("ChildGrowth") || defName.Contains("Learning")) { tags.Add("成长"); tags.Add("学习"); }
            else if (defName.Contains("Cooking")) { tags.Add("烹饪"); tags.Add("美食"); }
            else if (defName.Contains("Foraging")) { tags.Add("采集"); tags.Add("植物"); }
            else if (defName.Contains("Hunting")) { tags.Add("狩猎"); tags.Add("射击"); }
            else if (defName.Contains("Kindness")) { tags.Add("友善"); tags.Add("社交"); }
            else if (defName.Contains("Leadership")) { tags.Add("领袖"); tags.Add("激励"); }
            else if (defName.Contains("Mining")) { tags.Add("采矿"); tags.Add("工作"); }
            else if (defName.Contains("Plant") || defName.Contains("Farming")) { tags.Add("种植"); tags.Add("农业"); }
            else if (defName.Contains("Ranching")) { tags.Add("畜牧"); tags.Add("动物"); }
            else if (defName.Contains("Research")) { tags.Add("研究"); tags.Add("智识"); }
            else if (defName.Contains("Travelling")) { tags.Add("旅行"); tags.Add("远行"); }
            else if (defName.Contains("Flirting")) { tags.Add("调情"); tags.Add("浪漫"); tags.Add("爱欲"); }
        }

        // ==========================================
        // 7. 关系状态分析 (Relationships)
        // ==========================================
        if (p.relations != null)
        {
            foreach (var relation in p.relations.DirectRelations)
            {
                if (relation.otherPawn == null || relation.otherPawn.Map != p.Map) continue;

                string relName = relation.def.defName;

                if (relName == "Parent" || relName == "ParentBirth") { tags.Add(relation.otherPawn.gender == Gender.Female ? "母亲" : "父亲"); }
                else if (relName == "Child") { tags.Add(relation.otherPawn.gender == Gender.Female ? "女儿" : "儿子"); }
                else if (relName == "Sibling" || relName == "HalfSibling") { tags.Add(relation.otherPawn.gender == Gender.Female ? "姐妹" : "兄弟"); }
                else if (relName.Contains("Grandparent")) tags.Add("祖父母");
                else if (relName.Contains("Grandchild")) tags.Add("孙子女");
                else if (relName.Contains("Uncle") || relName.Contains("Aunt")) { tags.Add(relation.otherPawn.gender == Gender.Female ? "姑姨" : "叔舅"); }
                else if (relName.Contains("Nephew") || relName.Contains("Niece")) { tags.Add(relation.otherPawn.gender == Gender.Female ? "侄女" : "侄子"); }
                else if (relName.Contains("Cousin")) tags.Add("堂表亲");
                else if (relName.Contains("Kin")) tags.Add("亲戚");
                else if (relName == "Spouse" || relName.Contains("CoSpouse")) { tags.Add(relation.otherPawn.gender == Gender.Female ? "妻子" : "丈夫"); tags.Add("爱情"); }
                else if (relName == "Fiance") { tags.Add("未婚妻"); tags.Add("爱情"); }
                else if (relName == "Lover") { tags.Add("情人"); tags.Add("爱情"); }
                else if (relName == "ExLover" || relName == "ExSpouse") { tags.Add("前任"); tags.Add("回忆"); }
                else if (relName == "ParentInLaw") tags.Add("岳父母");
                else if (relName == "ChildInLaw") { tags.Add(relation.otherPawn.gender == Gender.Female ? "儿媳" : "女婿"); }
                else if (relName.Contains("Step"))
                {
                    if (relName.Contains("Parent")) tags.Add(relation.otherPawn.gender == Gender.Female ? "继母" : "继父");
                    else tags.Add("继子");
                }
                else if (relName == "Bond") { tags.Add("牵绊"); tags.Add("动物"); }
                else if (relName == "Overseer") { tags.Add("监管者"); tags.Add("机械"); }
                else if (relName == "VSIE_BestFriend") { tags.Add("密友"); tags.Add("友谊"); }
                else if (relName == "VRE_PackMember") { tags.Add("族群"); tags.Add("狼人"); }
                else if (relName == "RS_Master") { tags.Add("主人"); tags.Add("奴役"); }
                else if (relName == "RS_Thrall") { tags.Add("奴仆"); tags.Add("爱欲"); }
                else if (relName == "BS_Creator") { tags.Add("创造者"); tags.Add("机械"); }
            }
        }

        // 过滤并返回
        return tags.Where(t => Constant.CoreMemoryTags.Contains(t)).ToList();
    }

    /// <summary>
    /// 根据 RimTalk 传入的互动标签文本或 HistoryEventDef.label 提取核心标签。
    /// </summary>
    public static List<string> GetEventTags(string text)
    {
        var tags = new HashSet<string>();
        if (string.IsNullOrEmpty(text)) return [];

        // --- Anomaly (异象) ---
        if (text.Contains("阴森之语") || text.Contains("令人不安")) { tags.Add("诡异"); tags.Add("疯狂"); tags.Add("恐惧"); }
        else if (text.Contains("奇怪的闲聊")) { tags.Add("诡异"); tags.Add("八卦"); }
        else if (text.Contains("非人胡言乱语")) { tags.Add("非人"); tags.Add("疯狂"); tags.Add("混乱"); }
        else if (text.Contains("变异尖叫") || text.Contains("尖叫")) { tags.Add("尖叫"); tags.Add("恐怖"); tags.Add("变异"); }
        else if (text.Contains("洗脑")) { tags.Add("洗脑"); tags.Add("控制"); tags.Add("仪式"); }
        else if (text.Contains("讲道") || text.Contains("布道")) { tags.Add("讲道"); tags.Add("信仰"); }
        else if (text.Contains("突变") || text.Contains("转化")) { tags.Add("变异"); tags.Add("怪异"); }
        else if (text.Contains("虚空") || text.Contains("巨石")) { tags.Add("虚空"); tags.Add("恐惧"); }
        else if (text.Contains("食尸鬼")) { tags.Add("食尸鬼"); tags.Add("恐怖"); }
        else if (text.Contains("仪式") || text.Contains("典礼")) { tags.Add("仪式"); tags.Add("心灵"); }

        // --- Ideology (文化与戒律) ---
        if (text.Contains("审判")) { tags.Add("审判"); tags.Add("正义"); }
        if (text.Contains("指控")) tags.Add("指控");
        if (text.Contains("辩护")) tags.Add("辩护");
        if (text.Contains("定罪")) tags.Add("定罪");
        if (text.Contains("演讲")) { tags.Add("演讲"); tags.Add("领袖"); tags.Add("责任"); tags.Add("仪式"); }
        if (text.Contains("吃人") || text.Contains("人肉")) { tags.Add("吃人"); tags.Add("尸体"); tags.Add("饮食"); }
        if (text.Contains("处决")) { tags.Add("处决"); tags.Add("死亡"); tags.Add("正义"); }
        if (text.Contains("奴役") || text.Contains("贩卖奴隶")) { tags.Add("奴役"); tags.Add("交易"); }
        if (text.Contains("器官") && (text.Contains("摘取") || text.Contains("贩卖"))) { tags.Add("器官"); tags.Add("手术"); tags.Add("残忍"); }
        if (text.Contains("慈善") || text.Contains("帮助")) { tags.Add("慈善"); tags.Add("友善"); }
        if (text.Contains("拒绝") || text.Contains("背叛")) tags.Add("冷漠");
        if (text.Contains("致盲")) { tags.Add("致盲"); tags.Add("仪式"); tags.Add("痛苦"); }
        if (text.Contains("纹身") || text.Contains("划伤")) { tags.Add("纹身"); tags.Add("仪式"); tags.Add("痛苦"); }
        if (text.Contains("树") && (text.Contains("修剪") || text.Contains("连接"))) { tags.Add("树木"); tags.Add("自然"); tags.Add("连接"); }
        if (text.Contains("领袖") || text.Contains("角色")) { tags.Add("领袖"); tags.Add("责任"); }
        if (text.Contains("教化") || text.Contains("转化")) { tags.Add("信仰"); tags.Add("劝说"); }
        if (text.Contains("圣物")) { tags.Add("圣物"); tags.Add("搜寻"); tags.Add("信仰"); }

        // --- Biotech (生物科技) ---
        if (text.Contains("胚芽") || text.Contains("基因")) { tags.Add("基因"); tags.Add("进化"); }
        if (text.Contains("血原质") || text.Contains("汲血")) { tags.Add("吸血"); tags.Add("血液"); }
        if (text.Contains("污染") || text.Contains("废料")) { tags.Add("污染"); tags.Add("毒气"); }
        if (text.Contains("机械") && (text.Contains("孕育") || text.Contains("复活"))) { tags.Add("机械"); tags.Add("制造"); }

        // --- Diplomacy & Combat (外交与战斗) ---
        if (text.Contains("和平") || text.Contains("谈判")) { tags.Add("和平"); tags.Add("外交"); tags.Add("谈判"); }
        if (text.Contains("礼物")) { tags.Add("礼物"); tags.Add("友善"); }
        if (text.Contains("交易") || text.Contains("贸易")) { tags.Add("交易"); tags.Add("商贸"); }
        if (text.Contains("袭击") || text.Contains("攻击")) { tags.Add("袭击"); tags.Add("战斗"); }
        if (text.Contains("战败") || text.Contains("失败")) { tags.Add("战败"); tags.Add("耻辱"); tags.Add("悲伤"); }
        if (text.Contains("胜利") || text.Contains("赢")) { tags.Add("胜利"); tags.Add("荣耀"); tags.Add("开心"); }
        if (text.Contains("逃兵")) { tags.Add("逃兵"); tags.Add("背叛"); tags.Add("收留"); }

        // --- Misc Activities (杂项活动) ---
        if (text.Contains("结婚") || text.Contains("婚礼") || text.Contains("配偶")) { tags.Add("婚礼"); tags.Add("爱情"); tags.Add("家庭"); }
        if (text.Contains("浪漫") || text.Contains("求爱") || text.Contains("滚床单")) { tags.Add("浪漫"); tags.Add("爱欲"); }
        if (text.Contains("研究")) { tags.Add("研究"); tags.Add("智识"); }
        if (text.Contains("采矿")) { tags.Add("采矿"); tags.Add("工作"); }
        if (text.Contains("种植") || text.Contains("收获")) { tags.Add("种植"); tags.Add("农业"); }
        if (text.Contains("驯服") || text.Contains("训练")) { tags.Add("驯兽"); tags.Add("动物"); }
        if (text.Contains("治疗") || text.Contains("手术")) { tags.Add("治疗"); tags.Add("医疗"); }
        if (text.Contains("拘捕") || text.Contains("抓捕")) { tags.Add("抓捕"); tags.Add("监狱"); }

        // --- Mod Specifics (模组特性) ---
        if (text.Contains("炮击")) { tags.Add("炮击"); tags.Add("战争"); } // VFE-Security
        if (text.Contains("渔猎") || text.Contains("宰鱼")) { tags.Add("渔猎"); tags.Add("食物"); } // VAE-Fishing
        if (text.Contains("书") || text.Contains("阅读") || text.Contains("书写")) { tags.Add("阅读"); tags.Add("书写"); tags.Add("智识"); } // VBE
        if (text.Contains("甜点") || text.Contains("巧克力")) { tags.Add("甜食"); tags.Add("开心"); } // VCE
        if (text.Contains("虫胶") || text.Contains("虫族")) { tags.Add("虫族"); tags.Add("虫胶"); } // VFE-Insectoids
        if (text.Contains("灭火")) { tags.Add("灭火"); tags.Add("火灾"); }
        if (text.Contains("预言") || text.Contains("灵质")) { tags.Add("预言"); tags.Add("灵质"); tags.Add("灵能"); } // VPE
        if (text.Contains("孤儿") || text.Contains("儿童") || text.Contains("领养")) { tags.Add("孤儿"); tags.Add("领养"); tags.Add("家庭"); }
        if (text.Contains("贷款") || text.Contains("债务")) { tags.Add("债务"); tags.Add("交易"); } // VTE

        // --- Social Interactions (Vanilla Social Interactions Expanded & Base) ---
        if (text.Contains("八卦")) tags.Add("八卦");
        if (text.Contains("秘密")) { tags.Add("秘密"); tags.Add("信任"); }
        if (text.Contains("玩笑") || text.Contains("笑话")) { tags.Add("玩笑"); tags.Add("开心"); }
        if (text.Contains("性话题") || text.Contains("荤段子")) { tags.Add("性话题"); tags.Add("爱欲"); }
        if (text.Contains("调情")) { tags.Add("调情"); tags.Add("爱欲"); }
        if (text.Contains("羞辱") || text.Contains("侮辱")) { tags.Add("羞辱"); tags.Add("愤怒"); }
        if (text.Contains("争辩") || text.Contains("吵架")) { tags.Add("争辩"); tags.Add("愤怒"); }
        if (text.Contains("发泄")) { tags.Add("发泄"); tags.Add("压力"); }
        if (text.Contains("安慰")) { tags.Add("安慰"); tags.Add("悲伤"); }
        if (text.Contains("鼓励")) { tags.Add("鼓励"); tags.Add("希望"); }
        if (text.Contains("背叛")) { tags.Add("背叛"); tags.Add("愤怒"); }
        if (text.Contains("谈论工作")) tags.Add("工作");
        if (text.Contains("开始对话") || text.Contains("边缘世谭")) { tags.Add("交流"); tags.Add("闲聊"); }

        // ==========================================
        // Game Conditions (天气、灾害与环境状态)
        // ==========================================

        // --- Anomaly (异象DLC) ---
        if (text.Contains("血怒之雨") || text.Contains("鲜血")) { tags.Add("血雨"); tags.Add("狂暴"); tags.Add("愤怒"); }
        else if (text.Contains("死灵迷雾") || text.Contains("死灵尘")) { tags.Add("死灵"); tags.Add("复活"); tags.Add("迷雾"); tags.Add("尸体"); tags.Add("诡异"); }
        else if (text.Contains("灰棘迷雾")) { tags.Add("迷雾"); tags.Add("腐烂"); tags.Add("恶臭"); tags.Add("恐惧"); }
        else if (text.Contains("恶咒吟诵")) { tags.Add("吟诵"); tags.Add("怨恨"); tags.Add("诅咒"); tags.Add("邪恶"); }
        else if (text.Contains("异常黑暗")) { tags.Add("黑暗"); tags.Add("恐怖"); tags.Add("怪物"); }
        else if (text.Contains("异常高温")) { tags.Add("高温"); tags.Add("热浪"); tags.Add("危险"); }
        else if (text.Contains("死灵末日")) { tags.Add("死灵"); tags.Add("末日"); tags.Add("尸体"); tags.Add("绝望"); }

        // --- Core / Biotech / Royalty (核心/生物/皇权) ---
        else if (text.Contains("酸雾")) { tags.Add("酸雾"); tags.Add("污染"); tags.Add("腐蚀"); }
        else if (text.Contains("心灵干扰")) { tags.Add("心灵"); tags.Add("干扰"); tags.Add("疯狂"); }
        else if (text.Contains("心灵低语")) { tags.Add("心灵"); tags.Add("焦虑"); tags.Add("干扰"); }
        else if (text.Contains("心灵抚慰")) { tags.Add("心灵"); tags.Add("平静"); tags.Add("开心"); }
        else if (text.Contains("心灵抑制")) { tags.Add("心灵"); tags.Add("抑制"); tags.Add("虚弱"); }
        else if (text.Contains("极光")) { tags.Add("极光"); tags.Add("美丽"); tags.Add("开心"); }
        else if (text.Contains("日蚀")) { tags.Add("日蚀"); tags.Add("黑暗"); }
        else if (text.Contains("黑幕")) { tags.Add("黑暗"); tags.Add("阻挡"); }
        else if (text.Contains("寒流") || text.Contains("极度深寒")) { tags.Add("寒流"); tags.Add("寒冷"); tags.Add("低温"); tags.Add("生存"); }
        else if (text.Contains("热浪")) { tags.Add("热浪"); tags.Add("高温"); tags.Add("中暑"); tags.Add("生存"); }
        else if (text.Contains("闪电风暴")) { tags.Add("闪电"); tags.Add("风暴"); tags.Add("火灾"); }
        else if (text.Contains("太阳耀斑")) { tags.Add("耀斑"); tags.Add("故障"); tags.Add("停电"); }
        else if (text.Contains("有毒尘埃") || text.Contains("空气污染")) { tags.Add("有毒"); tags.Add("污染"); tags.Add("躲避"); tags.Add("生存"); }
        else if (text.Contains("火山冬天") || text.Contains("火山灰")) { tags.Add("火山"); tags.Add("寒冷"); tags.Add("灰烬"); tags.Add("暗淡"); }
        else if (text.Contains("火山碎屑") || text.Contains("熔岩")) { tags.Add("火山"); tags.Add("熔岩"); tags.Add("火焰"); tags.Add("危险"); }
        else if (text.Contains("歼星武器")) { tags.Add("末日"); tags.Add("绝望"); tags.Add("毁灭"); }
        else if (text.Contains("电磁干扰")) { tags.Add("电磁"); tags.Add("故障"); }
        else if (text.Contains("巨大烟云")) { tags.Add("烟雾"); tags.Add("黑暗"); }
        else if (text.Contains("天气控制")) { tags.Add("天气"); tags.Add("控制"); }
        else if (text.Contains("气候调整")) { tags.Add("气温"); }

        // --- Vanilla Expanded / Other Mods (模组扩展) ---
        else if (text.Contains("萤光孢子")) { tags.Add("孢子"); tags.Add("美丽"); tags.Add("光芒"); }
        else if (text.Contains("暗沉的天空")) { tags.Add("黑暗"); tags.Add("乌云"); }
        else if (text.Contains("干旱") || text.Contains("旱情")) { tags.Add("干旱"); tags.Add("缺水"); tags.Add("生存"); }
        else if (text.Contains("鳃腐病")) { tags.Add("瘟疫"); tags.Add("生病"); }
        else if (text.Contains("热气排放")) { tags.Add("高温"); }
        else if (text.Contains("多风")) { tags.Add("大风"); }
        else if (text.Contains("辐射尘埃")) { tags.Add("辐射"); tags.Add("污染"); tags.Add("危险"); tags.Add("躲避"); }
        else if (text.Contains("纳米机械日蚀")) { tags.Add("黑暗"); tags.Add("机械"); }
        else if (text.Contains("全球变暖")) { tags.Add("高温"); tags.Add("灾难"); }
        else if (text.Contains("冰河时代")) { tags.Add("寒冷"); tags.Add("灾难"); }
        else if (text.Contains("无尽长夜")) { tags.Add("黑暗"); tags.Add("漫长"); tags.Add("绝望"); }
        else if (text.Contains("心灵怒放")) { tags.Add("花朵"); tags.Add("美丽"); tags.Add("心灵"); }
        else if (text.Contains("心灵雾雨")) { tags.Add("雨"); tags.Add("衰老"); tags.Add("心灵"); }
        else if (text.Contains("太空战")) { tags.Add("太空"); tags.Add("战斗"); tags.Add("坠落"); tags.Add("危险"); }
        else if (text.Contains("流星风暴")) { tags.Add("流星"); tags.Add("轰炸"); tags.Add("危险"); }
        else if (text.Contains("轨道轰炸")) { tags.Add("轰炸"); tags.Add("爆炸"); tags.Add("战争"); }
        else if (text.Contains("骤雨飓风")) { tags.Add("飓风"); tags.Add("风暴"); tags.Add("雨"); }
        else if (text.Contains("剧化黑暗")) { tags.Add("黑暗"); }
        else if (text.Contains("袭击止步")) { tags.Add("停止"); tags.Add("和平"); }
        else if (text.Contains("时空震颤")) { tags.Add("时空"); tags.Add("震动"); }
        else if (text.Contains("心灵风暴")) { tags.Add("心灵"); tags.Add("风暴"); tags.Add("闪电"); }
        else if (text.Contains("血月")) { tags.Add("血月"); tags.Add("疯狂"); tags.Add("吸血"); }
        else if (text.Contains("祝福")) { tags.Add("祝福"); tags.Add("奇迹"); tags.Add("开心"); }
        else if (text.Contains("改造机械体")) { tags.Add("机械"); tags.Add("强化"); }
        else if (text.Contains("远古储备地库")) { tags.Add("地库"); tags.Add("黑暗"); }

        // 过滤并返回
        return tags.Where(t => Constant.CoreMemoryTags.Contains(t)).ToList();
    }
}