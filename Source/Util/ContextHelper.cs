using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using static RimTalk.Service.PromptService;

namespace RimTalk.Util;

public static class ContextHelper
{
    public static string GetPawnLocationStatus(Pawn pawn)
    {
        if (pawn?.Map == null || pawn.Position == IntVec3.Invalid)
            return null;

        var room = pawn.GetRoom();
        return room is { PsychologicallyOutdoors: false } 
            ? "Indoors".Translate() 
            : "Outdoors".Translate();
    }

    public static Dictionary<Thought, float> GetThoughts(Pawn pawn)
    {
        var thoughts = new List<Thought>();
        pawn?.needs?.mood?.thoughts?.GetAllMoodThoughts(thoughts);

        return thoughts
            .GroupBy(t => t.def.defName)
            .ToDictionary(g => g.First(), g => g.Sum(t => t.MoodOffset()));
    }

    public static string GetDecoratedName(Pawn pawn)
    {
        // 針對非人類（如機器人、動物）
        if (!pawn.RaceProps.Humanlike)
            return $"{pawn.LabelShort}(Age: {pawn.ageTracker.AgeBiologicalYears}, Race: {pawn.def.LabelCap})";

        // 針對人類或亞人種
        var race = ModsConfig.BiotechActive && pawn.genes?.Xenotype != null
            ? pawn.genes.Xenotype.LabelCap
            : pawn.def.LabelCap;

        // 調整格式：加入性別、角色標籤，並用逗號分隔
        return $"{pawn.LabelShort}(Age: {pawn.ageTracker.AgeBiologicalYears}, Gender: {pawn.gender.GetLabel()}, Role: {pawn.GetRole(true)}, Race: {race})";
    }

    public static bool IsWall(Thing thing)
    {
        var data = thing.def.graphicData;
        return data != null && data.linkFlags.HasFlag((Enum)LinkFlags.Wall);
    }
    
    public static string Sanitize(string text, Pawn pawn = null)
    {
        if (pawn != null)
            text = text.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn).Resolve();
        return text.StripTags().RemoveLineBreaks();
    }

    public static string FormatBackstory(string label, BackstoryDef backstory, Pawn pawn, InfoLevel infoLevel)
    {
        var result = $"{label}: {backstory.title}({backstory.titleShort})";
        if (infoLevel == InfoLevel.Full)
            result += $":{Sanitize(backstory.description, pawn)}";
        return result;
    }

    public static List<IntVec3> GetNearbyCells(Pawn pawn, int distance = 5)
    {
        var cells = new List<IntVec3>();
        var facing = pawn.Rotation.FacingCell;

        for (int i = 1; i <= distance; i++)
        {
            var targetCell = pawn.Position + facing * i;
            for (int offset = -1; offset <= 1; offset++)
            {
                var cell = new IntVec3(targetCell.x + offset, targetCell.y, targetCell.z);
                if (cell.InBounds(pawn.Map))
                    cells.Add(cell);
            }
        }

        return cells;
    }

    /// <summary>
    /// [MOD] 改為回傳 List<Thing>，由呼叫端決定如何處理
    /// </summary>
    public static List<Thing> CollectNearbyItems(Pawn pawn, int maxItems)
    {
        var items = new List<Thing>();
        var seenThings = new HashSet<Thing>();
        var nearbyCells = GetNearbyCells(pawn);

        foreach (var cell in nearbyCells.InRandomOrder())
        {
            if (items.Count >= maxItems)
                break;

            var thingsHere = cell.GetThingList(pawn.Map);
            if (thingsHere == null || thingsHere.Count == 0)
                continue;

            // Skip cells with pawns/animals
            if (thingsHere.Any(t => t?.def != null &&
                t.def.category != ThingCategory.Building &&
                t.def.category != ThingCategory.Plant &&
                t.def.category != ThingCategory.Item &&
                !t.def.IsFilth))
                continue;

            // Get one valid thing per category
            var candidatesByCategory = new Dictionary<ThingCategory, Thing>();
            foreach (var thing in thingsHere)
            {
                if (thing?.def == null)
                    continue;

                var isValid = thing.def.category == ThingCategory.Building ||
                              thing.def.category == ThingCategory.Plant ||
                              thing.def.category == ThingCategory.Item ||
                              thing.def.IsFilth;

                if (!isValid)
                    continue;

                if (thing.def.category == ThingCategory.Building && IsWall(thing))
                    continue;

                if (!candidatesByCategory.ContainsKey(thing.def.category))
                    candidatesByCategory[thing.def.category] = thing;
            }

            if (candidatesByCategory.Count == 0)
                continue;

            var picked = candidatesByCategory.Values.ToList().RandomElement();
            if (seenThings.Contains(picked))
                continue;

            seenThings.Add(picked);
            items.Add(picked);
        }

        return items;
    }
}
