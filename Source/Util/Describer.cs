using Verse;

namespace RimTalk.Util;

public static class Describer
{
    public static string Wealth(float wealthTotal)
    {
        return wealthTotal switch
        {
            < 50_000f => "impecunious",
            < 100_000f => "needy",
            < 200_000f => "just rid of starving",
            < 300_000f => "moderately prosperous",
            < 400_000f => "rich",
            < 600_000f => "luxurious",
            < 1_000_000f => "extravagant",
            < 1_500_000f => "treasures fill the home",
            < 2_000_000f => "as rich as glitter world",
            _ => "richest in the galaxy"
        };
    }
    
    public static string Beauty(float beauty)
    {
        return beauty switch
        {
            > 100f => "wondrously", 
            > 20f => "impressive",
            > 10f => "beautiful",
            > 5f => "decent",
            > -1f => "general",
            > -5f => "awful",
            > -20f => "very awful",
            _ => "disgusting"
        };
    }

    public static string Cleanliness(float cleanliness)
    {
        return cleanliness switch
        {
            > 1.5f => "spotless",
            > 0.5f => "clean",
            > -0.5f => "neat",
            > -1.5f => "a bit dirty",
            > -2.5f => "dirty",
            > -5f => "very dirty",
            _ => "foul"
        };
    }

    // 使用翻譯鍵
    public static string Resistance(float value)
    {
        if (value <= 0f) return "RimTalk.Describer.Resistance.Broken".Translate();
        if (value < 2f) return "RimTalk.Describer.Resistance.Barely".Translate();
        if (value < 6f) return "RimTalk.Describer.Resistance.Weakened".Translate();
        if (value < 12f) return "RimTalk.Describer.Resistance.Strong".Translate();
        return "RimTalk.Describer.Resistance.Defiant".Translate();
    }
    public static string Will(float value)
    {
        if (value <= 0f) return "RimTalk.Describer.Will.None".Translate();
        if (value < 2f) return "RimTalk.Describer.Will.Weak".Translate();
        if (value < 6f) return "RimTalk.Describer.Will.Moderate".Translate();
        if (value < 12f) return "RimTalk.Describer.Will.Strong".Translate();
        return "RimTalk.Describer.Will.Unyielding".Translate();
    }
    public static string Suppression(float value)
    {
        if (value < 20f) return "RimTalk.Describer.Suppression.Rebellious".Translate();
        if (value < 50f) return "RimTalk.Describer.Suppression.Unstable".Translate();
        if (value < 80f) return "RimTalk.Describer.Suppression.Obedient".Translate();
        return "RimTalk.Describer.Suppression.Cowed".Translate();
    }
}
