using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GameNetcodeStuff;
using LethalGargoyles.src.Patch;
using LethalGargoyles.src.Utility;
using Unity.Netcode;
using UnityEngine;
using LethalGargoyles.src.SoftDepends;
using HarmonyLib;
using System.Reflection;
using UnityEngine.AI;
using System.IO;

namespace LethalGargoyles.src.Enemy
{
    [HarmonyPatch(typeof(DoorLock), "OnTriggerStay")]
    public class HarmonyDoorPatch
    {
        [HarmonyPostfix]
        static void PostFixOnTriggerStay(DoorLock __instance, Collider other)
        {
            if (other == null) {return; }
            if (other.GetComponent<EnemyAICollisionDetect>() == null) { return; }
            if (other.GetComponent<EnemyAICollisionDetect>().mainScript == null) { return; }

            if (other.GetComponent<EnemyAICollisionDetect>().mainScript is LethalGargoylesAI gargoyles)
            {
                // Get the enemyDoorMeter field using reflection
                FieldInfo enemyDoorMeterField = typeof(DoorLock).GetField("enemyDoorMeter", BindingFlags.NonPublic | BindingFlags.Instance);

                if (enemyDoorMeterField != null)
                {
                    // Get the value of the enemyDoorMeter field
                    float enemyDoorMeter = (float)enemyDoorMeterField.GetValue(__instance);
                    if (enemyDoorMeter <= 0f)
                    {
                        if (!gargoyles.openedDoors.ContainsKey(__instance))
                        {
                            gargoyles.openedDoors.Add(__instance, Time.time);
                        }
                    }
                }
                else
                {
                    // Handle the case where the field is not found
                    Plugin.Logger.LogWarning("enemyDoorMeter field not found in DoorLock.");
                }
            }
        }
    }

    public class LethalGargoylesAI : EnemyAI
    {
#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
#pragma warning restore 0649

        public static LethalGargoylesAI? LGInstance { get; private set; }
        public readonly Dictionary<DoorLock, float> openedDoors = [];

        PlayerControllerB closestPlayer = null!;
        PlayerControllerB aggroPlayer = null!;
        string? matPlayerIsOn;

        private float randGenTauntTime = 0f;
        private float randAgrTauntTime = 0f;
        private float randEnemyTauntTime = 0f;

        private float lastGenTauntTime = 0f;
        private float lastAgrTauntTime = 0f;
        private float lastEnemyTauntTime = 0f;

        private int lastGenTaunt = -1;
        private int lastAgrTaunt = -1;

        private int genTauntCount;

        private float lastAttackTime;
        private float distanceToPlayer = 0f;
        private float distanceToClosestPlayer = 0f;
        private float idleDistance = 0f;
        private string? lastEnemy = null;
        private bool isAllPlayersDead = false;
        private bool isSeen;
        private bool canSeePlayer;
        private bool targetSeesGargoyle;
        private float pushTimer = 0f;
        private int pushStage = 0;
        private float targetTimer = 0f;
        public AISearchRoutine? searchForPlayers;

        private float baseSpeed = 0f;
        private float attackRange = 0f;
        private int attackDamage = 0;
        private float aggroRange = 0f;
        private int minTaunt = 0;
        private int maxTaunt = 0;
        private float distWarn = 0f;
        private float bufferDist = 0f;
        private float awareDist = 0f;
        private bool enablePush = false;
  
        enum State
        {
            SearchingForPlayer,
            StealthyPursuit,
            GetOutOfSight,
            AggressivePursuit,
            Idle,
            PushTarget,
        }

        public enum RelativeZone
        {
            Front,
            FrontRight,
            Right,
            BackRight,
            Back,
            BackLeft,
            Left,
            FrontLeft
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start()
        {
            base.Start();
            LGInstance = this;
            LogIfDebugBuild("Gargoyle Spawned");
            DoAnimationClientRpc("startWalk");

            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            StartSearch(transform.position);

            baseSpeed = Plugin.BoundConfig.baseSpeed.Value;
            attackRange = Plugin.BoundConfig.attackRange.Value;
            attackDamage = Plugin.BoundConfig.attackDamage.Value;
            aggroRange = Plugin.BoundConfig.aggroRange.Value;
            idleDistance = Plugin.BoundConfig.idleDistance.Value;
            minTaunt = Plugin.BoundConfig.minTaunt.Value;
            maxTaunt = Plugin.BoundConfig.maxTaunt.Value;
            distWarn = Plugin.BoundConfig.distWarn.Value;
            bufferDist = Plugin.BoundConfig.bufferDist.Value;
            awareDist = Plugin.BoundConfig.awareDist.Value;
            enablePush = Plugin.BoundConfig.enablePush.Value;
            lastAttackTime = Time.time;
            pushTimer = Time.time;

            creatureVoice.maxDistance *= 3;
        }

