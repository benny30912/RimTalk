using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Util;

public static class PawnUtil
{
    public static bool IsTalkEligible(this Pawn pawn)
    {
        if (pawn.IsPlayer()) return true;
        if (pawn.HasVocalLink()) return true;
        if (pawn.DestroyedOrNull() || !pawn.Spawned || pawn.Dead) return false;
        if (!pawn.RaceProps.Humanlike) return false;
        if (pawn.RaceProps.intelligence < Intelligence.Humanlike) return false;
        if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking)) return false;
        if (pawn.skills?.GetSkill(SkillDefOf.Social) == null) return false;

        RimTalkSettings settings = Settings.Get();
        if (!settings.AllowBabiesToTalk && pawn.IsBaby()) return false;
        
        return pawn.IsFreeColonist ||
               (settings.AllowSlavesToTalk && pawn.IsSlave) ||
               (settings.AllowPrisonersToTalk && pawn.IsPrisoner) ||
               (settings.AllowOtherFactionsToTalk && pawn.IsVisitor()) ||
               (settings.AllowEnemiesToTalk && pawn.IsEnemy());
    }

    public static HashSet<Hediff> GetHediffs(this Pawn pawn)
    {
        return pawn?.health.hediffSet.hediffs.Where(hediff => hediff.Visible).ToHashSet();
    }

    public static bool IsInDanger(this Pawn pawn, bool includeMentalState = false)
    {
        if (pawn == null || pawn.IsPlayer()) return false;
        if (pawn.Dead) return true;
        if (pawn.Downed) return true;
        if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return true;
        if (pawn.InMentalState && includeMentalState) return true;
        if (pawn.IsBurning()) return true;
        if (pawn.health.hediffSet.PainTotal >= pawn.GetStatValue(StatDefOf.PainShockThreshold)) return true;
        if (pawn.health.hediffSet.BleedRateTotal > 0.3f) return true;
        if (pawn.IsInCombat()) return true;
        if (pawn.CurJobDef == JobDefOf.Flee) return true;

        // Check severe Hediffs
        foreach (var h in pawn.health.hediffSet.hediffs)
        {
            if (h.Visible && (h.CurStage?.lifeThreatening == true ||
                              h.def.lethalSeverity > 0 && h.Severity > h.def.lethalSeverity * 0.8f))
                return true;
        }

        return false;
    }

    public static bool IsInCombat(this Pawn pawn)
    {
        if (pawn == null) return false;

        if (pawn.mindState.enemyTarget != null) return true;

        if (pawn.stances?.curStance is Stance_Busy busy && busy.verb != null)
        {
            // 檢查 Verb 目標是否為 Pawn（任何攻擊行為都視為戰鬥）
            if (busy.focusTarg.Thing is Pawn)
                return true;
        }

        Pawn hostilePawn = pawn.GetHostilePawnNearBy();
        return hostilePawn != null && pawn.Position.DistanceTo(hostilePawn.Position) <= 20f;
    }

    public static string GetRole(this Pawn pawn, bool includeFaction = false)
    {
        if (pawn == null) return null;
        if (pawn.IsPrisoner) return "Prisoner";
        if (pawn.IsSlave) return "Slave";
        if (pawn.IsEnemy())
        {
            if (pawn.GetMapRole() == MapRole.Invading)
                return includeFaction && pawn.Faction != null ? $"Enemy Group({pawn.Faction.Name})" : "Enemy";
            return "Enemy Defender";
        }
        if (pawn.IsVisitor())
            return includeFaction && pawn.Faction != null ? $"Visitor Group({pawn.Faction.Name})" : "Visitor";
        if (pawn.IsQuestLodger()) return "Lodger";
        if (pawn.IsFreeColonist)
        {
            string role = pawn.GetMapRole() == MapRole.Invading ? "Invader" : "Colonist";
            // 檢查是否加入不滿 15 天 (900,000 Ticks)
            if (pawn.records.GetValue(RecordDefOf.TimeAsColonistOrColonyAnimal) < 900000)
            {
                role += "(New Member)";
            }
            return role;
        }
        return null;
    }

    public static bool IsVisitor(this Pawn pawn)
    {
        return pawn?.Faction != null && pawn.Faction != Faction.OfPlayer && !pawn.HostileTo(Faction.OfPlayer);
    }

    public static bool IsEnemy(this Pawn pawn)
    {
        return pawn != null && pawn.HostileTo(Faction.OfPlayer);
    }

    public static bool IsBaby(this Pawn pawn)
    {
        return pawn.ageTracker?.CurLifeStage?.developmentalStage < DevelopmentalStage.Child;
    }

    /// <summary>
    /// [向後相容] 舊版 GetPawnStatusFull，僅回傳 (status, isInDanger)。
    /// 保留此簽名供外部 Mod（如 RimTalkEventMemory）使用。
    /// </summary>
    public static (string status, bool isInDanger) GetPawnStatusFull(
        this Pawn pawn,
        List<Pawn> nearbyPawns)
    {
        // 委派給新版方法，丟棄額外回傳值
        var (status, isInDanger, _, _) = GetPawnStatusFullExtend(pawn, nearbyPawns);
        return (status, isInDanger);
    }

    public static (string status, bool isInDanger, List<string> activities, List<string> names) GetPawnStatusFullExtend(
    this Pawn pawn,
    List<Pawn> nearbyPawns)
    {
        var settings = Settings.Get();

        if (pawn == null)
            return (null, false, new List<string>(), new List<string>());

        if (pawn.IsPlayer())
            return (settings.PlayerName, false, new List<string>(), new List<string>());

        bool isInDanger = false;
        var lines = new List<string>();

        // [NEW] 收集動作句子和人名
        var collectedActivities = new List<string>();
        var collectedNames = new List<string>();

        // Collect all relevant pawns for context
        var relevantPawns = CollectRelevantPawns(pawn, nearbyPawns);
        bool useOptimization = settings.Context.EnableContextOptimization;

        // Main pawn activity
        string pawnLabel = GetPawnLabel(pawn, relevantPawns, useOptimization);
        string pawnActivity = GetPawnActivity(pawn, relevantPawns, useOptimization);
        lines.Add($"{pawnLabel} {pawnActivity}");

        // [NEW] 收集主 Pawn 的動作
        if (!string.IsNullOrEmpty(pawnActivity))
            collectedActivities.Add($"{pawn.LabelShort} {pawnActivity}");

        if (pawn.IsInDanger())
            isInDanger = true;

        bool hasAnyNearbyLog = false;

        // Nearby pawns in danger
        if (nearbyPawns != null && nearbyPawns.Any())
        {
            var (notablePawns, nearbyInDanger) = GetNearbyPawnsInDanger(pawn, nearbyPawns, relevantPawns, useOptimization, settings.Context.MaxPawnContextCount);
            if (nearbyInDanger.Any())
            {
                lines.Add("People in condition nearby: " + string.Join("; ", nearbyInDanger));
                isInDanger = true;
                hasAnyNearbyLog = true;

                // [NEW] 收集附近危險狀態的人名和動作
                foreach (var p in notablePawns)
                {
                    collectedNames.Add(p.LabelShort);
                    string activity = GetPawnActivity(p, relevantPawns, useOptimization);
                    if (!string.IsNullOrEmpty(activity))
                        collectedActivities.Add($"{p.LabelShort} {activity}");
                }
            }

            string nearbyList = GetNearbyPawnsList(nearbyPawns, relevantPawns, useOptimization, settings.Context.MaxPawnContextCount, excludePawns: notablePawns);

            if (nearbyList != "none")
            {
                lines.Add("Nearby: " + nearbyList);
                hasAnyNearbyLog = true;

                // [NEW] 收集附近每個人的活動（分開收集）
                foreach (var np in nearbyPawns.Where(p => !relevantPawns.Contains(p) && (notablePawns == null || !notablePawns.Contains(p))))
                {
                    collectedNames.Add(np.LabelShort);
                    // [NEW] 每個人的活動分開收集
                    string activity = GetPawnActivity(np, relevantPawns, useOptimization);
                    if (!string.IsNullOrEmpty(activity))
                        collectedActivities.Add($"{np.LabelShort} {activity}");  // [MOD] 加入人名+活動
                }
            }
        }

        if (!hasAnyNearbyLog)
        {
            lines.Add("Nearby people: none");
        }

        // Contextual information
        AddContextualInfo(pawn, lines, ref isInDanger);

        // [NEW] 收集情境資訊
        foreach (var line in lines)
        {
            if (line.StartsWith("Combat:") || line.StartsWith("Threat:") || line.StartsWith("Alert:") ||
                line.Contains("Visiting") || line.Contains("Assaulting") || line.Contains("Defending") ||
                line.Contains("staging") || line.Contains("settlement"))
            {
                collectedActivities.Add(line);
            }
        }

        return (string.Join("\n", lines), isInDanger, collectedActivities, collectedNames);
    }

    private static HashSet<Pawn> CollectRelevantPawns(Pawn mainPawn, List<Pawn> nearbyPawns)
    {
        // [MOD] 初始為空，不包含 mainPawn，避免自我裝飾
        var relevantPawns = new HashSet<Pawn>();

        if (mainPawn.CurJob != null)
            AddJobTargetsToRelevantPawns(mainPawn.CurJob, relevantPawns);

        if (nearbyPawns != null)
        {
            relevantPawns.UnionWith(nearbyPawns);
            
            foreach (var nearby in nearbyPawns.Where(p => p.CurJob != null))
                AddJobTargetsToRelevantPawns(nearby.CurJob, relevantPawns);
        }

        // [NEW] 最終防線：強制移除 mainPawn，確保絕對不自我修飾
        relevantPawns.Remove(mainPawn);

        return relevantPawns;
    }

    private static string GetPawnLabel(Pawn pawn, HashSet<Pawn> relevantPawns, bool useOptimization)
    {
        if (useOptimization)
            return pawn.LabelShort;
        
        return relevantPawns.Contains(pawn) 
            ? ContextHelper.GetDecoratedName(pawn) 
            : pawn.LabelShort;
    }

    private static string GetPawnActivity(Pawn pawn, HashSet<Pawn> relevantPawns, bool useOptimization)
    {
        string activity = pawn.GetActivity();
        
        if (useOptimization || string.IsNullOrEmpty(activity))
            return activity;

        return DecorateText(activity, relevantPawns);
    }

    // 增加回傳篩選結果
    private static (List<Pawn>, List<string>) GetNearbyPawnsInDanger(Pawn mainPawn, List<Pawn> nearbyPawns,
        HashSet<Pawn> relevantPawns, bool useOptimization, int maxCount)
    {
        // 這裡維持邏輯封裝：先篩選出 Pawn
        var identifiedPawns = nearbyPawns
            .Where(p => p.Faction == mainPawn.Faction && p.IsInDanger(true))
            .Take(maxCount - 1)
            .ToList();

        var descriptions = identifiedPawns
            .Select(p =>
            {
                string label = GetPawnLabel(p, relevantPawns, useOptimization);
                string activity = GetPawnActivity(p, relevantPawns, useOptimization);
                return $"{label} in {activity.Replace("\n", "; ")}";
            })
            .ToList();

        return (identifiedPawns, descriptions);
    }

    // 增加 excludePawns 參數
    private static string GetNearbyPawnsList(List<Pawn> nearbyPawns, HashSet<Pawn> relevantPawns,
        bool useOptimization, int maxCount, List<Pawn> excludePawns = null)
    {
        // 過濾邏輯封裝在此：排除 context 中的人 + 排除 notable 的人
        var validPawns = nearbyPawns
            .Where(p => !relevantPawns.Contains(p)) // [NEW] 過濾掉已經是 relevantPawns 的人，避免重複顯示
            .Where(p => excludePawns == null || !excludePawns.Contains(p)) // 使用傳入的排除清單
            .ToList();

        if (!validPawns.Any())
            return "none";

        var pawnDescriptions = validPawns
            .Select(p =>
            {
                string label = GetPawnLabel(p, relevantPawns, useOptimization);
                
                if (Cache.Get(p) != null)
                {
                    string activity = GetPawnActivity(p, relevantPawns, useOptimization);
                    return $"{label} {activity.StripTags()}";
                }
                
                return label;
            })
            .ToList();

        if (pawnDescriptions.Count > maxCount)
            return string.Join(", ", pawnDescriptions.Take(maxCount)) + ", and others";
        
        return string.Join(", ", pawnDescriptions);
    }

    private static void AddContextualInfo(Pawn pawn, List<string> lines, ref bool isInDanger)
    {
        if (pawn.IsVisitor())
        {
            lines.Add("Visiting colony"); // 改: "Visiting colony"
            return;
        }

        if (pawn.IsFreeColonist && pawn.GetMapRole() == MapRole.Invading)
        {
            lines.Add("Away from home, assaulting enemy settlement"); // "Away from home" 點出地點，"Assaulting" 描述動作，不提及目的(capture)，留給 AI 發揮
            return;
        }

        if (pawn.IsEnemy())
        {
            if (pawn.GetMapRole() == MapRole.Invading)
            {
                var lord = pawn.GetLord()?.LordJob;
                if (lord is LordJob_StageThenAttack || lord is LordJob_Siege)
                    lines.Add("Staging for colony assault"); // 正在集結/圍攻
                else
                    lines.Add("Assaulting colony"); // 正在進攻
            }
            else
            {
                lines.Add("Defending settlement"); // 改: "Defending settlement"
            }
            return;
        }

        // Check for nearby hostiles
        Pawn nearestHostile = pawn.GetHostilePawnNearBy();
        if (nearestHostile != null)
        {
            float distance = pawn.Position.DistanceTo(nearestHostile.Position);

            // 改用 Combat 前綴，讓 LLM 知道這是戰鬥狀態，微調用詞
            if (distance <= 10f)
                lines.Add("Combat: Engaging in battle!");
            else if (distance <= 20f)
                lines.Add("Threat: Hostiles in close range!");
            else
                lines.Add("Alert: Hostiles spotted in area");
            
            isInDanger = true;
        }
    }

    /// <summary>
    /// Decorates text by replacing pawn names with their decorated versions
    /// </summary>
    private static string DecorateText(string text, HashSet<Pawn> relevantPawns)
    {
        if (string.IsNullOrEmpty(text) || relevantPawns == null || !relevantPawns.Any())
            return text;

        // Build replacement map
        var replacements = relevantPawns
            .Select(p => new { Key = p.LabelShort, Value = ContextHelper.GetDecoratedName(p) })
            .Where(x => !string.IsNullOrEmpty(x.Key))
            .OrderByDescending(x => x.Key.Length) // Longer names first to avoid partial matches
            .ToList();

        // Apply replacements
        return replacements.Aggregate(text, (current, replacement) => 
            current.Replace(replacement.Key, replacement.Value));
    }

    public static Pawn GetHostilePawnNearBy(this Pawn pawn)
    {
        if (pawn?.Map == null) return null;

        Faction referenceFaction = GetReferenceFaction(pawn);
        if (referenceFaction == null) return null;

        var hostileTargets = pawn.Map.attackTargetsCache?.TargetsHostileToFaction(referenceFaction);
        if (hostileTargets == null) return null;

        return FindClosestValidThreat(pawn, referenceFaction, hostileTargets);
    }

    private static Faction GetReferenceFaction(Pawn pawn)
    {
        if (pawn.IsPrisoner || pawn.IsSlave || pawn.IsFreeColonist || 
            pawn.IsVisitor() || pawn.IsQuestLodger())
        {
            return Faction.OfPlayer;
        }

        return pawn.Faction;
    }

    private static Pawn FindClosestValidThreat(Pawn pawn, Faction referenceFaction, 
        IEnumerable<IAttackTarget> hostileTargets)
    {
        Pawn closestPawn = null;
        float closestDistSq = float.MaxValue;

        foreach (var target in hostileTargets)
        {
            if (!GenHostility.IsActiveThreatTo(target, referenceFaction))
                continue;

            if (target.Thing is not Pawn threatPawn || threatPawn.Downed)
                continue;

            if (!IsValidThreat(pawn, threatPawn))
                continue;

            float distSq = pawn.Position.DistanceToSquared(threatPawn.Position);
            if (distSq < closestDistSq)
            {
                closestDistSq = distSq;
                closestPawn = threatPawn;
            }
        }

        return closestPawn;
    }

    private static bool IsValidThreat(Pawn observer, Pawn threat)
    {
        // Filter out prisoners/slaves as threats to colonists
        if (threat.IsPrisoner && threat.HostFaction == Faction.OfPlayer)
            return false;
        
        if (threat.IsSlave && threat.HostFaction == Faction.OfPlayer)
            return false;

        // Prisoners don't threaten each other
        if (observer.IsPrisoner && threat.IsPrisoner)
            return false;

        Lord lord = threat.GetLord();

        // Exclude tactically retreating pawns
        if (lord is { CurLordToil: LordToil_ExitMapFighting or LordToil_ExitMap })
            return false;
        
        if (threat.CurJob?.exitMapOnArrival == true)
            return false;

        // Exclude roaming mech cluster pawns
        if (threat.RaceProps.IsMechanoid && lord is { CurLordToil: LordToil_DefendPoint })
            return false;

        // [NEW] 排除未發動攻擊的野生動物
        if (threat.RaceProps.Animal &&
            threat.Faction == null &&
            threat.mindState.enemyTarget == null)
            return false;

        return true;
    }

    private static readonly HashSet<string> ResearchJobDefNames = new()
    {
        "Research",
        "RR_Analyse",
        "RR_AnalyseInPlace",
        "RR_AnalyseTerrain",
        "RR_Research",
        "RR_InterrogatePrisoner",
        "RR_LearnRemotely"
    };

    private static string GetActivity(this Pawn pawn)
    {
        if (pawn == null) return null;
        
        if (pawn.InMentalState)
            return pawn.MentalState?.InspectLine;

        if (pawn.CurJobDef is null)
            return null;

        var target = pawn.IsAttacking() ? pawn.TargetCurrentlyAimingAt.Thing?.LabelShortCap : null;
        if (target != null)
            return $"Attacking {target}";

        var lord = pawn.GetLord()?.LordJob?.GetReport(pawn);
        var job = pawn.jobs?.curDriver?.GetReport();

        string activity = lord == null ? job : 
                         job == null ? lord : 
                         $"{lord} ({job})";

        if (ResearchJobDefNames.Contains(pawn.CurJob?.def.defName))
        {
            activity = AppendResearchProgress(activity);
        }

        return activity;
    }

    private static string AppendResearchProgress(string activity)
    {
        ResearchProjectDef project = Find.ResearchManager.GetProject();
        if (project == null) return activity;

        float progress = Find.ResearchManager.GetProgress(project);
        float percentage = (progress / project.baseCost) * 100f;
        return $"{activity} (Project: {project.label} - {percentage:F0}%)";
    }

    private static void AddJobTargetsToRelevantPawns(Job job, HashSet<Pawn> relevantPawns)
    {
        if (job == null) return;

        foreach (TargetIndex index in Enum.GetValues(typeof(TargetIndex)))
        {
            try
            {
                var target = job.GetTarget(index);
                if (target == (LocalTargetInfo)(Thing)null)
                    continue;

                if (target.HasThing && target.Thing is Pawn pawn)
                {
                    relevantPawns.Add(pawn);
                    // [MOD] 移除遞迴呼叫以提升效能並避免潛在循環
                }
            }
            catch
            {
                // Ignore invalid indices
            }
        }
    }

    public static MapRole GetMapRole(this Pawn pawn)
    {
        if (pawn?.Map == null || pawn.IsPrisonerOfColony)
            return MapRole.None;

        Map map = pawn.Map;
        Faction mapFaction = map.ParentFaction;

        if (mapFaction == pawn.Faction || (map.IsPlayerHome && pawn.Faction == Faction.OfPlayer))
            return MapRole.Defending;

        if (pawn.Faction.HostileTo(mapFaction))
            return MapRole.Invading;

        return MapRole.Visiting;
    }

    public static string GetPrisonerSlaveStatus(this Pawn pawn)
    {
        if (pawn == null) return null;

        var lines = new List<string>();

        if (pawn.IsPrisoner)
        {
            float resistance = pawn.guest.resistance;
            lines.Add($"Resistance: {resistance:0.0} ({Describer.Resistance(resistance)})");

            float will = pawn.guest.will;
            lines.Add($"Will: {will:0.0} ({Describer.Will(will)})");
        }
        else if (pawn.IsSlave)
        {
            var suppressionNeed = pawn.needs?.TryGetNeed<Need_Suppression>();
            if (suppressionNeed != null)
            {
                float suppression = suppressionNeed.CurLevelPercentage * 100f;
                lines.Add($"Suppression: {suppression:0.0}% ({Describer.Suppression(suppression)})");
            }
        }

        return lines.Any() ? string.Join("\n", lines) : null;
    }

    public static bool IsPlayer(this Pawn pawn)
    {
        return pawn == Cache.GetPlayer();
    }

    public static bool HasVocalLink(this Pawn pawn)
    {
        return Settings.Get().AllowNonHumanToTalk && 
               pawn.health.hediffSet.HasHediff(Constant.VocalLinkDef);
    }
}
