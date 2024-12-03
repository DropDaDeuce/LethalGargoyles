using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Coroner;
using GameNetcodeStuff;

namespace LethalGargoyles.src.SoftDepends
{

    internal class CoronerClass
    {
        public static Object? GargoyleDeath { get; private set; } = null;

        // Initialize Coroner integration
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void Init()
        {
            try
            {
                // Invoke the method with the existing 'player' object
                GargoyleDeath = API.Register("DeathEnemyLGargoyle");

                Plugin.Logger.LogInfo($"Gargoyle cause of death registered with Coroner.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Skipping Coroner initialization. Exception: {ex}");
            }
        }

        internal static void CoronerSetCauseOfDeath(PlayerControllerB player)
        {
            Coroner.API.SetCauseOfDeath(player, (AdvancedCauseOfDeath?)GargoyleDeath);
        }

        internal static string? CoronerGetCauseOfDeath(PlayerControllerB player)
        {
            string? causeOfDeathString = null;
            AdvancedCauseOfDeath? nullableCauseOfDeath = API.GetCauseOfDeath(player);
            if (nullableCauseOfDeath != null)
            {
                AdvancedCauseOfDeath causeOfDeath = (AdvancedCauseOfDeath)nullableCauseOfDeath;
                causeOfDeathString = Regex.Replace(causeOfDeath.GetLanguageTag(), "Death", "");
                return causeOfDeathString;
            }
            else return null;
        }
    }
}
