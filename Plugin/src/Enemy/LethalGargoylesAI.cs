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
using System.Collections.Concurrent;
using System.Collections;

namespace LethalGargoyles.src.Enemy
{
    [HarmonyPatch(typeof(DoorLock), "OnTriggerStay")]
    public class HarmonyDoorPatch
    {
        [HarmonyPostfix]
        static void PostFixOnTriggerStay(DoorLock __instance, Collider other)
        {
            if (other == null || other.GetComponent<EnemyAICollisionDetect>() == null || other.GetComponent<EnemyAICollisionDetect>().mainScript == null)
            {
                return;
            }

            if (other.GetComponent<EnemyAICollisionDetect>().mainScript is LethalGargoylesAI gargoyles)
            {
                // Using reflection to get enemyDoorMeter
                FieldInfo enemyDoorMeterField = typeof(DoorLock).GetField("enemyDoorMeter", BindingFlags.NonPublic | BindingFlags.Instance);

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

    public class LethalGargoylesAI : EnemyAI
    {
#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
#pragma warning restore 0649

        public static LethalGargoylesAI? LGInstance { get; private set; }
        public DoorLock? currentDoor = null;
        public float lastDoorCloseTime = 0f;

        PlayerControllerB closestPlayer = null!;
        PlayerControllerB aggroPlayer = null!;
        //string? matPlayerIsOn;
        //string? objPlayerIsOn;

        private float randGenTauntTime = 0f;
        private float randAgrTauntTime = 0f;
        private float randEnemyTauntTime = 0f;

        private float lastGenTauntTime = 0f;
        private float lastAgrTauntTime = 0f;
        private float lastEnemyTauntTime = 0f;

        private int lastGenTaunt = -1;
        private int lastAgrTaunt = -1;

        private int genTauntCount;

        private static float lastNodeCheckTime = 0f;
        private readonly float nodeCheckInterval = 5f;
        private static List<GameObject> cachedOutsideAINodes = [];
        private static List<GameObject> cachedInsideAINodes = [];
        private static List<GameObject> cachedAllAINodes = [];
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
        public int myID;
        protected static ConcurrentDictionary<int, PlayerControllerB?> gargoyleTargets = []; // Use a static dictionary
        protected static ConcurrentDictionary<PlayerControllerB, ConcurrentDictionary<int, bool>> playerPushStates = [];
        private static int lastGargoyleToSwitch = 0; // Track the last gargoyle that switched targets
        private float playerCheckTimer = 0f;
        private float pathDelayTimer = 0f;
        private List<PlayerControllerB> validPlayers = [];
        private List<LethalGargoylesAI> gargoyles = [];
        private int previousStateIndex;
        //private Vector3 mainEntrancePos = default;
        private Transform? killTrigger;
        private float distToKillTrigger;

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

        string StateToString(int state)
        {
            return state switch
            {
                0 => "SearchingForPlayer",
                1 => "StealthyPusuit",
                2 => "GetOutOfSight",
                3 => "AggressivePursuit",
                4 => "Idle",
                5 => "PushTarget",
                _ => "Unknown",
            };
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
            myID = agent.GetInstanceID();
            gargoyleTargets[myID] = targetPlayer;
            creatureVoice.maxDistance *= 3;
            pathDelayTimer = Time.time;
            cachedOutsideAINodes = RoundManager.Instance.outsideAINodes.Where(node => node != null).ToList();
            cachedInsideAINodes = RoundManager.Instance.insideAINodes.Where(node => node != null).ToList();
            cachedAllAINodes = allAINodes.Where(node => node != null).ToList();
            LogIfDebugBuild($"Nodes Initialized | Node Counts: Outside = {cachedOutsideAINodes.Count}, Inside = {cachedInsideAINodes.Count}, All = {cachedAllAINodes.Count}");
        }

        public override void Update()
        {
            base.Update();

            isAllPlayersDead = StartOfRound.Instance.allPlayersDead;
            if (isEnemyDead || isAllPlayersDead) return;

            gargoyleTargets[myID] = targetPlayer;

            if (Time.time - lastNodeCheckTime > nodeCheckInterval)
            {
                CheckAndRefreshAINodes();
                lastNodeCheckTime = Time.time;
            }

            if (targetPlayer == null)
            {
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }
            else
            {
                //matPlayerIsOn = StartOfRound.Instance.footstepSurfaces[targetPlayer.currentFootstepSurfaceIndex].surfaceTag;
                //objPlayerIsOn = GetPlayerSurfaceTag(targetPlayer);
                distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);

                if (currentBehaviourStateIndex != (int)State.PushTarget)
                {
                    pushStage = 0;
                    var innerDict = playerPushStates.GetOrAdd(targetPlayer, new ConcurrentDictionary<int, bool>());
                    // Now you can safely set the push state to false
                    innerDict[myID] = false;

                    foreach (var player in playerPushStates)
                    {
                        if (player.Key.playerUsername != targetPlayer.playerUsername) { player.Value.TryRemove(myID, out _); }
                    }
                }

                if (!isOutside != targetPlayer.isInsideFactory || !targetPlayer.isPlayerControlled || targetPlayer.isPlayerDead) { LogIfDebugBuild($"Player Status: isInsideFactory = {targetPlayer.isInsideFactory}, isPlayerControlled = {targetPlayer.isPlayerControlled}, isPlayerDead = {targetPlayer.isPlayerDead}"); targetPlayer = null; SwitchToBehaviourClientRpc((int)State.SearchingForPlayer); }
            }
            closestPlayer = GetClosestPlayer();
            distanceToClosestPlayer = closestPlayer != null ? Vector3.Distance(transform.position, closestPlayer.transform.position) : 0f;

            if (pushStage < 1)
            {
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

                    if (!targetSeesGargoyle &&
                        targetPlayer != null &&
                        currentBehaviourStateIndex != (int)State.AggressivePursuit &&
                        Time.time > pushTimer &&
                        enablePush &&
                        (distToKillTrigger <= 2f))
                    {
                        // Lock to prevent concurrent access
                        lock (playerPushStates)
                        {
                            if (playerPushStates.TryGetValue(targetPlayer, out var pushStates) &&
                                !pushStates.Any(kvp => kvp.Key != myID && kvp.Value))
                            {
                                // Set the push state BEFORE switching to PushTarget
                                playerPushStates.GetOrAdd(targetPlayer, new ConcurrentDictionary<int, bool>())[myID] = true;
                                SwitchToBehaviourClientRpc((int)State.PushTarget);
                            }
                            else
                            {
                                pushTimer = Time.time + 10f;
                            }
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
                    agent.angularSpeed = 140f;
                    creatureSFX.volume = 0f;
                    agent.stoppingDistance = 0.1f;
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
                    agent.angularSpeed = 250f;
                    creatureSFX.volume = 1f;
                    agent.stoppingDistance = 0.2f;
                    SearchForPlayers();
                    break;
                case (int)State.StealthyPursuit:
                    agent.speed = baseSpeed;
                    agent.angularSpeed = 140f;
                    creatureSFX.volume = 0.5f;
                    agent.stoppingDistance = 0.1f;
                    if (targetPlayer != null)
                    {
                        foundSpot = SetDestinationToHiddenPosition();
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
                    else
                    {
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    }
                    break;
                case (int)State.AggressivePursuit:
                    agent.speed = baseSpeed * 1.8f;
                    creatureSFX.volume = 1.7f;
                    agent.angularSpeed = 180f;
                    agent.stoppingDistance = 0.1f;
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
                    agent.angularSpeed = 250f;
                    creatureSFX.volume = 1f;
                    agent.stoppingDistance = 0.2f;
                    if (targetPlayer != null)
                    {
                        foundSpot = SetDestinationToHiddenPosition();
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
                    agent.angularSpeed = 500f;
                    agent.stoppingDistance = 0.3f;
                    if (targetPlayer != null)
                    {
                        canSeePlayer = CanSeePlayer(targetPlayer);
                        //LogIfDebugBuild($"Distance To Player: {distanceToPlayer} | Target Sees Gargoyle: {targetSeesGargoyle} | Can See Player: {canSeePlayer} | Push Stage: {pushStage} | Push Timer: {pushTimer}");

                        if (distanceToPlayer <= attackRange && (!targetSeesGargoyle || pushStage == 1))
                        {
                            PushPlayer(targetPlayer);
                            pushStage = 0;
                            pushTimer = Time.time + 45f;
                            if (playerPushStates.TryGetValue(targetPlayer, out var innerDict))
                            {
                                innerDict[myID] = false;
                            }
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

            if (currentBehaviourStateIndex != previousStateIndex)
            {
                LogIfDebugBuild(StateToString(currentBehaviourStateIndex));
            }
            previousStateIndex = currentBehaviourStateIndex;

            if (targetPlayer != null)
            {
                killTrigger = FindNearestKillTrigger(targetPlayer.transform.position);
                if (Time.time - playerCheckTimer > 3f)
                {
                    LogIfDebugBuild($"My Target is {targetPlayer.playerUsername}");
                    LogIfDebugBuild($"Closest Kill Trigger: {distToKillTrigger}");
                    //LogIfDebugBuild($"Player is on {GetPlayerSurfaceTag(targetPlayer)}");
                    ChangeTarget();
                    playerCheckTimer = Time.time;
                }
            }

            if (LGInstance != null && currentDoor != null)
            {
                if (Time.time - lastDoorCloseTime >= 0.75f &&  // Use a timer for the delay
                    !currentDoor.isLocked &&
                    currentDoor.GetComponent<AnimatedObjectTrigger>().boolValue &&
                    Vector3.Distance(currentDoor.transform.position, transform.position) > (currentBehaviourStateIndex == (int)State.Idle ? 2.5f : 4f))
                {
                    StartCoroutine(DelayDoorClose(currentDoor));
                    currentDoor = null; // Reset currentDoor
                }
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    if (FoundClosestPlayerInRange())
                    {
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.StealthyPursuit);
                    }

                    if (agent.hasPath)
                    {
                        DoAnimationClientRpc("startWalk");
                    }
                    else
                    {
                        DoAnimationClientRpc("startIdle");
                    }
                    break;
                case (int)State.StealthyPursuit:
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
                    if ((Time.time - targetTimer > 0.5f || !agent.hasPath)&& targetPlayer != null )
                    {
                        // Log the condition that should trigger EvaluatePath
                        //LogIfDebugBuild($"Attempting to evaluate path. Distance to player: {distanceToPlayer}");
                        if (distanceToPlayer <= 20f)
                        {
                            Vector3 targetPosition = GetTargetPosition(targetPlayer);
                            SetDestinationToPosition(targetPosition, true);
                        }
                        else
                        {
                            SetDestinationToPosition(targetPlayer.transform.position);
                        }
                        // Log whether EvaluatePath was called
                        //LogIfDebugBuild($"Current position: {transform.position}");
                        //LogIfDebugBuild($"Distance to target: {agent.remainingDistance}");
                        //LogIfDebugBuild($"Evaluated path. New target position: {agent.destination}");
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

        private void CheckAndRefreshAINodes()
        {
            LogIfDebugBuild($"Node Counts: Outside = {cachedOutsideAINodes.Count}, Inside = {cachedInsideAINodes.Count}, All = {cachedAllAINodes.Count}");
            if (cachedOutsideAINodes.Any(node => node == null) ||
                cachedInsideAINodes.Any(node => node == null) ||
                cachedAllAINodes.Any(node => node == null))
            {
                // Refresh the cached lists if any null nodes are found
                LogIfDebugBuild("Null Nodes Found. Refreshing list");
                cachedOutsideAINodes = RoundManager.Instance.outsideAINodes.Where(node => node != null).ToList();
                cachedInsideAINodes = RoundManager.Instance.insideAINodes.Where(node => node != null).ToList();
                cachedAllAINodes = allAINodes.Where(node => node != null).ToList();
            }
        }

        private Transform? FindNearestKillTrigger(Vector3 playerPosition)
        {
            // 1. Get all GameObjects in the scene
            GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();

            // 2. Filter by name and the presence of a BoxCollider component
            List<Transform> killTriggers = allObjects
                .Where(obj => obj.name.StartsWith("KillTrigger") && obj.TryGetComponent<BoxCollider>(out _))
                .Select(obj => obj.transform)
                .ToList();

            // 3. Find the KillTrigger below the player, using ClosestPointOnBounds
            Transform? nearestTrigger = killTriggers
                .Where(trigger =>
                {
                    // Get the KillTrigger's BoxCollider
                    BoxCollider boxCollider = trigger.GetComponent<BoxCollider>();

                    // Calculate the closest point on the BoxCollider's bounds 
                    Vector3 closestPoint3D = boxCollider.ClosestPointOnBounds(playerPosition);

                    // Check if the trigger is below the player and the player is horizontally within the bounds
                    return trigger.position.y < playerPosition.y &&
                           Mathf.Abs(closestPoint3D.x - playerPosition.x) < boxCollider.bounds.extents.x + 1f && // Check x-axis
                           Mathf.Abs(closestPoint3D.z - playerPosition.z) < boxCollider.bounds.extents.z + 1f; // Check z-axis
                })
                .FirstOrDefault();

            distToKillTrigger = nearestTrigger != null
            ? Vector2.Distance(
                new Vector2(nearestTrigger.GetComponent<BoxCollider>().ClosestPointOnBounds(playerPosition).x,
                            nearestTrigger.GetComponent<BoxCollider>().ClosestPointOnBounds(playerPosition).z),
                new Vector2(playerPosition.x, playerPosition.z))
            : float.MaxValue;

            // Check if the player is INSIDE the kill trigger
            if (nearestTrigger != null)
            {
                BoxCollider boxCollider = nearestTrigger.GetComponent<BoxCollider>();
                Vector3 playerPosition2D = new(playerPosition.x, boxCollider.bounds.center.y, playerPosition.z); // Align player's y with trigger's center

                if (boxCollider.bounds.Contains(playerPosition2D))
                {
                    distToKillTrigger = 0f; // Set distance to 0 if inside (ignoring y)
                }
            }

            return nearestTrigger;
        }

        private Transform? FindNearestRailing(Vector3 position)
        {
            // 1. Get all GameObjects in the scene
            GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();

            // 2. Get the layer index for the "Railing" layer
            int railingLayer = LayerMask.NameToLayer("Railing");

            // 3. Filter by layer AND distance
            List<Transform> railings = allObjects
                .Where(obj => obj.layer == railingLayer && Vector3.Distance(obj.transform.position, position) <= 2f)
                .Select(obj => obj.transform)
                .ToList();

            // 4. Find the nearest railing (if any)
            Transform? nearestRailing = railings
                .OrderBy(railing => Vector3.Distance(railing.transform.position, position))
                .FirstOrDefault();

            return nearestRailing;
        }

        private IEnumerator DelayDoorClose(DoorLock door)
        {
            yield return new WaitForSeconds(0.1f); // Example: 0.2 second delay
            if (LGInstance != null)
            {
                AnimatedObjectTrigger component = door.gameObject.GetComponent<AnimatedObjectTrigger>();
                component.TriggerAnimationNonPlayer(playSecondaryAudios: true, overrideBool: true);
            }
            door.CloseDoorNonPlayerServerRpc();
        }

        EnemyAI? EnemyNearGargoyle()
        {
            if (targetPlayer != null)
            {
                foreach (EnemyAI enemy in RoundManager.Instance.SpawnedEnemies)
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
            UpdateValidPlayersAndGargoyles();

            Dictionary<PlayerControllerB, int> targetCounts = validPlayers.ToDictionary(p => p, p => 0);
            foreach (var gargoyle in gargoyles)
            {
                if (gargoyleTargets.TryGetValue(gargoyle.myID, out var target) && target != null && validPlayers.Contains(target))
                {
                    targetCounts[target]++;
                }
            }

            int fairShare = Mathf.CeilToInt((float)gargoyles.Count / validPlayers.Count);

            // If this gargoyle has a target AND more than one gargoyle is targeting it
            // AND there is another valid player available
            if (targetPlayer != null &&
                targetCounts.ContainsKey(targetPlayer) &&
                targetCounts[targetPlayer] > 1 &&
                validPlayers.Count > 1)
            {
                // Try to find a player not targeted by any other gargoyle (using helper method)
                var newTarget = FindBestTarget(targetCounts, fairShare);

                // If an untargeted player is found, switch targets
                if (newTarget != null && newTarget != targetPlayer)
                {
                    LogIfDebugBuild($"Changing {myID}'s target from {targetPlayer.playerClientId} to {newTarget.playerClientId}");
                    gargoyleTargets[myID] = newTarget;
                    targetPlayer = newTarget;
                }
            }
            else
            {
                targetPlayer = null; // Reset target if conditions aren't met
            }

            if (targetPlayer == null)
            {
                // 2. Pass fairShare to FindBestTarget
                targetPlayer = FindBestTarget(targetCounts, fairShare);

                if (targetPlayer != null)
                {
                    LogIfDebugBuild($"{myID} is targeting {targetPlayer.playerClientId}");
                    return true;
                }
                else
                {
                    //LogIfDebugBuild($"{myID} has no target");
                    return false;
                }
            }
            return true;
        }

        private void ChangeTarget()
        {
            UpdateValidPlayersAndGargoyles();
            Dictionary<PlayerControllerB, int> targetCounts = validPlayers.ToDictionary(p => p, p => 0);
            foreach (var gargoyle in gargoyles)
            {
                if (gargoyleTargets.TryGetValue(gargoyle.myID, out var target) && target != null && validPlayers.Contains(target))
                {
                    targetCounts[target]++;
                }
            }

            // 1. Check if any player has MORE than their "fair share" of gargoyles
            int fairShare = Mathf.CeilToInt((float)gargoyles.Count / validPlayers.Count);
            bool hasOverTargetedPlayer = targetCounts.Any(kvp => kvp.Value > fairShare);
            if (targetPlayer != null &&
                gargoyleTargets.ContainsKey(myID) &&
                gargoyleTargets[myID] == targetPlayer &&
                targetCounts.ContainsKey(targetPlayer) &&
                targetCounts[targetPlayer] > fairShare && // This gargoyle is on an "over-targeted" player
                validPlayers.Count > 1 &&
                hasOverTargetedPlayer)                  // There's at least one over-targeted player
            {
                LogIfDebugBuild("Checking if I need to change targets");

                // Get a list of gargoyle IDs (consider a more robust ordering if needed)
                List<int> gargoyleIDs = [.. gargoyles.Select(g => g.myID).OrderBy(id => id)];
                int myIndex = gargoyleIDs.IndexOf(myID);
                int switchIndex = (lastGargoyleToSwitch + 1) % gargoyleIDs.Count;

                // Always update lastGargoyleToSwitch 
                lastGargoyleToSwitch = switchIndex;

                if (myIndex == switchIndex)
                {
                    // 1. Calculate fair share BEFORE calling FindBestTarget

                    // 2. Pass fairShare to FindBestTarget
                    var newTarget = FindBestTarget(targetCounts, fairShare);

                    if (newTarget != null && newTarget != targetPlayer) // Add check for different target
                    {
                        LogIfDebugBuild($"Changing {myID}'s target from {targetPlayer.playerClientId} to {newTarget.playerClientId}");
                        gargoyleTargets[myID] = newTarget;
                        targetPlayer = newTarget;
                    }
                }
            }
        }

        private PlayerControllerB? FindBestTarget(Dictionary<PlayerControllerB, int> targetCounts, int fairShare)
        {

            // Find a player with LESS than their "fair share" of gargoyles, within awareDist
            return targetCounts
                .Where(kvp => kvp.Value < fairShare && Vector3.Distance(transform.position, kvp.Key.transform.position) <= awareDist)
                .OrderBy(kvp => kvp.Value)
                .ThenBy(kvp => Vector3.Distance(transform.position, kvp.Key.transform.position))
                .Select(kvp => kvp.Key)
                .FirstOrDefault();
        }

        // Helper method to update validPlayers and gargoyles lists
        private void UpdateValidPlayersAndGargoyles()
        {
            validPlayers = StartOfRound.Instance.allPlayerScripts
                .Where(player => !player.isPlayerDead && player.isInsideFactory == !isOutside)
                .ToList();

            gargoyles = RoundManager.Instance.SpawnedEnemies
                .Where(enemy => enemy is LethalGargoylesAI g && g.isOutside == isOutside)
                .Cast<LethalGargoylesAI>()
                .ToList();
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
            if (Time.time - pathDelayTimer < 2f && agent.hasPath) { return true; }
            if (distanceToPlayer > idleDistance) { SetDestinationToPosition(ChooseClosestNodeToPos(targetPlayer.transform.position, true),true); return true; }

            List<Vector3> coverPoints = FindCoverPointsAroundTarget();
            Transform? targetPlayerTransform = targetPlayer?.transform; // Cache targetPlayer transform

            if (coverPoints.Count == 0 || targetPlayerTransform == null)
            {
                return false; // No cover points or no target player
            }

            // Find the best cover point (prioritize points outside buffer distance)
            Vector3 bestCoverPoint = coverPoints
                .Where(coverPoint => Vector3.Distance(targetPlayerTransform.position, coverPoint) >= bufferDist)
                .OrderBy(coverPoint => Vector3.Distance(targetPlayerTransform.position, coverPoint))
                .FirstOrDefault(); // Close OrderBy here

            if (bestCoverPoint == default) // Check for default Vector3 (0, 0, 0)
            {
                bestCoverPoint = coverPoints
                    .Where(coverPoint => Vector3.Distance(targetPlayerTransform.position, coverPoint) >= aggroRange + 1f)
                    .OrderBy(coverPoint => Vector3.Distance(targetPlayerTransform.position, coverPoint))
                    .FirstOrDefault();
            }

            if (bestCoverPoint != default)
            {
                SetDestinationToPosition(bestCoverPoint, true);
                LogIfDebugBuild($"Heading to {bestCoverPoint}");
                pathDelayTimer = Time.time;
                // LogIfDebugBuild("Found a hiding spot!");
                return true;
            }
            else
            {
                LogIfDebugBuild("No suitable hiding spot found.");
                return false;
            }
        }

        public List<Vector3> FindCoverPointsAroundTarget()
        {
            List<Vector3> coverPoints = [];
            Vector3 targetPlayerPosition = targetPlayer.transform.position;
            Bounds playerBounds = new(targetPlayerPosition, new Vector3(40, 2, 40));
            Bounds gargoyleBounds = new(transform.position, new Vector3(40, 2, 40));

            List<GameObject> validAINodes;
            if (isOutside)
            {
                validAINodes = cachedOutsideAINodes
                    .Where(node => node != null && (gargoyleBounds.Contains(node.transform.position) || playerBounds.Contains(node.transform.position))) // Filter nulls here
                    .ToList();

                if (validAINodes.Count == 0)
                {
                    validAINodes = cachedAllAINodes
                        .Where(node => node != null && (gargoyleBounds.Contains(node.transform.position) || playerBounds.Contains(node.transform.position))) // Filter nulls here
                        .ToList();
                }
            }
            else
            {
                validAINodes = cachedInsideAINodes
                    .Where(node => node != null && (gargoyleBounds.Contains(node.transform.position) || playerBounds.Contains(node.transform.position))) // Filter nulls here
                    .ToList();

                if (validAINodes.Count == 0)
                {
                    validAINodes = cachedAllAINodes
                        .Where(node => node != null && (gargoyleBounds.Contains(node.transform.position) || playerBounds.Contains(node.transform.position))) // Filter nulls here
                        .ToList();
                }
            }

            foreach (var node in validAINodes)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 potentialPos = node.transform.position;
                    Vector2 randomOffset = Random.insideUnitCircle * 3f;
                    potentialPos += new Vector3(randomOffset.x, 0f, randomOffset.y);
                    potentialPos = ValidateZonePosition(potentialPos);
                    if (potentialPos != default)
                    {
                        bool isSafe = !StartOfRound.Instance.allPlayerScripts
                            .Any(player => !player.isPlayerDead && player.isPlayerControlled && isOutside != player.isInsideFactory && 
                            (playerBounds.Contains(player.transform.position) || gargoyleBounds.Contains(player.transform.position)) &&
                            (player.HasLineOfSightToPosition(potentialPos, 60f, 60, 25f) || PathIsIntersectedByLOS(potentialPos, false, true)) &&
                            CheckForPath(transform.position, potentialPos));
                        if (isSafe)
                        {
                            coverPoints.Add(potentialPos);
                        }
                    }
                }
            }
            return coverPoints;
        }

        public Vector3 ChooseClosestNodeToPos(Vector3 pos, bool avoidLineOfSight = false, int offset = 0)
        {
            GameObject[]? orderedNodes = null;
            Vector3 targetPosition = Vector3.zero;

            List<GameObject> validAINodes;
            if (isOutside)
            {
                validAINodes = cachedOutsideAINodes
                    .Where(node => node != null) // Filter nulls here
                    .ToList();

                if (validAINodes.Count == 0)
                {
                    validAINodes = cachedAllAINodes
                        .Where(node => node != null) // Filter nulls here
                        .ToList();
                }
            }
            else
            {
                validAINodes = cachedInsideAINodes
                    .Where(node => node != null) // Filter nulls here
                    .ToList();

                if (validAINodes.Count == 0)
                {
                    validAINodes = cachedAllAINodes
                        .Where(node => node != null) // Filter nulls here
                        .ToList();
                }
            }

            // Order the nodes only if the cache is empty or the target position has changed
            if (orderedNodes == null || targetPosition != pos)
            {
                orderedNodes = [.. validAINodes.OrderBy((GameObject x) => Vector3.Distance(pos, x.transform.position))];
                targetPosition = pos; // Update the target position
            }

            Transform result = orderedNodes[0].transform;
            for (int i = 0; i < orderedNodes.Length; i++)
            {
                if (!PathIsIntersectedByLOS(orderedNodes[i].transform.position, calculatePathDistance: false, avoidLineOfSight))
                {
                    mostOptimalDistance = Vector3.Distance(pos, orderedNodes[i].transform.position);
                    result = orderedNodes[i].transform;
                    if (offset == 0 || i >= orderedNodes.Length - 1)
                    {
                        break;
                    }
                    offset--;
                }
            }
            LogIfDebugBuild($"Heading to {result.position}");
            return result.position;
        }

        public bool PathIsIntersectedByLOS(Vector3 targetPos, bool calculatePathDistance = false, bool avoidLineOfSight = true, bool checkLOSToTargetPlayer = false)
        {
            pathDistance = 0f;
            if (agent.isOnNavMesh && !agent.CalculatePath(targetPos, path1))
            {
                return true; // Path could not be calculated
            }
            if (path1 == null || path1.corners.Length == 0)
            {
                return true; // No path found
            }
            if (Vector3.Distance(path1.corners[^1], RoundManager.Instance.GetNavMeshPosition(targetPos, RoundManager.Instance.navHit, 2.7f)) > 1.5f)
            {
                return true; // Path is not complete
            }

            bool flag = false;
            if (calculatePathDistance)
            {
                for (int j = 1; j < path1.corners.Length; j++)
                {
                    pathDistance += Vector3.Distance(path1.corners[j - 1], path1.corners[j]);
                    if ((!avoidLineOfSight && !checkLOSToTargetPlayer) || j > 15)
                    {
                        continue; // Skip checks if not needed or beyond the limit
                    }
                    if (!flag && j < path1.corners.Length - 1 && j > 8 && Vector3.Distance(path1.corners[j - 1], path1.corners[j]) < 2f)
                    {
                        flag = true;
                        j++; // Skip the next corner as well
                        continue;
                    }
                    flag = false;
                    if (targetPlayer != null && checkLOSToTargetPlayer && !Physics.Linecast(path1.corners[j - 1], targetPlayer.transform.position + Vector3.up * 0.3f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    {
                        return true; // Line of sight to target player
                    }
                    if (avoidLineOfSight && Physics.Linecast(path1.corners[j - 1], path1.corners[j], 262144))
                    {
                        return true; // Path blocked by line of sight
                    }
                }
            }
            else if (avoidLineOfSight)
            {
                for (int k = 1; k < path1.corners.Length; k++)
                {
                    if (!flag && k > 8 && Vector3.Distance(path1.corners[k - 1], path1.corners[k]) < 2f)
                    {
                        flag = true;
                        continue; // Skip this corner
                    }
                    if (targetPlayer != null && checkLOSToTargetPlayer && !Physics.Linecast(Vector3.Lerp(path1.corners[k - 1], path1.corners[k], 0.5f) + Vector3.up * 0.25f, targetPlayer.transform.position + Vector3.up * 0.25f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    {
                        return true; // Line of sight to target player
                    }
                    if (Physics.Linecast(path1.corners[k - 1], path1.corners[k], 262144))
                    {
                        return true; // Path blocked by line of sight
                    }
                    if (k > 15)
                    {
                        return false; // Reached corner limit, no obstruction
                    }
                }
            }
            return false; // No obstruction found
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
            if (signedAngle >= 337.5f || signedAngle < 22.5f) { return RelativeZone.Front; }
            if (signedAngle >= 22.5f && signedAngle < 67.5f) { return RelativeZone.FrontRight; }
            if (signedAngle >= 67.5f && signedAngle < 112.5f) { return RelativeZone.Right; }
            if (signedAngle >= 112.5f && signedAngle < 157.5f) { return RelativeZone.BackRight; }
            if (signedAngle >= 157.5f && signedAngle < 202.5f) { return RelativeZone.Back; }
            if (signedAngle >= 202.5f && signedAngle < 247.5f) { return RelativeZone.BackLeft; }
            if (signedAngle >= 247.5f && signedAngle < 292.5f) { return RelativeZone.Left; }
            if (signedAngle >= 292.5f && signedAngle < 337.5f) { return RelativeZone.FrontLeft; }

            LogIfDebugBuild("This log shouldn't happen... Returning front anyways.");
            return RelativeZone.Front; // Default case
        }

        private readonly Dictionary<RelativeZone, Vector3> RelativeZones = [];
        private RelativeZone currentZone;
        private RelativeZone nextZoneRight;
        private RelativeZone nextZoneLeft;
        private float leftPathDist;
        private float rightPathDist;

        private Vector3 GetTargetPosition(PlayerControllerB player)
        {
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

            //LogIfDebugBuild($"Current Zone: {RelativeZoneToString(currentZone)} | Next Right Zone {nextZoneRight} | Next Left Zone {nextZoneLeft} ");

            bool leftPath = CheckZonePath("Left");
            bool rightPath = CheckZonePath("Right");

            RelativeZone targetZone = rightPath ? nextZoneRight : nextZoneLeft;

            if ((rightPath && RelativeZoneToString(currentZone).Contains("Right")) || (rightPath && !leftPath) || (rightPathDist <= leftPathDist && rightPath && leftPath))
            {
                LogIfDebugBuild("Going Right");
            }
            else if ((leftPath && RelativeZoneToString(currentZone).Contains("Left")) || (leftPath && !rightPath) || (leftPathDist < rightPathDist && leftPath && rightPath))
            {
                LogIfDebugBuild("Going Left");
            }
            else
            {
                LogIfDebugBuild("Staying Still");
                return transform.position; // No valid path, stay in the current position
            }

            return RelativeZones[targetZone];
        }

        private bool CheckZonePath(string side)
        {
            RelativeZone testZone = currentZone;
            RelativeZone nextZone; // Initialize nextZone here
            float pathDist = 0f;
            leftPathDist = 1000f;
            rightPathDist = 1000f;

            do
            {
                nextZone = side == "Right" ? GetNextZone(testZone, 1) : GetNextZone(testZone, -1); // Calculate nextZone inside the loop

                //LogIfDebugBuild($"Testing {side} side: {RelativeZoneToString(testZone)} | Position: {RelativeZones[testZone]}");
                //LogIfDebugBuild($"Testing for valid path of {RelativeZoneToString(testZone)} to {RelativeZoneToString(nextZone)}");

                if (nextZone == RelativeZone.Front){ LogIfDebugBuild($"Path calculation failed. nextZone is 'Front''");  return false;} // Don't go around the front of the player
                if (RelativeZones[testZone] == Vector3.zero) { LogIfDebugBuild($"Path calculation failed. Zone {RelativeZoneToString(testZone)} = Vector3.Zero"); return false;}
                if (RelativeZones[nextZone] == Vector3.zero) { LogIfDebugBuild($"Path calculation failed. Zone {RelativeZoneToString(nextZone)} = Vector3.Zero"); return false; }

                NavMeshPath path = new();
                if (!CheckForPath(RelativeZones[testZone], RelativeZones[nextZone]))
                {
                    LogIfDebugBuild($"Path calculation failed. Path status: {path.status}");
                    return false; // Path calculation failed, so path is not possible
                }
                else
                {
                    //LogIfDebugBuild($"Path calculation successful. Path corners: {string.Join(", ", path.corners)}");
                    pathDist += path.corners.Length > 1
                    ? Vector3.Distance(RelativeZones[testZone], path.corners[1]) // Distance from current zone to first corner
                    : 0f; // No corners, no distance
                }
                testZone = nextZone; // Move to the next zone
            } while (testZone != RelativeZone.Back); // Stop once it reaches the back

            LogIfDebugBuild($"{side} side test was successful | Total distance: {pathDist}");

            if (side == "Right")
            {
                rightPathDist = pathDist;
            }
            else
            {
                leftPathDist = pathDist;
            }

            return true; // All paths are valid
        }

        private void GetBufferPositions(Vector3 playerPos)
        {
            RelativeZones.Clear();
            foreach (RelativeZone position in System.Enum.GetValues(typeof(RelativeZone)))
            {
                Vector3 bufferedPosition = GetBufferedPosition(playerPos, position); // Pass true for randomize
                RelativeZones.Add(position, bufferedPosition);
                //LogIfDebugBuild($"Zone: {RelativeZoneToString(position)}, Position: {bufferedPosition}");
            }
        }

        public Vector3 GetBufferedPosition(Vector3 playerPOS, RelativeZone position)
        {
            Vector3 playerForward = targetPlayer.transform.forward; // Get the player's forward vector
            Vector3 directionVector = GetDirectionVector(position, playerForward); // Calculate the direction vector for the given relative position
            float distance = bufferDistances[position]; // Get the buffer distance for the given relative position
            Vector3 potentialPos = playerPOS + directionVector * distance; // Calculate the potential position
            Vector2 randomOffset = Random.insideUnitCircle * 2f;
            potentialPos += new Vector3(randomOffset.x, 0f, randomOffset.y);
            return ValidateZonePosition(potentialPos); // Now validate the position
        }

        private Vector3 GetDirectionVector(RelativeZone zone, Vector3 playerForward)
        {
            return zone switch
            {
                RelativeZone.Front => playerForward,
                RelativeZone.FrontRight => (playerForward + targetPlayer.transform.right).normalized,
                RelativeZone.Right => targetPlayer.transform.right,
                RelativeZone.BackRight => (-playerForward + targetPlayer.transform.right).normalized,
                RelativeZone.Back => -playerForward,
                RelativeZone.BackLeft => (-playerForward - targetPlayer.transform.right).normalized,
                RelativeZone.Left => -targetPlayer.transform.right,
                RelativeZone.FrontLeft => (playerForward - targetPlayer.transform.right).normalized,
                _ => Vector3.zero,
            };
        }

        private bool CheckForPath(Vector3 sourcePosition, Vector3 targetPosition)
        {
            NavMeshPath path = new();
            if (!NavMesh.CalculatePath(sourcePosition, targetPosition, NavMesh.AllAreas, path))
            {
                return false; // Path calculation failed
            }

            // Additional checks from the base game's SetDestinationToPosition
            if (Vector3.Distance(path.corners[^1], RoundManager.Instance.GetNavMeshPosition(targetPosition, RoundManager.Instance.navHit, 2.7f)) > 1.55f)
            {
                return false; // Path is not valid according to the base game's logic
            }

            return true; // Path is valid
        }

        private Vector3 ValidateZonePosition(Vector3 position)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                return hit.position;
            }

            //LogIfDebugBuild("Invalid Position");
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
            // Cache gargoylePoints array for efficiency
            Vector3[] gargoylePoints = [
                t.position + Vector3.up * 0.25f, // bottom
                t.position + Vector3.up * 1.3f, // top
                t.position + Vector3.left * 1.6f, // Left shoulder
                t.position + Vector3.right * 1.6f, // Right shoulder
                ];

            bool isSeen = StartOfRound.Instance.allPlayerScripts
                .Where(player => Vector3.Distance(player.transform.position, t.position) <= awareDist)
                .Any(player => gargoylePoints.Any(point => player.HasLineOfSightToPosition(point, 68f)) &&
                               PlayerIsTargetable(player) &&
                               PlayerHasHorizontalLOS(player) &&
                               !player.isPlayerDead);


            if (isSeen && targetPlayer != null)
            {
                targetSeesGargoyle = StartOfRound.Instance.allPlayerScripts
                    .Any(player => player.playerUsername == targetPlayer.playerUsername &&
                                   gargoylePoints.Any(point => player.HasLineOfSightToPosition(point, 68f)) &&
                                   PlayerIsTargetable(player) &&
                                   PlayerHasHorizontalLOS(player));
            }
            else
            {
                targetSeesGargoyle = false;
            }
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
            if (player.isPlayerDead || !player.isPlayerControlled) return false;

            Vector3 position = player.gameplayCamera.transform.position;
            return Vector3.Distance(position, eye.position) < range &&
                   !Physics.Linecast(eye.position, position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore) &&
                   (Vector3.Angle(eye.forward, position - eye.position) <= width ||
                    (proximityAwareness != -1 && Vector3.Distance(eye.position, position) < proximityAwareness));
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null && !playerControllerB.isPlayerDead)
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
            Vector3 pushDirection;
            if (killTrigger != null)
            {
        // 1. Find the nearest railing object
                Transform? nearestRailing = FindNearestRailing(player.transform.position);

                if (nearestRailing != null)
                {
                    LogIfDebugBuild("Pushing Towards Railing");

                    // 2. Calculate the direction towards the railing center
                    Vector3 pushDirectionXZ = (nearestRailing.position - player.transform.position).normalized;

                    // 3. Add a slight upward component to the push direction
                    pushDirection = (pushDirectionXZ + Vector3.up * 1f).normalized * 15f;
                }
                else
                {
                    LogIfDebugBuild("Pushing Towards Kill Trigger");

                    // Calculate the direction towards the kill trigger center
                    Vector3 pushDirectionXZ = (killTrigger.position - player.transform.position).normalized;

                    // Add a random left or right offset
                    Vector3 randomSideways = Random.value < 0.5f ? killTrigger.transform.right : -killTrigger.transform.right;
                    pushDirectionXZ += randomSideways * 1f; // 0.5f is an example offset factor, adjust as needed

                    // Add a slight upward component to the push direction
                    pushDirection = (pushDirectionXZ + Vector3.up * 2f).normalized * 15f;
                }
                pushDirection = pushDirection.normalized * 15f;
                player.externalForceAutoFade = pushDirection * 1.5f;
            }
            else
            {
                LogIfDebugBuild("Pushing player forward");
                pushDirection = player.transform.forward * 15f;
                player.externalForceAutoFade = pushDirection;
            }

            StartCoroutine(SetCauseOfDeathDelay(player, "Push"));
        }

        public IEnumerator SetCauseOfDeathDelay(PlayerControllerB player, string deathType)
        {
            yield return new WaitForSeconds(2f);
            //Wait for 2 seconds
            if (player.isPlayerDead)
            {
                if (Plugin.Instance.IsCoronerLoaded) SoftDepends.CoronerClass.CoronerSetCauseOfDeath(player, deathType);
                targetPlayer = null;
                PlayVoice(Utility.AudioManager.playerDeathClips, "playerdeath");
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
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
            if (IsOwner && enemyHP <= 0 && !isEnemyDead)
            {
                KillEnemyOnOwnerClient();
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

            gargoyleTargets.TryRemove(myID, out _);

            // Remove from playerPushStates
            foreach (var player in playerPushStates)
            {
                player.Value.TryRemove(myID, out _);
            }

            int randomIndex = UnityEngine.Random.Range(0, Utility.AudioManager.deathClips.Count());
            TauntClientRpc(Utility.AudioManager.deathClips[randomIndex].name, "death");

            StopCoroutine(searchCoroutine);
        }

        public void PlayVoice(List<AudioClip> clipList, string clipType, AudioClip? clip = null)
        {
            if (clip == null && clipList.Count > 0)
            {
                int randInt = UnityEngine.Random.Range(0, clipList.Count);
                LogIfDebugBuild($"{clipType} count is : {clipList.Count} | Index: {randInt}");
                TauntClientRpc(clipList[randInt].name, clipType, true);
            }
        }

        public void Taunt()
        {
            string? priorCauseOfDeath = null;
            string? playerClass = null;
            int randSource = UnityEngine.Random.Range(1, 4);

            if (targetPlayer != null)
            {
                // Cache player username for efficiency
                string targetPlayerUsername = targetPlayer.playerUsername;

                // Use LINQ for efficient filtering and selection
                (string playerName, string causeOfDeath, string source) = GetDeathCauses.previousRoundDeaths
                    .FirstOrDefault(death => death.playerName.Equals(targetPlayerUsername) &&
                                             (death.source == "Vanilla" || (randSource != 1 && death.source == "Coroner" && Plugin.Instance.IsCoronerLoaded)));

                if (!string.IsNullOrEmpty(playerName))
                {
                    LogIfDebugBuild($"{playerName}'s cause of death last round was {causeOfDeath}");
                    priorCauseOfDeath = causeOfDeath;
                }

                if (Plugin.Instance.IsEmployeeClassesLoaded)
                {
                    // Use LINQ and SingleOrDefault for efficiency
                    playerClass = StartOfRound.Instance.allPlayerScripts
                        .Where(player => targetPlayer.playerUsername == player.playerUsername)
                        .Select(player => EmployeeClassesClass.GetPlayerClass(player))
                        .SingleOrDefault();
                }
            }

            int randInt;
            if ((genTauntCount >= 10 && playerClass != null && priorCauseOfDeath != null) || (genTauntCount >= 15 && (playerClass != null || priorCauseOfDeath != null)))
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
            else if (randInt < 190 && priorCauseOfDeath != null && !GargoyleIsTalking())
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
                        int randomIndex = UnityEngine.Random.Range(0, clipList.Count);
                        if (randomIndex == lastTaunt)
                        {
                            randomIndex++;
                        }
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

                if (enemy != null && enemy.enemyType.enemyName != lastEnemy)
                {
                    string? clip = enemy.enemyType.enemyName.ToUpper() switch
                    {
                        "BLOB" => "taunt_enemy_Slime",
                        "BUTLER" => "taunt_enemy_Butler",
                        "CENTIPEDE" => "taunt_enemy_Centipede",
                        "GIRL" => "taunt_enemy_GhostGirl",
                        "HOARDINGBUG" => "taunt_enemy_HoardingBug",
                        "JESTER" => "taunt_enemy_Jester",
                        "MANEATER" => "taunt_enemy_Maneater",
                        "MASKED" => "taunt_enemy_Masked",
                        "CRAWLER" => "taunt_enemy_Thumper",
                        "BUNKERSPIDER" => "taunt_enemy_Spider",
                        "SPRING" => "taunt_enemy_SpringHead",
                        "NUTCRACKER" => "taunt_enemy_Nutcracker",
                        "FLOWERMAN" => "taunt_enemy_Bracken",
                        _ => null
                    };

                    if (clip != null && UnityEngine.Random.Range(1, 100) < 3)
                    {
                        LogIfDebugBuild(enemy.enemyType.enemyName);
                        lastEnemy = enemy.enemyType.enemyName;
                        string? randomClip = ChooseRandomClip(clip, "Enemy");
                        if (randomClip != null)
                        {
                            TauntClientRpc(randomClip, "enemy");
                            lastEnemyTauntTime = Time.time;
                            randEnemyTauntTime = UnityEngine.Random.Range(minTaunt, maxTaunt);
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

        private string? ChooseRandomClip(string clipName, string listName)
        {
            List<AudioClip> clipList = AudioManager.GetClipListByCategory(listName);
            List<AudioClip> tempList = clipList.Where(clip => clip.name.StartsWith(clipName)).ToList();

            if (tempList.Count == 0) { return null; }

            int intRand = UnityEngine.Random.Range(0, tempList.Count);
            return tempList[intRand].name;
        }

        public bool GargoyleIsTalking()
        {
            return FindObjectsOfType<EnemyAI>().Any(enemy => enemy.enemyType.enemyName.ToUpper() == "LETHALGARGOYLE" && enemy.creatureVoice.isPlaying);
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