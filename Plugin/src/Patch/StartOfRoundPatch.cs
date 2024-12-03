using System.Collections.Generic;
using System.Text.RegularExpressions;
using GameNetcodeStuff;
using HarmonyLib;

namespace LethalGargoyles.src.Patch
{
    [HarmonyPatch(typeof(StartOfRound))]
    public static class GetDeathCauses
    {
        public static List<(string playerName, string causeOfDeath)> previousRoundDeaths = [];
        
        [HarmonyPatch("ShipLeave")]
        [HarmonyPostfix]
#pragma warning disable IDE0051 // Remove unused private members
        static void PostFixShipLeave()
#pragma warning restore IDE0051 // Remove unused private members
        {
            Plugin.Logger.LogInfo("Getting Causes of Death.");
            previousRoundDeaths.Clear();
            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            if (player.isPlayerDead)
            {
                if (Plugin.Instance.IsCoronerLoaded)
                {
                    string? causeOfDeathString = SoftDepends.CoronerClass.CoronerGetCauseOfDeath(player);
                    previousRoundDeaths.Add((player.playerUsername, causeOfDeathString ?? player.causeOfDeath.ToString() ?? "Unknown"));
                    Plugin.Logger.LogInfo($"{player.playerUsername}'s cause of death this round was {causeOfDeathString ?? player.causeOfDeath.ToString()}");
                }
                else
                {
                    previousRoundDeaths.Add((player.playerUsername, player.causeOfDeath.ToString()));
                    Plugin.Logger.LogInfo($"{player.playerUsername}'s cause of death this round was {player.causeOfDeath.ToString()}");
                }
            }
        }
    }
}