        public override void Update()
        {
            base.Update();
            isAllPlayersDead = StartOfRound.Instance.allPlayersDead;
            if (isEnemyDead || isAllPlayersDead) return;

            if (targetPlayer == null)
            {
                FoundClosestPlayerInRange();
            }
            else
            {
                matPlayerIsOn = StartOfRound.Instance.footstepSurfaces[targetPlayer.currentFootstepSurfaceIndex].surfaceTag;
            }

            if (LGInstance != null)
            {
                List<DoorLock> doorsToRemove = [];

                foreach (DoorLock door in openedDoors.Keys)
                {
                    if (openedDoors.TryGetValue(door, out float openTime) && Time.time - openTime >= 0.75f)
                    {
                        float doorDist = Vector3.Distance(door.transform.position, transform.position);
                        bool isDoorOpen = door.GetComponent<AnimatedObjectTrigger>().boolValue;
                        bool isLocked = door.isLocked;

                        if (doorDist > 4f || (doorDist > 2.5f && currentBehaviourStateIndex == (int)State.Idle) && !isLocked && isDoorOpen)
                        {
                            door.gameObject.GetComponent<AnimatedObjectTrigger>().TriggerAnimationNonPlayer(LGInstance.useSecondaryAudiosOnAnimatedObjects, overrideBool: true);
                            door.CloseDoorNonPlayerServerRpc();
                            doorsToRemove.Add(door);
                            //if (previousAnimation != null) { DoAnimationClientRpc(previousAnimation); }
                        }
                    }
                }

                foreach (DoorLock door in doorsToRemove)
                {
                    openedDoors.Remove(door);
                }
            }

            closestPlayer = GetClosestPlayer();
            distanceToPlayer = targetPlayer != null ? Vector3.Distance(transform.position, targetPlayer.transform.position) : 0f;
            distanceToClosestPlayer = closestPlayer != null ? Vector3.Distance(transform.position, closestPlayer.transform.position) : 0f;

            if (pushStage < 1 || (matPlayerIsOn != null && !matPlayerIsOn.StartsWith("Catwalk")))
            {
                pushStage = 0;
                if (distanceToClosestPlayer <= awareDist)
                {
                    isSeen = GargoyleIsSeen(transform);

                    if (distanceToClosestPlayer > aggroRange)
                    {
                        randAgrTauntTime = Time.time - lastAgrTauntTime;
                    }

                    if (distanceToClosestPlayer <= aggroRange && isSeen)
                    {
                        SwitchToBehaviourClientRpc((int)State.AggressivePursuit);
                    }
                    else if (distanceToClosestPlayer <= attackRange && !isSeen && closestPlayer != null && currentBehaviourStateIndex != (int)State.AggressivePursuit && enablePush)
                    {
                        PushPlayer(closestPlayer);
                    }
                    else if (distanceToClosestPlayer > aggroRange)
                    {
                        if (isSeen) // Seen but outside aggro range
                        {
                            SwitchToBehaviourClientRpc((int)State.GetOutOfSight);
                        }
                        else if (distanceToPlayer <= idleDistance && targetPlayer != null) // Priority #2: Idle if within idle range
                        {
                            SwitchToBehaviourClientRpc((int)State.Idle);
                        }
                        else if (targetPlayer != null) // Priority #2: Follow if outside idle range
                        {
                            SwitchToBehaviourClientRpc((int)State.StealthyPursuit);
                        }
                        else if (currentBehaviourStateIndex != (int)State.SearchingForPlayer)
                        {
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        }
                    }

                    if (!targetSeesGargoyle && targetPlayer != null && currentBehaviourStateIndex != (int)State.AggressivePursuit && Time.time > pushTimer && enablePush)
                    {
                        if (matPlayerIsOn == "Catwalk")
                        {
                            SwitchToBehaviourClientRpc((int)State.PushTarget);
                        }
                    }
                }
                else // No players within awareness range, including targetPlayer
                {
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    targetPlayer = null; // Clear targetPlayer
                }
            }

            bool foundSpot;
            switch (currentBehaviourStateIndex)
            {
                case (int)State.Idle:
                    agent.speed = 0f;
                    creatureSFX.volume = 0f;
                    agent.autoBraking = true;
                    if (targetPlayer != null)
                    {
                        LookAtTarget(targetPlayer.transform.position);
                        if (Time.time - lastGenTauntTime >= randGenTauntTime)
                        {
                            Taunt();
                        }
                        else if (Time.time - lastEnemyTauntTime >= randEnemyTauntTime)
                        {
                            EnemyTaunt();
                        }
                    }
                    break;
                case (int)State.SearchingForPlayer:
                    agent.speed = baseSpeed * 1.5f;
                    creatureSFX.volume = 1f;
                    SearchForPlayers();
                    if (FoundClosestPlayerInRange())
                    {
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.StealthyPursuit);
                    }
                    break;
                case (int)State.StealthyPursuit:
                    agent.speed = baseSpeed;
                    creatureSFX.volume = 0.5f;
                    agent.autoBraking = true;
                    foundSpot = SetDestinationToHiddenPosition();
                    if (targetPlayer != null)
                    {
                        if (!foundSpot)
                        {
                            SetDestinationToPosition(targetPlayer.transform.position);
                        }

                        if (Time.time - lastGenTauntTime >= randGenTauntTime)
                        {
                            Taunt();
                        }
                        else if (Time.time - lastEnemyTauntTime >= randEnemyTauntTime)
                        {
                            EnemyTaunt();
                        }
                    }
                    break;
                case (int)State.AggressivePursuit:
                    agent.speed = baseSpeed * 2.5f;
                    creatureSFX.volume = 1.7f;
                    agent.autoBraking = false;
                    if (closestPlayer != null)
                    {
                        aggroPlayer = closestPlayer;
                        canSeePlayer = CanSeePlayer(aggroPlayer);

                        if (Time.time - lastAgrTauntTime >= randAgrTauntTime)
                        {
                            OtherTaunt("aggro", ref lastAgrTaunt, ref lastAgrTauntTime, ref randAgrTauntTime);
                        }

                        LookAtTarget(aggroPlayer.transform.position);
                        SetDestinationToPosition(aggroPlayer.transform.position);
                        
                        if (Time.time - lastAttackTime >= 1f && canSeePlayer && attackRange >= distanceToClosestPlayer)
                        {
                            AttackPlayer(aggroPlayer);
                        }
                    }
                    break;
                case (int)State.GetOutOfSight:
                    agent.speed = baseSpeed * 1.5f;
                    creatureSFX.volume = 1f;
                    agent.autoBraking = true;
                    foundSpot = SetDestinationToHiddenPosition();
                    if (targetPlayer != null)
                    {
                        if (Time.time - lastGenTauntTime >= randGenTauntTime)
                        {
                            Taunt();
                        }
                        if (!foundSpot)
                        {
                            SetDestinationToPosition(targetPlayer.transform.position);
                        }
                    }
                    break;
                case (int)State.PushTarget:
                    agent.speed = baseSpeed * 2.5f;
                    creatureSFX.volume = 1.7f;
                    agent.angularSpeed = 360f;
                    agent.stoppingDistance = 0.1f;
                    agent.autoBraking = true;

                    if (targetPlayer != null)
                    {
                        canSeePlayer = CanSeePlayer(targetPlayer);
                        //LogIfDebugBuild($"Distance To Player: {distanceToPlayer} | Target Sees Gargoyle: {targetSeesGargoyle} | Can See Player: {canSeePlayer} | Push Stage: {pushStage} | Push Timer: {pushTimer}");

                        if (distanceToPlayer <= attackRange && (!targetSeesGargoyle || pushStage == 1))
                        {
                            PushPlayer(targetPlayer);
                            pushStage = 0;
                            pushTimer = Time.time + 5f;
                            SwitchToBehaviourClientRpc((int)State.StealthyPursuit);
                        }

                        if (pushStage < 1)
                        {
                            if (distanceToPlayer <= aggroRange * 1.5 && !targetSeesGargoyle && canSeePlayer)
                            {
                                pushStage = 1;
                                SetDestinationToPosition(targetPlayer.transform.position);
                                LogIfDebugBuild("Push Stage = 1!");
                            }
                        }
                        else
                        {
                            SetDestinationToPosition(targetPlayer.transform.position);
                            LogIfDebugBuild("Push Stage = 1!");
                        }
                    }
                    break;
                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.StealthyPursuit:
                case (int)State.SearchingForPlayer:
                    if (agent.hasPath)
                    {
                        DoAnimationClientRpc("startWalk");
                    }
                    else
                    {
                        DoAnimationClientRpc("startIdle");
                    }
                    break;
                case (int)State.GetOutOfSight:
                    if (agent.hasPath)
                    {
                        DoAnimationClientRpc("startWalk");
                    }
                    else
                    {
                        DoAnimationClientRpc("startIdle");
                    }
                    break;
                case (int)State.Idle:
                    DoAnimationClientRpc("startIdle");
                    break;
                case (int)State.AggressivePursuit:
                    if (agent.hasPath)
                    {
                        DoAnimationClientRpc("startChase");
                    }
                    else
                    {
                        DoAnimationClientRpc("startIdle");
                    }
                    break;
                case (int)State.PushTarget:
                    if (Time.time - targetTimer > 0.5f)
                    {
                        // Log the condition that should trigger EvaluatePath
                        LogIfDebugBuild($"Attempting to evaluate path. Distance to player: {distanceToPlayer}");

                        Vector3 targetPosition = GetTargetPosition(targetPlayer);
                        SetDestinationToPosition(targetPosition, true);
                        // Log whether EvaluatePath was called
                        LogIfDebugBuild($"Current position: {transform.position}");
                        LogIfDebugBuild($"Distance to target: {agent.remainingDistance}");
                        LogIfDebugBuild($"Evaluated path. New target position: {agent.destination}");
                        targetTimer = Time.time;
                    }
                    if (agent.hasPath)
                    {
                        DoAnimationClientRpc("startChase");
                    }
                    else
                    {
                        DoAnimationClientRpc("startIdle");
                    }
                    break;
                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        }

