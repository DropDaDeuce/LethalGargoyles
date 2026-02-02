using GameNetcodeStuff;
using HarmonyLib;
using static LethalGargoyles.src.Enemy.LethalGargoylesAI.PlayerActivityTracker;
using static LethalGargoyles.src.Enemy.LethalGargoylesAI;
using Unity.Netcode;
using UnityEngine;
using LethalGargoyles.src.Enemy;
using System.Reflection;
using System.Collections.Generic;
using System.Collections;

namespace LethalGargoyles.src.Patch
{
    [HarmonyPatch(typeof(DoorLock), "OnTriggerStay")]
    public class HarmonyDoorPatch
    {
        private static readonly FieldInfo enemyDoorMeterField = typeof(DoorLock).GetField("enemyDoorMeter", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPostfix]
        static void PostFixOnTriggerStay(DoorLock __instance, Collider other)
        {
            if (other == null || other.GetComponent<EnemyAICollisionDetect>() == null || other.GetComponent<EnemyAICollisionDetect>().mainScript == null)
            {
                return;
            }

            if (other.GetComponent<EnemyAICollisionDetect>().mainScript is LethalGargoylesAI gargoyles)
            {
                if (enemyDoorMeterField != null)
                {
                    float enemyDoorMeter = (float)enemyDoorMeterField.GetValue(__instance);
                    if (enemyDoorMeter <= 0f && gargoyles.currentDoor == null) // Check if currentDoor is null
                    {
                        gargoyles.currentDoor = __instance;  // Assign the door
                        gargoyles.lastDoorCloseTime = Time.time; // Reset the timer
                    }
                }
                else
                {
                    Plugin.Logger.LogWarning("enemyDoorMeter field not found in DoorLock.");
                }
            }
        }
    }

    [HarmonyPatch(typeof(EnemyAI), "HitEnemy")]
    public class KillEnemyPatch
    {
        [HarmonyPostfix]
        static void Postfix(EnemyAI __instance, PlayerControllerB? playerWhoHit)
        {
            if (playerWhoHit != null)
            {
                __instance.StartCoroutine(KillEnemyHelper.KillEnemy(__instance, playerWhoHit));
            }
        }
    }

    public class KillEnemyHelper
    {
        public static IEnumerator KillEnemy(EnemyAI enemyAI, PlayerControllerB playerWhoHit)
        {
            yield return new WaitForSeconds(1f); // Wait for the enemy to die
            if (enemyAI.isEnemyDead)
            {
                UpdatePlayerActivity(playerWhoHit, PlayerActivityType.KilledEnemy, enemyAI.enemyType.enemyName);
                Plugin.Instance.LogIfDebugBuild($"{playerWhoHit.playerUsername} killed enemy: {enemyAI.enemyType.enemyName}");
            }
            yield break; // Use yield break to return an empty IEnumerator
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "GrabObjectServerRpc")]
    public class GrabObjectServerRpcPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerControllerB __instance, ref NetworkObjectReference grabbedObject)
        {
            if (!__instance.IsServer) { return; }
            if (grabbedObject.TryGet(out var networkObject) && networkObject.GetComponentInChildren<GrabbableObject>() is GrabbableObject grabbableObject)
            {
                string itemName = grabbableObject.itemProperties.itemName;

                // Check if the item name is in the trackedItems set
                if (trackedItems.Contains(itemName))
                {
                    // Update player activity
                    UpdatePlayerActivity(__instance, PlayerActivityType.PickedUpItem, grabbableObject.itemProperties.itemName);
                    Plugin.Instance.LogIfDebugBuild(__instance.playerUsername + " picked up item: " + grabbableObject.itemProperties.itemName);
                }
            }
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "SetObjectAsNoLongerHeld")]
    public class SetObjectAsNoLongerHeldPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerControllerB __instance, GrabbableObject dropObject)
        {
            if (!__instance.IsServer) { return; }
            string itemName = dropObject.itemProperties.itemName;
            // Check if the item name is in the trackedItems set
            if (trackedItems.Contains(itemName))
            {
                // Update player activity
                RemoveActivity(__instance, PlayerActivityType.PickedUpItem, itemName);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "Update")]
    public class PlayerInFacilityPatch
    {
        private static readonly Dictionary<PlayerControllerB, float> playerEnterTimes = [];
        private static readonly Dictionary<PlayerControllerB, float> lastRanTimes = []; // Dictionary to store lastRan time for each player
        private const float MinimumTimeInFacility = 5f;
        private const float delay = 1f;

        [HarmonyPostfix]
        static void Postfix(PlayerControllerB __instance)
        {
            float lastRan = lastRanTimes.TryGetValue(__instance, out float lastRanTime) ? lastRanTime : 0f;
            if (Time.time - lastRan > delay /*&& !shipInOrbit*/)
            {
                if (__instance.isPlayerControlled && __instance.isInsideFactory && !__instance.isPlayerDead)
                {
                    //Plugin.Instance.LogIfDebugBuild(__instance.playerUsername + " is inside the facility.");
                    // Record entry time if not already recorded
                    if (!playerEnterTimes.ContainsKey(__instance))
                    {
                        playerEnterTimes[__instance] = Time.time;
                    }
                    else
                    {
                        // Check if the player has been inside for the minimum duration
                        float timeEntered = playerEnterTimes[__instance];
                        float timeInFacility = Time.time - timeEntered;

                        if (timeInFacility >= MinimumTimeInFacility * 60) // Convert minutes to seconds
                        {
                            UpdatePlayerActivity(__instance, PlayerActivityType.InFacility, "InFacilityTime", timeInFacility);
                            //Plugin.Instance.LogIfDebugBuild(__instance.playerUsername + " has been in the facility for " + timeInFacility + " seconds.");
                        }
                    }
                }
                else
                {
                    if (playerEnterTimes.ContainsKey(__instance))
                    {
                        // Remove player from the dictionary when they leave the facility
                        playerEnterTimes.Remove(__instance);
                    }
                    RemoveActivity(__instance, PlayerActivityType.InFacility);
                }
                lastRanTimes[__instance] = Time.time;
            }
        }
    }

    /*
    [HarmonyPatch(typeof(RoundManager))]
    public static class RoundManagerPatch
    {
        [HarmonyPatch("DespawnPropsAtEndOfRound")]
        [HarmonyPostfix]
        public static void DespawnPropsAtEndOfRound_Postfix()
        {
            // Ensure LGInstance is not null before calling ClearAllVariables
            if (LGInstance != null)
            {
                LGInstance.ClearAllVariables();
                shipInOrbit = true;
                Plugin.Instance.LogIfDebugBuild("LethalGargoylesAI variables cleared at the end of the round.");
            }
        }

        [HarmonyPatch("UnloadSceneObjectsEarly")]
        [HarmonyPostfix]
        public static void UnloadSceneObjectsEarly_Postfix()
        {
            // Ensure LGInstance is not null before calling ClearAllVariables
            if (LGInstance != null)
            {
                LGInstance.ClearAllVariables();
                shipInOrbit = true;
                Plugin.Instance.LogIfDebugBuild("LethalGargoylesAI variables cleared when unloading scene objects early.");
            }
        }

        [HarmonyPatch("SetLevelObjectVariables")]
        [HarmonyPostfix]
        public static void SetLevelObjectVariables_Postfix()
        {
            shipInOrbit = false;
            Plugin.Instance.LogIfDebugBuild("shipInOrbit set to false.");
        }
    } 
    */
}