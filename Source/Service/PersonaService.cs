using RimTalk.Service;
using RimTalk.Util;
using System;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace RimTalk.Data;

public static class PersonaService
{
    // [NEW] 新增取消權杖
    private static CancellationTokenSource _cts = new();

    public static string GetPersonality(Pawn pawn)
    {
        return Hediff_Persona.GetOrAddNew(pawn).Personality;
    }

    public static void SetPersonality(Pawn pawn, string personality)
    {
        Hediff_Persona.GetOrAddNew(pawn).Personality = personality;
    }

    public static float GetTalkInitiationWeight(Pawn pawn)
    {
        return Hediff_Persona.GetOrAddNew(pawn).TalkInitiationWeight;
    }

    public static void SetTalkInitiationWeight(Pawn pawn, float frequency)
    {
        Hediff_Persona.GetOrAddNew(pawn).TalkInitiationWeight = frequency;
    }

    // [NEW] 新增取消方法
    public static void CancelAllRetries()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
    }

    public static async Task<PersonalityData> GeneratePersona(Pawn pawn)
    {
        string pawnBackstory = PromptService.CreatePawnBackstory(pawn, PromptService.InfoLevel.Full);

        try
        {
            // [NEW] 檢查是否已取消
            if (_cts.Token.IsCancellationRequested) return null;

            AIService.UpdateContext($"[Character]\n{pawnBackstory}");
            var request = new TalkRequest(Constant.PersonaGenInstruction, pawn);
            PersonalityData personalityData = await AIService.Query<PersonalityData>(request);

            // [NEW] 再次檢查是否已取消 (防止在等待期間遊戲被重置)
            if (_cts.Token.IsCancellationRequested) return null;

            if (personalityData?.Persona != null)
            {
                personalityData.Persona = personalityData.Persona.Replace("**", "").Trim();
            }

            return personalityData;
        }
        catch (Exception e)
        {
            Logger.Error(e.Message);
            return null;
        }
    }
}