        EnemyAI? EnemyNearGargoyle()
        {
            if (targetPlayer != null)
            {
                EnemyAI[] enemies = FindObjectsOfType<EnemyAI>();

                foreach (EnemyAI enemy in enemies)
                {
                    float distance = Vector3.Distance(enemy.transform.position, transform.position);
                    if (distance <= distWarn)
                    {
                        return enemy;
                    }
                }
            }
            return null;
        }

        bool FoundClosestPlayerInRange()
        {
            PlayerControllerB? closestPlayerInsideFactory = null;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
            {
                PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[i];

                if (!isOutside)
                {
                    if (player.isInsideFactory)
                    {
                        float distance = Vector3.Distance(transform.position, player.transform.position);
                        if (distance < closestDistance)
                        {
                            closestPlayerInsideFactory = player;
                            closestDistance = distance;
                        }
                    }
                }
                else
                {
                    if (!player.isInsideFactory)
                    {
                        float distance = Vector3.Distance(transform.position, player.transform.position);
                        if (distance < closestDistance)
                        {
                            closestPlayerInsideFactory = player;
                            closestDistance = distance;
                        }
                    }
                }
                
            }

            if (closestPlayerInsideFactory != null)
            {
                targetPlayer = closestPlayerInsideFactory;
                // LogIfDebugBuild("Found You!");
                return true;
            }
            else
            {
                // LogIfDebugBuild("Where are you?");
                return false;
            }
        }

        public void SearchForPlayers()
        {
            //Do Stuff
            if (searchForPlayers != null)
            {
                if (!searchForPlayers.inProgress)
                {
                    StartSearch(transform.position, searchForPlayers);
                }
            }
        }

