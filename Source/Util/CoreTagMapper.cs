using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimTalk.Util;

public static class CoreTagMapper
{
    // ★ 優化：靜態緩存，避免每次都去資料庫查
    private static ThoughtDef _observedLayingCorpseDef;
    public static List<string> GetAbstractTags(Pawn p)
    {
        var tags = new HashSet<string>();

        if (p == null) return [];

        // ==========================================
        // 1. 精神狀態 (Mental State)
        // ==========================================
        if (p.InMentalState)
        {
            var ms = p.MentalStateDef;
            string msName = ms.defName; // 使用字串名稱比對，避免 DefOf 缺失報錯

            tags.Add("崩溃");

            if (ms.IsAggro)
            {
                tags.Add("愤怒");
                tags.Add("战斗");
                tags.Add("仇恨");
            }
            // 修正：使用字串比對解決 DefOf 缺失問題
            else if (msName == "GivenUpExit" || msName == "PanicFlee" || msName == "Wander_Psychotic")
            {
                tags.Add("恐惧");
                tags.Add("无助");
                tags.Add("逃跑");
            }
            // 修正：SadWander 在 DefOf 中通常叫 Wander_Sad
            else if (msName == "Wander_Sad" || msName == "SadWander" || msName.Contains("Sad"))
            {
                tags.Add("悲伤");
                tags.Add("孤独");
            }
            // 修正：使用字串比對 Binging
            else if (msName.Contains("Binging"))
            {
                tags.Add("焦虑");
                tags.Add("成瘾");
            }
            else if (msName.Contains("Confused") || msName == "Wander_OwnRoom")
            {
                tags.Add("无助");
                tags.Add("焦虑");
            }
        }

        // ==========================================
        // 2. 心情與需求 (Mood & Needs)
        // ==========================================
        if (p.needs?.mood != null)
        {
            float mood = p.needs.mood.CurLevel;
            // 修正：如果 mindState 為空的安全檢查
            float breakThreshold = p.mindState?.mentalBreaker?.BreakThresholdMajor ?? 0.35f;

            if (mood < breakThreshold)
            {
                tags.Add("绝望");
                tags.Add("痛苦");
                tags.Add("焦虑");
            }
            else if (mood < 0.35f)
            {
                tags.Add("悲伤");
                tags.Add("焦虑");
            }
            else if (mood > 0.85f)
            {
                tags.Add("开心");
            }
            else if (mood > 0.5f && !p.InMentalState && !p.IsFighting())
            {
                tags.Add("平静");
            }
        }

        if (p.needs?.food != null && p.needs.food.Starving) tags.Add("饥饿");
        if (p.needs?.rest != null && p.needs.rest.CurLevel < 0.1f) tags.Add("疲劳");

        // ==========================================
        // 3. 健康與受傷 (Health)
        // ==========================================
        if (p.health != null)
        {
            var hediffSet = p.health.hediffSet;

            // 修正：使用 Hediff_Injury 類別判斷，涵蓋所有物理傷害 (槍傷、燒傷、刀傷等)
            // 這樣就不需要去 DefOf 找不存在的 definitions
            if (hediffSet.hediffs.Any(h => h is Hediff_Injury))
            {
                tags.Add("受伤");
                tags.Add("痛苦");
                tags.Add("生存");
            }

            // 生病判定
            if (hediffSet.hediffs.Any(h => h.Visible && h.def.makesSickThought))
            {
                tags.Add("生病");
                tags.Add("痛苦");
            }

            // 修正：成癮判定 (直接檢查 IsAddiction 屬性，避開 causesNeed 錯誤)
            if (hediffSet.hediffs.Any(h => h.def.IsAddiction))
            {
                tags.Add("成瘾");
            }

            if (p.Downed)
            {
                tags.Add("无助");
                tags.Add("绝望");
            }
        }

        // ==========================================
        // 4. 當前行為 (Jobs)
        // ==========================================
        if (p.CurJob != null)
        {
            JobDef def = p.CurJob.def;
            string defName = def.defName;

            // --- 戰鬥類 ---
            if (def == JobDefOf.AttackMelee || def == JobDefOf.AttackStatic || def == JobDefOf.Wait_Combat)
            {
                tags.Add("战斗");
                tags.Add("生存");
                if (p.Map != null && p.Map.IsPlayerHome && p.HostileTo(Faction.OfPlayer)) tags.Add("袭击");
            }
            else if (def == JobDefOf.Flee || def == JobDefOf.FleeAndCower)
            {
                tags.Add("逃跑");
                tags.Add("恐惧");
                tags.Add("生存");
            }
            else if (def == JobDefOf.ManTurret)
            {
                tags.Add("战斗");
            }

            // --- 社交類 ---
            else if (def == JobDefOf.SocialFight)
            {
                tags.Add("争吵");
                tags.Add("愤怒");
                tags.Add("仇恨");
            }
            else if (def == JobDefOf.Lovin || defName.Contains("Propose") || defName.Contains("Marry"))
            {
                tags.Add("爱情");
                tags.Add("开心");
            }
            else if (def == JobDefOf.SpectateCeremony)
            {
                tags.Add("聚会");
                tags.Add("仪式");
            }

            // --- 互動類 ---
            else if (def == JobDefOf.PrisonerAttemptRecruit || defName.Contains("Convert"))
            {
                tags.Add("劝说");
                tags.Add("信念");
            }
            else if (defName == "VisitSickPawn" || defName == "CheerUp")
            {
                tags.Add("友谊");
            }

            // --- 醫療類 ---
            else if (def == JobDefOf.TendPatient || def == JobDefOf.Rescue || def == JobDefOf.FeedPatient)
            {
                tags.Add("治疗");
                tags.Add("救助");
                tags.Add("友谊");
            }

            // --- 應急類 ---
            else if (def == JobDefOf.BeatFire || def == JobDefOf.ExtinguishSelf)
            {
                tags.Add("火灾");
                tags.Add("紧急");
                tags.Add("恐惧");
            }

            // --- 日常 ---
            // 修正：使用 joyKind 判斷是否為娛樂活動，取代不存在的 JobDriver_Relax
            else if (def.joyKind != null)
            {
                tags.Add("闲聊");
                tags.Add("平静");
            }
            else if (defName.Contains("Art") || (p.CurJob.bill?.recipe?.products?.Any(t => t.thingDef.IsArt) ?? false))
            {
                tags.Add("艺术");
                tags.Add("信念");
            }
            // 修正：使用 WorkGiverDef 來判斷是否為工作
            else if (p.CurJob.workGiverDef != null)
            {
                tags.Add("工作");
                if (p.needs?.rest?.CurLevel < 0.3f) tags.Add("疲劳");
            }
        }

        // ==========================================
        // 5. 環境與社交脈絡
        // ==========================================

        Lord lord = p.GetLord();
        if (lord != null)
        {
            // 使用字串檢查避免 DLC 依賴報錯
            string lordJobName = lord.LordJob.GetType().Name;

            if (lordJobName.Contains("Ritual"))
            {
                tags.Add("仪式");
                tags.Add("信念");
                tags.Add("聚会");
            }
            else if (lord.LordJob is LordJob_Joinable_Party || lord.LordJob is LordJob_VoluntarilyJoinable)
            {
                tags.Add("聚会");
                tags.Add("开心");
                tags.Add("友谊");
            }
            else if (lord.LordJob is LordJob_AssaultColony || lord.LordJob is LordJob_DefendPoint)
            {
                tags.Add("战斗");
                tags.Add("袭击");
            }
        }

        // 在方法內獲取 Def (Lazy Loading)
        if (_observedLayingCorpseDef == null)
        {
            _observedLayingCorpseDef = DefDatabase<ThoughtDef>.GetNamed("ObservedLayingCorpse", false);
        }

        // 使用緩存的變數
        if (p.needs?.mood?.thoughts?.memories != null)
        {
            if (_observedLayingCorpseDef != null &&
                p.needs.mood.thoughts.memories.Memories.Any(m => m.def == _observedLayingCorpseDef))
            {
                tags.Add("死亡");
                tags.Add("恐惧");
                tags.Add("厌恶");
            }
        }

        if (p.IsPrisoner)
        {
            tags.Add("囚犯");
            tags.Add("自由");
            tags.Add("无助");
        }
        else if (p.IsSlave)
        {
            tags.Add("自由");
            tags.Add("工作");
        }

        if (p.Map != null && p.Map.IsPlayerHome && !p.Map.dangerWatcher.DangerRating.Equals(Danger.Deadly))
        {
            if (p.IsColonist && p.needs?.mood?.CurLevel > 0.6f)
            {
                tags.Add("家园");
            }
        }

        if (p.Map != null)
        {
            Pawn target = p.CurJob?.targetA.Thing as Pawn;
            if (target != null && p.relations != null && p.relations.OpinionOf(target) < -50)
            {
                tags.Add("仇恨");
                if (p.IsFighting()) tags.Add("复仇");
            }
        }

        return tags.Where(t => Constant.CoreMemoryTags.Contains(t)).ToList();
    }
}