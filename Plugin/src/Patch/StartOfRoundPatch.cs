using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;

namespace LethalGargoyles.src.Patch
{
    [HarmonyPatch(typeof(StartOfRound))]
    public static class GetDeathCauses
    {
        public static List<(string playerName, string causeOfDeath, string source)> previousRoundDeaths = [];

        [HarmonyPostfix]
        [HarmonyPatch("WritePlayerNotes")]
#pragma warning disable IDE0051 // Remove unused private members
        static void PostFixWritePlayerNotes()
#pragma warning restore IDE0051 // Remove unused private members
        {
            Plugin.Logger.LogInfo("Getting Causes of Death.");
            previousRoundDeaths.Clear();
            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player.isPlayerDead)
                {
                    string causeOfDeathString;
                    if (Plugin.Instance.IsCoronerLoaded)
                    {
                        causeOfDeathString = player.causeOfDeath.ToString() ?? "Unknown";
                        Plugin.Logger.LogInfo($"Vanilla caught {player.playerUsername}'s cause of death this round was {causeOfDeathString}");
                        previousRoundDeaths.Add((player.playerUsername, causeOfDeathString, "Vanilla"));

                        causeOfDeathString = SoftDepends.CoronerClass.CoronerGetCauseOfDeath(player) ?? "Unknown";
                        Plugin.Logger.LogInfo($"Coroner caught {player.playerUsername}'s cause of death this round was {causeOfDeathString}");
                        previousRoundDeaths.Add((player.playerUsername, causeOfDeathString, "Coroner"));
                    }
                    else
                    {
                        causeOfDeathString = player.causeOfDeath.ToString() ?? "Unknown";
                        previousRoundDeaths.Add((player.playerUsername, causeOfDeathString, "Vanilla"));
                        Plugin.Logger.LogInfo($"Vanilla caught {player.playerUsername}'s cause of death this round was {causeOfDeathString}");
                    }
                }
            }
        }
    }
}