        bool SetDestinationToHiddenPosition()
        {
            Transform? bestCoverPoint = null;
            List<Transform> coverPoints = [];

            FindCoverPointsAroundTarget(ref coverPoints);

            if (coverPoints.Count > 0)
            {
                bestCoverPoint = ChooseBestCoverPoint(coverPoints);
            }

            if (bestCoverPoint != null)
            {
                agent.SetDestination(bestCoverPoint.position);
                // LogIfDebugBuild("Found a hiding spot!");
                return true;
            }
            else
            {
                // LogIfDebugBuild("No suitable hiding spot found.");
                return false;
            }
        }

        public void FindCoverPointsAroundTarget(ref List<Transform> coverPoints)
        {
            for (int i = 0; i < allAINodes.Length; i++)
            {
                Vector3 pos = allAINodes[i].transform.position;

                Bounds playerBounds = new(targetPlayer.transform.position, new Vector3(40, 20, 40));
                Bounds gargoyleBounds = new(transform.position, new Vector3(40, 20, 40));
                if (gargoyleBounds.Contains(pos) || playerBounds.Contains(pos))
                {
                    bool isSafe = true;
                    foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
                    {
                        if (playerBounds.Contains(player.transform.position) || gargoyleBounds.Contains(player.transform.position))
                        {
                            if (player.HasLineOfSightToPosition(pos, 60f, 60, 25f) || PathIsIntersectedByLineOfSight(pos, false, true))
                            {
                                isSafe = false;
                                break;
                            }
                        }
                    }

                    if (isSafe)
                    {
                        coverPoints.Add(allAINodes[i].transform);
                    }
                }
            }
            return;
        }

        public Transform? ChooseBestCoverPoint(List<Transform> coverPoints)
        {
            if (coverPoints.Count == 0)
            {
                return null;
            }

            Transform bestCoverPoint = coverPoints[0];
            Transform bestCoverPoint2 = coverPoints[0];

            float bestDistance = 40f;
            float bestDistance2 = 40f;

            foreach (Transform coverPoint in coverPoints)
            {
                float distance = Vector3.Distance(targetPlayer.transform.position, coverPoint.position);
                if (distance < bestDistance && distance >= bufferDist)
                {
                    bestCoverPoint = coverPoint;
                    bestDistance = distance;
                }
                else if (distance < bestDistance2 && distance >= aggroRange + 1f)
                {
                    bestCoverPoint2 = coverPoint;
                    bestDistance2 = distance;
                }
            }

            if (bestCoverPoint == coverPoints[0])
            {
                bestCoverPoint = bestCoverPoint2;
            }

            return bestCoverPoint;
        }

        private readonly Dictionary<RelativeZone, float> bufferDistances = new()
        {
            { RelativeZone.Front, 15f },
            { RelativeZone.FrontRight, 12f },
            { RelativeZone.Right, 10f },
            { RelativeZone.BackRight, 6f },
            { RelativeZone.Back, 3f }, // Reduced side distance for Back
            { RelativeZone.BackLeft, 6f },
            { RelativeZone.Left, 10f },
            { RelativeZone.FrontLeft, 12f },
        };

        private RelativeZone GetRelativeZone(PlayerControllerB player)
        {
            Vector3 playerPosition = player.transform.position;
            Vector3 aiPosition = transform.position;

            Vector3 directionToAI = aiPosition - playerPosition;

            // Use player's *local* right vector to get a consistent clockwise angle
            float signedAngle = Vector3.SignedAngle(player.transform.forward, directionToAI, player.transform.up);

            // Normalize the signed angle to 0-360 (clockwise from player's forward)
            if (signedAngle < 0)
            {
                signedAngle = 360 + signedAngle;
            }

            // Define angle ranges for each relative position (clockwise)
            if (signedAngle >= 337.5f || signedAngle < 22.5f) { LogIfDebugBuild($"Returned Front | Normalized Angle: {signedAngle}"); return RelativeZone.Front; }
            if (signedAngle >= 22.5f && signedAngle < 67.5f) { LogIfDebugBuild($"Returned FrontRight | Normalized Angle: {signedAngle}"); return RelativeZone.FrontRight; }
            if (signedAngle >= 67.5f && signedAngle < 112.5f) { LogIfDebugBuild($"Returned Right | Normalized Angle: {signedAngle}"); return RelativeZone.Right; }
            if (signedAngle >= 112.5f && signedAngle < 157.5f) { LogIfDebugBuild($"Returned BackRight | Normalized Angle: {signedAngle}"); return RelativeZone.BackRight; }
            if (signedAngle >= 157.5f && signedAngle < 202.5f) { LogIfDebugBuild($"Returned Back | Normalized Angle: {signedAngle}"); return RelativeZone.Back; }
            if (signedAngle >= 202.5f && signedAngle < 247.5f) { LogIfDebugBuild($"Returned BackLeft | Normalized Angle: {signedAngle}"); return RelativeZone.BackLeft; }
            if (signedAngle >= 247.5f && signedAngle < 292.5f) { LogIfDebugBuild($"Returned Left | Normalized Angle: {signedAngle}"); return RelativeZone.Left; }
            if (signedAngle >= 292.5f && signedAngle < 337.5f) { LogIfDebugBuild($"Returned FrontLeft | Normalized Angle: {signedAngle}"); return RelativeZone.FrontLeft; }

            LogIfDebugBuild("This log shouldn't happen... Returning front anyways.");
            return RelativeZone.Front; // Default case
        }

        private readonly Dictionary<RelativeZone, Vector3> RelativeZones = [];
        private RelativeZone currentZone;
        private RelativeZone nextZoneRight;
        private RelativeZone nextZoneLeft;

