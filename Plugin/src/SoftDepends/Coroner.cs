using System;
using System.Diagnostics;
using System.Reflection;
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
                    if (GargoyleDeath != null) { SetCauseOfDeath((int)player.playerClientId, (AdvancedCauseOfDeath)GargoyleDeath); }
                    break;
                case "Push":
                    if (GargoylePushDeath != null) { SetCauseOfDeath((int)player.playerClientId, (AdvancedCauseOfDeath)GargoylePushDeath); }
                    break;
            }
        }

        internal static void SetCauseOfDeath(int playerId, AdvancedCauseOfDeath cause)
        {
            Type advancedDeathTrackerType = typeof(Coroner.Plugin).Assembly.GetType("Coroner.AdvancedDeathTracker");
            if (advancedDeathTrackerType != null)
            {
                // 3. Get the SetCauseOfDeath method
                MethodInfo? setCauseOfDeathMethod = advancedDeathTrackerType.GetMethod(
                    "SetCauseOfDeath",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    [typeof(int), typeof(Coroner.AdvancedCauseOfDeath), typeof(bool)],
                    null
                );

                // 4. Invoke the method
                setCauseOfDeathMethod?.Invoke(null, [playerId, cause, true]);

                if (setCauseOfDeathMethod != null)
                {
#if DEBUG
                    Plugin.Logger.LogInfo("setCauseOfDeathMethod is null");
#endif
                }
            }
            else
            {
#if DEBUG
                Plugin.Logger.LogInfo("advancedDeathTrackerType is null");
#endif
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
