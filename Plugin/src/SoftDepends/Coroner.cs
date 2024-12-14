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
        public static Object? GargoylePushDeath { get; private set; } = null;

        // Initialize Coroner integration
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void Init()
        {
            try
            {
                // Invoke the method with the existing 'player' object
                GargoyleDeath = API.Register("DeathEnemyLGargoyle");
                GargoylePushDeath = API.Register("DeathEnemyGargoylePush");

                Plugin.Logger.LogInfo($"Gargoyle causes of death registered with Coroner.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Skipping Coroner initialization. Exception: {ex}");
            }
        }

        internal static void CoronerSetCauseOfDeath(PlayerControllerB player, string deathType)
        {
            switch (deathType)
            {
                case "Attack":
                    Coroner.API.SetCauseOfDeath(player, (AdvancedCauseOfDeath?)GargoyleDeath);
                    break;
                case "Push":
                    Coroner.API.SetCauseOfDeath(player, (AdvancedCauseOfDeath?)GargoylePushDeath);
                    break;
            }
            
        }

        internal static string? CoronerGetCauseOfDeath(PlayerControllerB player)
        {
            AdvancedCauseOfDeath? nullableCauseOfDeath = API.GetCauseOfDeath(player);
            if (nullableCauseOfDeath != null)
            {
                AdvancedCauseOfDeath causeOfDeath = (AdvancedCauseOfDeath)nullableCauseOfDeath;
                string? causeOfDeathString = Regex.Replace(causeOfDeath.GetLanguageTag(), "Death", "");
                return causeOfDeathString;
            }
            else return null;
        }
    }
}