        private Vector3 GetTargetPosition(PlayerControllerB player)
        {
            bool rightPath = false;
            bool leftPath = false;
            bool getUnstuck = false;
            if (distanceToPlayer > 20f)
            {
                return targetPlayer.transform.position;
            }
            currentZone = GetRelativeZone(player);
            if (currentZone == RelativeZone.Back ||
                currentZone == RelativeZone.BackRight ||
                currentZone == RelativeZone.BackLeft)
            {
                return targetPlayer.transform.position;
            }
            if (agent.remainingDistance < 2f) getUnstuck = true;

            if (RelativeZones.Count == 0 || currentZone == nextZoneLeft || currentZone == nextZoneRight || getUnstuck) GetBufferPositions(player.transform.position);

            nextZoneRight = GetNextZone(currentZone, 1);
            nextZoneLeft = GetNextZone(currentZone, -1);

            LogIfDebugBuild($"Current Zone: {RelativeZoneToString(currentZone)} | Next Right Zone {nextZoneRight} | Next Left Zone {nextZoneLeft} ");

            leftPath = CheckZonePosition("Left");
            rightPath = CheckZonePosition("Right");

            if ((rightPath && RelativeZoneToString(currentZone).Contains("Right")) || rightPath && !leftPath)
            {
                LogIfDebugBuild("Going Right");
                return RelativeZones[nextZoneRight];
            }
            else if ((leftPath && RelativeZoneToString(currentZone).Contains("Left")) || (leftPath && !rightPath))
            {
                LogIfDebugBuild("Going Left");
                return RelativeZones[nextZoneLeft];
            }
            else
            {
                LogIfDebugBuild("Staying Still");
                return transform.position;
            }
        }

        private bool CheckZonePosition(string side)
        {
            if (side == "Right")
            {
                RelativeZone testZone = nextZoneRight;
                do
                {
                    if (RelativeZones[testZone] == Vector3.zero && testZone != RelativeZone.Front) { return false; }
                    LogIfDebugBuild($"Testing Right side: {RelativeZoneToString(testZone)} | Position: {RelativeZones[testZone]}");
                    testZone = GetNextZone(testZone, 1);
                    LogIfDebugBuild($"Calculating path from {RelativeZones[nextZoneRight]} to {RelativeZones[testZone]}");
                    NavMeshPath path = new();
                    if (!NavMesh.CalculatePath(RelativeZones[nextZoneRight], RelativeZones[testZone], NavMesh.AllAreas, path) && testZone != RelativeZone.Back && testZone != RelativeZone.Front)
                    {
                        LogIfDebugBuild($"Path calculation failed. Path status: {path.status}");
                        return false;
                    }
                    else
                    {
                        LogIfDebugBuild($"Path calculation successful. Path corners: {string.Join(", ", path.corners)}");
                    }
                } while (testZone != RelativeZone.Back);
            }
            else if (side == "Left")
            {
                RelativeZone testZone = nextZoneLeft;
                do
                {
                    if (RelativeZones[testZone] == Vector3.zero && testZone != RelativeZone.Front) { return false; }
                    LogIfDebugBuild($"Testing Left side: {RelativeZoneToString(testZone)} | Position: {RelativeZones[testZone]}");
                    testZone = GetNextZone(testZone, -1);
                    LogIfDebugBuild($"Calculating path from {RelativeZones[nextZoneLeft]} to {RelativeZones[testZone]}");
                    NavMeshPath path = new();
                    if (!NavMesh.CalculatePath(RelativeZones[nextZoneLeft], RelativeZones[testZone], NavMesh.AllAreas, path) && testZone != RelativeZone.Back && testZone != RelativeZone.Front)
                    {
                        LogIfDebugBuild($"Path calculation failed. Path status: {path.status}");
                        return false;
                    }
                    else
                    {
                        LogIfDebugBuild($"Path calculation successful. Path corners: {string.Join(", ", path.corners)}");
                    }
                } while (testZone != RelativeZone.Back);
            }

            return true;
        }

        private void GetBufferPositions(Vector3 playerPos)
        {
            RelativeZones.Clear();
            foreach (RelativeZone position in System.Enum.GetValues(typeof(RelativeZone)))
            {
                Vector3 bufferedPosition = GetBufferedPosition(playerPos, position);
                RelativeZones.Add(position, bufferedPosition);

                // Log the calculated position for each zone
                LogIfDebugBuild($"Zone: {RelativeZoneToString(position)}, Position: {bufferedPosition}");
            }
        }

        public Vector3 GetBufferedPosition(Vector3 playerPOS, RelativeZone position)
        {
            // Get the player's forward vector
            Vector3 playerForward = targetPlayer.transform.forward;

            // Calculate the direction vector for the given relative position
            Vector3 directionVector = GetDirectionVector(position, playerForward);

            // Get the buffer distance for the given relative position
            float distance = bufferDistances[position];

            // Calculate the buffered position
            Vector3 potentialPos = ValidateZonePosition(playerPOS + directionVector * distance);
            return potentialPos;
        }

        private Vector3 GetDirectionVector(RelativeZone zone, Vector3 playerForward)
        {
            switch (zone)
            {
                case RelativeZone.Front:
                    return playerForward;
                case RelativeZone.FrontRight:
                    return (playerForward + targetPlayer.transform.right).normalized;
                case RelativeZone.Right:
                    return targetPlayer.transform.right;
                case RelativeZone.BackRight:
                    return (-playerForward + targetPlayer.transform.right).normalized;
                case RelativeZone.Back:
                    return -playerForward;
                case RelativeZone.BackLeft:
                    return (-playerForward - targetPlayer.transform.right).normalized;
                case RelativeZone.Left:
                    return -targetPlayer.transform.right;
                case RelativeZone.FrontLeft:
                    return (playerForward - targetPlayer.transform.right).normalized;
                default:
                    return Vector3.zero;
            }
        }

