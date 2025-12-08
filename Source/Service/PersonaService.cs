using System;
using System.Threading;
using System.Threading.Tasks;
using RimTalk.Service;
using RimTalk.Util;
using Verse;

namespace RimTalk.Data;

public static class PersonaService
{
    // 新增：取消權杖來源
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

    // 新增：取消所有正在進行的生成任務
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
            // 檢查是否已被取消
            if (_cts.Token.IsCancellationRequested) return null;

            AIService.UpdateContext($"[Character]\n{pawnBackstory}");
            var request = new TalkRequest(Constant.PersonaGenInstruction, pawn);

            // 這裡雖然 AIService.Query 目前還沒支援 CancellationToken 參數，
            // 但我們可以在 await 後檢查 Token，避免在重置後繼續處理舊資料
            PersonalityData personalityData = await AIService.Query<PersonalityData>(request);

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