        private Vector3 ValidateZonePosition(Vector3 position)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                return hit.position;
            }

            LogIfDebugBuild("Invalid Position");
            return Vector3.zero; // Return Vector3.zero to indicate an invalid position
        }

        string RelativeZoneToString (RelativeZone relativeZone)
        {
            return relativeZone switch
            {
                RelativeZone.Left => "Left",
                RelativeZone.Right => "Right",
                RelativeZone.BackRight => "BackRight",
                RelativeZone.BackLeft => "BackLeft",
                RelativeZone.FrontRight => "FrontRight",
                RelativeZone.FrontLeft => "FrontLeft",
                RelativeZone.Front => "Front",
                RelativeZone.Back => "Back",
                _ => "Unknown",
            };
        }

        // Helper function to get the next zone in a clockwise or counter-clockwise direction
        private RelativeZone GetNextZone(RelativeZone currentZone, int direction)
        {
            int nextZoneIndex = (int)currentZone + direction;
            if (nextZoneIndex > (int)RelativeZone.FrontLeft)
            {
                nextZoneIndex = 0;
            }
            else if (nextZoneIndex < 0)
            {
                nextZoneIndex = (int)RelativeZone.FrontLeft;
            }
            return (RelativeZone)nextZoneIndex;
        }

        bool GargoyleIsSeen(Transform t)
        {
            bool isSeen = false;
            bool partSeen = false;
            targetSeesGargoyle = false;

            Vector3[] gargoylePoints = [
                t.position + Vector3.up * 0.25f, // bottom
                t.position + Vector3.up * 1.3f, // top
                t.position + Vector3.left * 1.6f, // Left shoulder
                t.position + Vector3.right * 1.6f, // Right shoulder
                // Add more points as needed
            ];

            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                float distance = Vector3.Distance(player.transform.position, t.position);

                if (distance <= awareDist)
                {
                    if (!isOutside && player.isInsideFactory || isOutside)
                    {
                        foreach (Vector3 point in gargoylePoints)
                        {
                            if (player.HasLineOfSightToPosition(point, 68f))
                            {
                                partSeen = true;
                                break;
                            }
                        }

                        if (PlayerIsTargetable(player) && partSeen && PlayerHasHorizontalLOS(player))
                        {
                            isSeen = true;
                            if (targetPlayer != null)
                            {

                                if (player.playerUsername == targetPlayer.playerUsername)
                                {
                                    targetSeesGargoyle = true;
                                }
                            }
                        }
                    }
                }
            }
            //LogIfDebugBuild("GargoyleSeen: " + isSeen);
            return isSeen;
        }

        public bool PlayerHasHorizontalLOS(PlayerControllerB player)
        {
            Vector3 to = transform.position - player.transform.position;
            to.y = 0f;
            return Vector3.Angle(player.transform.forward, to) < 68f;
        }

        public bool CanSeePlayer(PlayerControllerB player, float width = 180f, int range = 60, int proximityAwareness = -1)
        {
            Vector3 position = player.gameplayCamera.transform.position;
            if (Vector3.Distance(position, eye.position) < range && !Physics.Linecast(eye.position, position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            {
                Vector3 to = position - eye.position;
                if (Vector3.Angle(eye.forward, to) <= width || proximityAwareness != -1 && Vector3.Distance(eye.position, position) < proximityAwareness)
                {
                    return true;
                }
            }
            return false;
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null)
            {
                //LogIfDebugBuild("Gargoyle Collision with Player!");
                if (Time.time - lastAttackTime >= 1f && CanSeePlayer(playerControllerB))
                {
                    AttackPlayer(playerControllerB);
                }
            }
        }

        public void AttackPlayer(PlayerControllerB player)
        {
            //LogIfDebugBuild("Attack!");
            LookAtTarget(player.transform.position);
            agent.speed = 0f;
            lastAttackTime = Time.time;
            DoAnimationClientRpc("swingAttack");
            PlayVoice(Utility.AudioManager.attackClips, "attack");
            player.DamagePlayer(attackDamage, false, true, CauseOfDeath.Bludgeoning);
            
            if (targetPlayer != null)
            {
                if (targetPlayer.isPlayerDead)
                {
                    if (Plugin.Instance.IsCoronerLoaded) SoftDepends.CoronerClass.CoronerSetCauseOfDeath(player, "Attack");
                    targetPlayer = null;
                    PlayVoice(Utility.AudioManager.playerDeathClips, "playerdeath");
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                }
            }
            player.JumpToFearLevel(1f);
        }

        public void PushPlayer(PlayerControllerB player)
        {
            //LogIfDebugBuild("Attack!");

            LookAtTarget(player.transform.position);
            agent.speed = 0f;
            lastAttackTime = Time.time;
            DoAnimationClientRpc("swingAttack");
            PlayVoice(Utility.AudioManager.attackClips, "attack");
            player.DamagePlayer(2, false, true, CauseOfDeath.Gravity);
            Vector3 pushDirection = player.transform.forward * 15f;
            player.externalForceAutoFade = pushDirection;

            if (targetPlayer != null)
            {
                if (targetPlayer.isPlayerDead)
                {
                    if (Plugin.Instance.IsCoronerLoaded) SoftDepends.CoronerClass.CoronerSetCauseOfDeath(player, "Push");
                    targetPlayer = null;
                    PlayVoice(Utility.AudioManager.playerDeathClips, "playerdeath");
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                }
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead)
            {
                return;
            }
            PlayVoice(Utility.AudioManager.hitClips, "hit");
            SwitchToBehaviourClientRpc((int)State.AggressivePursuit);            
            enemyHP -= force;
            if (IsOwner)
            {
                if (enemyHP <= 0 && !isEnemyDead)
                {
                    // We need to stop our search coroutine, because the game does not do that by default.
                    StopCoroutine(searchCoroutine);
                    KillEnemyOnOwnerClient();
                }
            }
        }

        public void LookAtTarget(Vector3 target)
        {
            Quaternion targetRotation = Quaternion.LookRotation(target - transform.position);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, 300f * Time.deltaTime);
        }

        public override void KillEnemy(bool destroy = false)
        {
            base.KillEnemy(destroy);
            Collider col = transform.GetComponent<Collider>();
            col.enabled = false;
            foreach (Collider collider in GetComponentsInChildren<Collider>())
            {
                collider.enabled = false;
            }

            {
                int randomIndex = UnityEngine.Random.Range(0, Utility.AudioManager.deathClips.Count());
                TauntClientRpc(Utility.AudioManager.deathClips[randomIndex].name, "death");
            }
        }

        public void PlayVoice(List<AudioClip> clipList, string clipType, AudioClip? clip = null)
        {
            if (clip != null)
            {
                return;
            }
            else if (clip == null && clipList != null)
            {
                int randInt = UnityEngine.Random.Range(0, clipList.Count());
                LogIfDebugBuild(clipType + " count is :" + clipList.Count() + " | Index: " + randInt);
                TauntClientRpc(clipList[randInt].name, clipType, true);
            }
        }

        public void Taunt()
        {
            string? priorCauseOfDeath = null;
            string? playerClass = null;
            int randSource = UnityEngine.Random.Range(1, 4);

            List<(string playerName, string causeOfDeath, string source)> priorDeathCauses = [];
            List<(string playerName, string causeOfDeath, string source)> getDeathCausesList = GetDeathCauses.previousRoundDeaths;

            if (targetPlayer != null)
            {
                for (int i = 0; i < getDeathCausesList.Count(); i++)
                {
                    if (getDeathCausesList[i].playerName.Equals(targetPlayer.playerUsername))
                    {
                        priorDeathCauses.Add(getDeathCausesList[i]);
                    }
                }

                for (int i = 0; i < priorDeathCauses.Count(); i++)
                {
                    string? playerName;
                    if ((randSource == 1 && priorDeathCauses[i].source == "Vanilla") || !Plugin.Instance.IsCoronerLoaded)
                    {
                        playerName = priorDeathCauses[i].playerName;
                        priorCauseOfDeath = priorDeathCauses[i].causeOfDeath;
                        LogIfDebugBuild($"{playerName}'s cause of death last round was {priorCauseOfDeath}");
                    }
                    else if (randSource != 1 && priorDeathCauses[i].source == "Coroner" && Plugin.Instance.IsCoronerLoaded)
                    {
                        playerName = priorDeathCauses[i].playerName;
                        priorCauseOfDeath = priorDeathCauses[i].causeOfDeath;
                        LogIfDebugBuild($"{playerName}'s cause of death last round was {priorCauseOfDeath}");
                    }
                }

                if (Plugin.Instance.IsEmployeeClassesLoaded)
                {
                    foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
                    {
                        if (targetPlayer.playerUsername == player.playerUsername)
                        {
                            playerClass = EmployeeClassesClass.GetPlayerClass(player);
                            break;
                        }
                    }
                }
            }

            int randInt;
            if (genTauntCount >= 10 && playerClass != null && priorCauseOfDeath != null)
            {
                randInt = UnityEngine.Random.Range(165, 200);
            }
            else if (genTauntCount >= 15 && (playerClass != null || priorCauseOfDeath != null))
            {
                randInt = UnityEngine.Random.Range(165, 200);
            }
            else
            {
                randInt = UnityEngine.Random.Range(1, 200);
            }

            LogIfDebugBuild($"Random Taunt Number: {randInt} | # of general taunts: {genTauntCount}");

            if (randInt < 175)
            {
                OtherTaunt("general", ref lastGenTaunt, ref lastGenTauntTime, ref randGenTauntTime);
                genTauntCount++;
            }
            else if (randInt < 180)
            {
                OtherTaunt("enemy", ref lastGenTaunt, ref lastGenTauntTime, ref randGenTauntTime);
                genTauntCount = 0;
            }
            else if(randInt < 190 && priorCauseOfDeath != null && !GargoyleIsTalking())
            {
                string? randClip = ChooseRandomClip("taunt_priordeath_" + priorCauseOfDeath, "PriorDeath");
                if (randClip == null) { Plugin.Logger.LogError($"Clip missing for {priorCauseOfDeath} death."); return; }
                TauntClientRpc(randClip, "priordeath");
                genTauntCount = 0;
            } 
            else if (playerClass != null && !GargoyleIsTalking())
            {
                string? randClip = ChooseRandomClip("taunt_employeeclass_" + playerClass, "Class");
                if (randClip == null) { Plugin.Logger.LogError($"Clip missing for {playerClass} class."); return; }
                TauntClientRpc(randClip, "class");
                genTauntCount = 0;
            }
            else if (!GargoyleIsTalking())
            {
                OtherTaunt("general", ref lastGenTaunt, ref lastGenTauntTime, ref randGenTauntTime);
                genTauntCount++;
            }
        }

        public void OtherTaunt(string clipType, ref int lastTaunt, ref float lastTauntTime, ref float randTime)
        {
            List<AudioClip> clipList = [];

            switch (clipType)
            {
                case "general":
                    clipList = Utility.AudioManager.tauntClips;
                    break;
                case "aggro":
                    clipList = Utility.AudioManager.aggroClips;
                    break;
                case "death":
                    clipList = Utility.AudioManager.deathClips;
                    break;
                case "enemy":
                    clipList = Utility.AudioManager.enemyClips;
                    break;
            }

            if (clipList.Count > 0)
            {
                if (!GargoyleIsTalking())
                {
                    // Play a random taunt clip
                    int randomIndex;
                    do
                    {
                        randomIndex = UnityEngine.Random.Range(0, clipList.Count());
                    } while (randomIndex == lastTaunt);

                    lastTaunt = randomIndex;
                    TauntClientRpc(clipList[randomIndex].name, clipType);
                    lastTauntTime = Time.time;
                    randTime = UnityEngine.Random.Range(minTaunt, maxTaunt);
                } 
                else
                {
                    lastTauntTime = Time.time;
                    randTime = UnityEngine.Random.Range((int)(minTaunt/2), (int)(maxTaunt/2));
                }
            }
            else
            {
                LogIfDebugBuild(clipType + " TAUNTS ARE NULL! WHY!?");
                return;
            }
        }

        public void EnemyTaunt()
        {           
            if (!GargoyleIsTalking())
            {
                EnemyAI? enemy = EnemyNearGargoyle();

                if (enemy != null)
                {
                    if (enemy.enemyType.enemyName != lastEnemy)
                    {
                        {
                            string? clip = null;
                            switch (enemy.enemyType.enemyName.ToUpper())
                            {
                                case "BLOB":
                                    clip = "taunt_enemy_Slime";
                                    break;
                                case "BUTLER":
                                    clip = "taunt_enemy_Butler";
                                    break;
                                case "CENTIPEDE":
                                    clip = "taunt_enemy_Centipede";
                                    break;
                                case "GIRL":
                                    clip = "taunt_enemy_GhostGirl";
                                    break;
                                case "HOARDINGBUG":
                                    clip = "taunt_enemy_HoardingBug";
                                    break;
                                case "JESTER":
                                    clip = "taunt_enemy_Jester";
                                    break;
                                case "MANEATER":
                                    clip = "taunt_enemy_Maneater";
                                    break;
                                case "MASKED":
                                    clip = "taunt_enemy_Masked";
                                    break;
                                case "CRAWLER":
                                    clip = "taunt_enemy_Thumper";
                                    break;
                                case "BUNKERSPIDER":
                                    clip = "taunt_enemy_Spider";
                                    break;
                                case "SPRING":
                                    clip = "taunt_enemy_SpringHead";
                                    break;
                                case "NUTCRACKER":
                                    clip = "taunt_enemy_Nutcracker";
                                    break;
                                case "FLOWERMAN":
                                    clip = "taunt_enemy_Bracken";
                                    break;
                            }

                            int randInt = UnityEngine.Random.Range(1, 100);
                            if (clip != null && randInt < 3)
                            {
                                LogIfDebugBuild(enemy.enemyType.enemyName);
                                lastEnemy = enemy.enemyType.enemyName;
                                string? randomClip = ChooseRandomClip(clip, "Enemy");
                                if (randomClip == null ) { return; }
                                TauntClientRpc(randomClip, "enemy");
                                lastEnemyTauntTime = Time.time;
                                randEnemyTauntTime = UnityEngine.Random.Range((int)(minTaunt), (int)(maxTaunt));
                            }
                        }
                    }
                }
            }
        }

        AudioClip? FindClip(string clipName, List<AudioClip> clips)
        {
            foreach (AudioClip clip in clips)
            {
                if (clip.name.StartsWith(clipName))
                {
                    return clip;
                }
            }
            return null;
        }

        string? ChooseRandomClip(string clipName, string listName)
        {
            List<AudioClip> tempList = [];
            List<AudioClip> clipList = AudioManager.GetClipListByCategory(listName);

            foreach (AudioClip clip in clipList)
            {
                if (clip.name.StartsWith(clipName))
                {
                    tempList.Add(clip);
                }
            }

            if (tempList.Count < 0) { return null; }

            int intRand = UnityEngine.Random.Range(0, tempList.Count);
            return tempList[intRand].name;
        }

        public bool GargoyleIsTalking()
        {
            EnemyAI[] enemies = FindObjectsOfType<EnemyAI>();

            foreach (EnemyAI enemy in enemies)
            {
                if (enemy.enemyType.enemyName.ToUpper() == "LETHALGARGOYLE")
                {
                    if (enemy.creatureVoice.isPlaying)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        [ClientRpc]
        private void TauntClientRpc(string clipName, string clipType, bool stop = false)
        {
            List<AudioClip> clipList = [];

            if (stop && creatureVoice.isPlaying)
            {
                creatureVoice.Stop();
            }

            switch (clipType)
            {
                case "general":
                    clipList = Utility.AudioManager.tauntClips; break;
                case "enemy":
                    clipList = Utility.AudioManager.enemyClips; break;
                case "aggro":
                    clipList = Utility.AudioManager.aggroClips; break;
                case "death":
                    clipList = Utility.AudioManager.deathClips; break;
                case "attack":
                    clipList = Utility.AudioManager.attackClips; break;
                case "hit":
                    clipList = Utility.AudioManager.hitClips; break;
                case "priordeath":
                    clipList = Utility.AudioManager.priorDeathClips; break;
                case "playerdeath":
                    clipList = Utility.AudioManager.playerDeathClips; break;
                case "class":
                    clipList = Utility.AudioManager.classClips; break;
            }

            AudioClip? clip = FindClip(clipName, clipList);

            if (clipList.Count > 0 && clip != null)
            {
                LogIfDebugBuild(clipType + " taunt: " + clip.name);
                creatureVoice.PlayOneShot(clip);
            }
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            //LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

    }
}