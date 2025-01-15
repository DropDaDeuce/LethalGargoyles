using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GameNetcodeStuff;
using LethalGargoyles.src.Patch;
using LethalGargoyles.src.Utility;
using Unity.Netcode;
using UnityEngine;
using LethalGargoyles.src.SoftDepends;
using UnityEngine.AI;
using System.Collections.Concurrent;
using System.Collections;
using static LethalGargoyles.src.Enemy.LethalGargoylesAI.PlayerActivityTracker;

namespace LethalGargoyles.src.Enemy
{
    public class LethalGargoylesAI : EnemyAI
    {
        public static readonly HashSet<string> trackedItems =
        [
            "Key",
            "Apparatus",
            "Comedy",
            "Tragedy",
            "Maneater",
        ];

        public static class PlayerActivityTracker
        {
            private static readonly Dictionary<PlayerControllerB, Dictionary<PlayerActivityType, ActivityData>> playerActivities = [];
            private static readonly Dictionary<PlayerControllerB, Dictionary<string, float>> playerTauntTimers = [];

            public enum PlayerActivityType
            {
                KilledEnemy,
                PickedUpItem,
                InFacility
            }

            public class ActivityData
            {
                public string? Data { get; set; }
                public float TimeValue { get; set; }
                public float LastActivityTime { get; set; }
            }

            public static void UpdatePlayerActivity(PlayerControllerB player, PlayerActivityType activityType, string? data = null, float timeValue = 0f)
            {
                if (!playerActivities.ContainsKey(player))
                {
                    playerActivities[player] = [];
                }

                Plugin.Instance.LogIfDebugBuild($"Updating {activityType} activity for {player.playerUsername} ({data})");

                playerActivities[player][activityType] = new ActivityData
                {
                    Data = data,
                    TimeValue = timeValue, // Store the time value
                    LastActivityTime = Time.time
                };
            }

            public static ActivityData GetPlayerActivity(PlayerControllerB player, PlayerActivityType activityType)
            {
                if (playerActivities.TryGetValue(player, out var activities) &&
                    activities.TryGetValue(activityType, out var activityData))
                {
                        return activityData;
                }
                return new ActivityData { Data = null, TimeValue = 0f, LastActivityTime = 0f }; // Return default/empty activity data
            }

            public static void RemoveActivity(PlayerControllerB player, PlayerActivityType activityType, string? dataValue = null)
            {
                if (playerActivities.TryGetValue(player, out var activities))
                {
                    // Check if the activity exists before attempting to remove it
                    if (activities.ContainsKey(activityType))
                    {
                        if (dataValue != null)
                        {
                            if (activities[activityType].Data == dataValue)
                            {
                                activities.Remove(activityType);
                                Plugin.Instance.LogIfDebugBuild($"Removed {activityType} activity for {player.playerUsername} ({dataValue})");
                            }
                            else
                            {
                                Plugin.Instance.LogIfDebugBuild($"Item ({dataValue}) was not the stored value in {activityType} activity for {player.playerUsername}");
                            }
                        }
                        else
                        {
                            activities.Remove(activityType);
                            Plugin.Instance.LogIfDebugBuild($"Removed {activityType} activity for {player.playerUsername}");
                        }

                        if (activities.Count == 0)
                        {
                            playerActivities.Remove(player);
                            Plugin.Instance.LogIfDebugBuild($"Removed player {player.playerUsername} from activity tracker (no remaining activities)");
                        }
                    }
                }
            }

            public static void ClearAllPlayerData()
            {
                playerActivities.Clear();
                playerTauntTimers.Clear();
            }

            public static float GetPlayerTauntTimer(PlayerControllerB player, string timerName)
            {
                if (!playerTauntTimers.TryGetValue(player, out var timers))
                {
                    timers = new Dictionary<string, float>
                    {
                        { "lastLostTauntTime", Time.time - 61f },
                        { "lastGrabTauntTime", Time.time - 61f },
                        { "lastKillTauntTime", Time.time - 61f }
                    };
                    playerTauntTimers[player] = timers;
                }

                if (!timers.TryGetValue(timerName, out var timer))
                {
                    timer = Time.time - 61f;
                    timers[timerName] = timer;
                }

                return timer;
            }

            public static void UpdatePlayerTauntTimer(PlayerControllerB player, string timerName)
            {
                if (playerTauntTimers.ContainsKey(player))
                {
                    playerTauntTimers[player][timerName] = Time.time;
                }
            }

        }

#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
#pragma warning restore 0649

        public static LethalGargoylesAI? LGInstance { get; private set; }
        public DoorLock? currentDoor = null;
        public float lastDoorCloseTime = 0f;

        PlayerControllerB closestPlayer = null!;
        PlayerControllerB aggroPlayer = null!;

        private float randGenTauntTime = 0f;
        private float randAgrTauntTime = 0f;
        private float randEnemyTauntTime = 0f;

        private float lastGenTauntTime = 0f;
        private float lastAgrTauntTime = 0f;
        private float lastEnemyTauntTime = 0f;

        private float lastSteamIDTauntTime = 0f;

        private static int lastGenTaunt = -1;
        private static int lastAgrTaunt = -1;

        private int genTauntCount;

        protected static ConcurrentDictionary<int, PlayerControllerB?> gargoyleTargets = []; // Use a static dictionary
        protected static ConcurrentDictionary<PlayerControllerB, ConcurrentDictionary<int, bool>> playerPushStates = [];
        private static readonly List<GameObject> cachedOutsideAINodes = [];
        private static readonly List<GameObject> cachedInsideAINodes = [];
        private static readonly List<GameObject> cachedAllAINodes = [];
        private static readonly List<Transform> cachedKillTriggers = [];
        private static readonly List<Transform> cachedRailings = [];
        private static readonly List<LethalGargoylesAI> activeGargoyles = [];
        private static readonly Dictionary<RelativeZone, float> bufferDistances = new()
        {
            { RelativeZone.Front, 15f },
            { RelativeZone.FrontRight, 12f },
            { RelativeZone.Right, 10f },
            { RelativeZone.BackRight, 6f },
            { RelativeZone.Back, 3f },
            { RelativeZone.BackLeft, 6f },
            { RelativeZone.Left, 10f },
            { RelativeZone.FrontLeft, 12f },
        };
        private readonly Dictionary<RelativeZone, Vector3> RelativeZones = [];

        public AISearchRoutine? searchForPlayers;
        public int myID;

        private static float lastNodeCheckTime = 0f;
        private readonly float nodeCheckInterval = 5f;
        private float lastAttackTime;
        private float distanceToPlayerSqr = 0f;
        private float distanceToClosestPlayerSqr = 0f;
        private string? lastEnemy = null;
        //private bool isAllPlayersDead = false;
        private bool isSeen;
        private bool canSeePlayer;
        private bool targetSeesGargoyle;
        private float pushTimer = 0f;
        private int pushStage = 0;
        private float targetTimer = 0f;
        private RelativeZone currentZone;
        private RelativeZone nextZoneRight;
        private RelativeZone nextZoneLeft;
        private float leftPathDist;
        private float rightPathDist;
        private static int lastGargoyleToSwitch = 0; // Track the last gargoyle that switched targets
        private float playerCheckTimer = 0f;
        private float pathDelayTimer = 0f;
        private readonly List<PlayerControllerB> validPlayers = [];
        private readonly List<LethalGargoylesAI> gargoyles = [];
        private int previousStateIndex;
        private Transform? killTrigger;
        private float distToKillTriggerSqr;
        private readonly Dictionary<PlayerControllerB, string> playerClasses = [];
        private float lastSeenCheckTime = 0f;

        private float baseSpeed = 0f;
        private float attackRangeSqr = 0f;
        private int attackDamage = 0;
        private float aggroRangeSqr = 0f;
        private int minTaunt = 0;
        private int maxTaunt = 0;
        private float distWarnSqr = 0f;
        private float bufferDistSqr = 0f;
        private float awareDistSqr = 0f;
        private float idleDistanceSqr = 0f;
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
            
            attackDamage = Plugin.BoundConfig.attackDamage.Value;
            
            minTaunt = Plugin.BoundConfig.minTaunt.Value;
            maxTaunt = Plugin.BoundConfig.maxTaunt.Value;

            attackRangeSqr = Plugin.BoundConfig.attackRange.Value;  // Get the attack range from config
            attackRangeSqr *= attackRangeSqr;            // Calculate and store the squared value

            aggroRangeSqr = Plugin.BoundConfig.aggroRange.Value;
            aggroRangeSqr *= aggroRangeSqr;

            distWarnSqr = Plugin.BoundConfig.distWarn.Value;
            distWarnSqr *= distWarnSqr;

            idleDistanceSqr = Plugin.BoundConfig.idleDistance.Value;
            idleDistanceSqr *= idleDistanceSqr;

            bufferDistSqr = Plugin.BoundConfig.bufferDist.Value;
            bufferDistSqr *= bufferDistSqr;

            awareDistSqr = Plugin.BoundConfig.awareDist.Value;
            awareDistSqr *= awareDistSqr;

            enablePush = Plugin.BoundConfig.enablePush.Value;
            lastAttackTime = Time.time;
            pushTimer = Time.time;
            myID = agent.GetInstanceID();
            gargoyleTargets[myID] = targetPlayer;
            creatureVoice.maxDistance *= 3;
            pathDelayTimer = Time.time;

            lastSteamIDTauntTime = Time.time - 91f;

            if (cachedOutsideAINodes.Count > 0)
            {
                cachedOutsideAINodes.Clear();
            }
            foreach (var node in RoundManager.Instance.outsideAINodes)
            {
                if (node != null)
                {
                    cachedOutsideAINodes.Add(node);
                }
            }

            if (cachedInsideAINodes.Count > 0)
            {
                cachedInsideAINodes.Clear();
            }
            foreach (var node in RoundManager.Instance.insideAINodes)
            {
                if (node != null)
                {
                    cachedInsideAINodes.Add(node);
                }
            }

            if (cachedAllAINodes.Count > 0)
            {
                cachedAllAINodes.Clear();
            }
            foreach (var node in allAINodes)
            {
                if (node != null)
                {
                    cachedAllAINodes.Add(node);
                }

            }
            LogIfDebugBuild($"Nodes Initialized | Node Counts: Outside = {cachedOutsideAINodes.Count}, Inside = {cachedInsideAINodes.Count}, All = {cachedAllAINodes.Count}");

            CacheKillTriggers();

            playerClasses.Clear();
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                // Cache the player's class
                playerClasses[player] = EmployeeClassesClass.GetPlayerClass(player) ?? "Employee";
            }

            activeGargoyles.Add(this); // Add this gargoyle to the list when it spawns
        }

        public override void Update()
        {
            base.Update();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) return;

            gargoyleTargets[myID] = targetPlayer;

            if (Time.time - lastNodeCheckTime > nodeCheckInterval)
            {
                CheckAndRefreshAINodes();
                lastNodeCheckTime = Time.time;
            }

            HandleTargetPlayer();

            if (Time.time - lastSeenCheckTime > 0.2f)
            {
                closestPlayer = GetClosestPlayer();
                distanceToClosestPlayerSqr = closestPlayer != null ? (transform.position - closestPlayer.transform.position).sqrMagnitude : 0f;
                isSeen = GargoyleIsSeen(transform);
                lastSeenCheckTime = Time.time;
            }

            HandlePushStage();

            HandleBehaviorState();
        }


        private void HandleTargetPlayer()
        {
            if (targetPlayer == null)
            {
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }
            else
            {
                distanceToPlayerSqr = (transform.position - targetPlayer.transform.position).sqrMagnitude;
                if (currentBehaviourStateIndex != (int)State.PushTarget)
                {
                    ResetPushStage();
                }

                if (!isOutside != targetPlayer.isInsideFactory || !targetPlayer.isPlayerControlled || targetPlayer.isPlayerDead || distanceToPlayerSqr > awareDistSqr)
                {
                    //LogIfDebugBuild($"Player Status: isInsideFactory = {targetPlayer.isInsideFactory}, isPlayerControlled = {targetPlayer.isPlayerControlled}, isPlayerDead = {targetPlayer.isPlayerDead}");
                    targetPlayer = null;
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                }
            }
        }

        private void HandleBehaviorState()
        {
            switch (currentBehaviourStateIndex)
            {
                case (int)State.Idle:
                    HandleIdleState();
                    break;
                case (int)State.SearchingForPlayer:
                    HandleSearchingForPlayerState();
                    break;
                case (int)State.StealthyPursuit:
                    HandleStealthyPursuitState();
                    break;
                case (int)State.AggressivePursuit:
                    HandleAggressivePursuitState();
                    break;
                case (int)State.GetOutOfSight:
                    HandleGetOutOfSightState();
                    break;
                case (int)State.PushTarget:
                    HandlePushTargetState();
                    break;
                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        }

        private void ResetPushStage()
        {
            pushStage = 0;
            var innerDict = playerPushStates.GetOrAdd(targetPlayer, new ConcurrentDictionary<int, bool>());
            innerDict[myID] = false;

            foreach (var player in playerPushStates)
            {
                if (!player.Key.playerUsername.Equals(targetPlayer.playerUsername))
                {
                    player.Value.TryRemove(myID, out _);
                }
            }
        }

        private void HandlePushStage()
        {
            if (pushStage < 1 && distanceToClosestPlayerSqr <= awareDistSqr)
            {
                HandleAggroAndPush();
            }
        }

        private void HandleOutOfAggroRange()
        {
            if (isSeen)
            {
                SwitchToBehaviourClientRpc((int)State.GetOutOfSight);
            }
            else if (distanceToPlayerSqr <= idleDistanceSqr && targetPlayer != null)
            {
                SwitchToBehaviourClientRpc((int)State.Idle);
            }
            else if (targetPlayer != null)
            {
                SwitchToBehaviourClientRpc((int)State.StealthyPursuit);
            }
            else if (currentBehaviourStateIndex != (int)State.SearchingForPlayer)
            {
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }
        }

        private void HandleAggroAndPush()
        {
            if (distanceToClosestPlayerSqr > aggroRangeSqr)
            {
                randAgrTauntTime = Time.time - lastAgrTauntTime;
            }

            if (distanceToClosestPlayerSqr <= aggroRangeSqr && isSeen)
            {
                SwitchToBehaviourClientRpc((int)State.AggressivePursuit);
            }
            else if (distanceToClosestPlayerSqr <= attackRangeSqr && !isSeen && closestPlayer != null && currentBehaviourStateIndex != (int)State.AggressivePursuit && enablePush)
            {
                PushPlayer(closestPlayer);
            }
            else if (distanceToClosestPlayerSqr > aggroRangeSqr)
            {
                HandleOutOfAggroRange();
            }

            if (!targetSeesGargoyle && targetPlayer != null && currentBehaviourStateIndex != (int)State.AggressivePursuit && Time.time > pushTimer && enablePush && (distToKillTriggerSqr <= 4f))
            {
                //LogIfDebugBuild($"Attempting to switch to PushTarget state. Conditions: targetSeesGargoyle={targetSeesGargoyle}, targetPlayer={targetPlayer != null}, currentBehaviourStateIndex={currentBehaviourStateIndex}, Time.time={Time.time}, pushTimer={pushTimer}, enablePush={enablePush}, distToKillTriggerSqr={distToKillTriggerSqr}");
                HandlePushTarget();
            }
        }

        private void HandlePushTarget()
        {
            lock (playerPushStates)
            {
                if (playerPushStates.TryGetValue(targetPlayer, out var pushStates) && !pushStates.Any(kvp => kvp.Key != myID && kvp.Value))
                {
                    playerPushStates.GetOrAdd(targetPlayer, new ConcurrentDictionary<int, bool>())[myID] = true;
                    //LogIfDebugBuild("Switching to PushTarget state.");
                    SwitchToBehaviourClientRpc((int)State.PushTarget);
                }
                else
                {
                    pushTimer = Time.time + 10f;
                }
            }
        }

        private void HandleIdleState()
        {
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
        }

        private void HandleSearchingForPlayerState()
        {
            agent.speed = baseSpeed * 1.5f;
            agent.angularSpeed = 250f;
            creatureSFX.volume = 1f;
            agent.stoppingDistance = 0.2f;
            SearchForPlayers();
        }

        private void HandleStealthyPursuitState()
        {
            agent.speed = baseSpeed;
            agent.angularSpeed = 140f;
            creatureSFX.volume = 0.5f;
            agent.stoppingDistance = 0.1f;
            if (targetPlayer != null)
            {
                bool foundSpot = SetDestinationToHiddenPosition();
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
        }

        private void HandleAggressivePursuitState()
        {
            agent.speed = baseSpeed * 1.8f;
            creatureSFX.volume = 1.7f;
            agent.angularSpeed = 180f;
            agent.stoppingDistance = 0.1f;
            if (closestPlayer != null)
            {
                aggroPlayer = closestPlayer;
                canSeePlayer = CanSeePlayer(aggroPlayer);
                bool isTalking = GargoyleIsTalking();

                if (Time.time - lastAgrTauntTime >= randAgrTauntTime && !isTalking)
                {
                    OtherTaunt("aggro", ref lastAgrTaunt, ref lastAgrTauntTime, ref randAgrTauntTime);
                }

                LookAtTarget(aggroPlayer.transform.position);
                SetDestinationToPosition(aggroPlayer.transform.position);

                if (Time.time - lastAttackTime >= 1f && canSeePlayer && attackRangeSqr >= distanceToClosestPlayerSqr)
                {
                    AttackPlayer(aggroPlayer);
                }
            }
        }

        private void HandleGetOutOfSightState()
        {
            agent.speed = baseSpeed * 1.5f;
            agent.angularSpeed = 250f;
            creatureSFX.volume = 1f;
            agent.stoppingDistance = 0.2f;
            if (targetPlayer != null)
            {
                bool foundSpot = SetDestinationToHiddenPosition();
                if (Time.time - lastGenTauntTime >= randGenTauntTime)
                {
                    Taunt();
                }
                if (!foundSpot)
                {
                    SetDestinationToPosition(targetPlayer.transform.position);
                }
            }
        }

        private void HandlePushTargetState()
        {
            agent.speed = baseSpeed * 2.5f;
            creatureSFX.volume = 1.7f;
            agent.angularSpeed = 500f;
            agent.stoppingDistance = 0.3f;
            if (targetPlayer != null)
            {
                canSeePlayer = CanSeePlayer(targetPlayer);
                if (distanceToPlayerSqr <= attackRangeSqr && (!targetSeesGargoyle || pushStage == 1))
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
                    if (distanceToPlayerSqr <= aggroRangeSqr * 1.5 && !targetSeesGargoyle && canSeePlayer)
                    {
                        pushStage = 1;
                        SetDestinationToPosition(targetPlayer.transform.position);
                        LogIfDebugBuild("Push Stage = 1!");
                    }
                }
                else
                {
                    SetDestinationToPosition(targetPlayer.transform.position);
                }
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                if (StartOfRound.Instance.allPlayersDead)
                {
                    ClearAllVariables();
                }
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
                    //LogIfDebugBuild($"My Target is {targetPlayer.playerUsername}");
                    //LogIfDebugBuild($"Closest Kill Trigger Squared: {distToKillTriggerSqr}");
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
                    (currentDoor.transform.position - transform.position).sqrMagnitude > (currentBehaviourStateIndex == (int)State.Idle ? 8f : 16f)) // Check if the door is far enough
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
                        if (distanceToPlayerSqr <= idleDistanceSqr)
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

        private void ClearAllVariables()
        {
            activeGargoyles.Clear();
            gargoyleTargets[myID] = null;
            playerPushStates.Clear();
            playerClasses.Clear();
            cachedOutsideAINodes.Clear();
            cachedInsideAINodes.Clear();
            cachedAllAINodes.Clear();
            cachedKillTriggers.Clear();
            cachedRailings.Clear();
        }

        private void CacheKillTriggers()
        {
            var allObjects = GameObject.FindObjectsOfType<GameObject>();
            cachedKillTriggers.Clear();

            foreach (var obj in allObjects)
            {
                if (obj.name.StartsWith("KillTrigger") && obj.TryGetComponent<BoxCollider>(out _))
                {
                    cachedKillTriggers.Add(obj.transform);
                }
            }

            LogIfDebugBuild($"Cached {cachedKillTriggers.Count} KillTriggers and {cachedRailings.Count} Railings.");
        }

        private void CheckAndRefreshAINodes()
        {
            RefreshNodesIfNull(cachedOutsideAINodes, RoundManager.Instance.outsideAINodes, "outside");
            RefreshNodesIfNull(cachedInsideAINodes, RoundManager.Instance.insideAINodes, "inside");
            RefreshNodesIfNull(cachedAllAINodes, allAINodes, "all");
        }

        private void RefreshNodesIfNull(List<GameObject> cachedNodes, IEnumerable<GameObject> sourceNodes, string nodeType)
        {
            bool nullNodesFound = cachedNodes.Any(node => node == null);

            if (nullNodesFound && sourceNodes.Any())
            {
                LogIfDebugBuild($"Null Nodes Found. Refreshing {nodeType} nodes list");
                cachedNodes.Clear();
                foreach (var node in sourceNodes)
                {
                    if (node != null)
                    {
                        cachedNodes.Add(node);
                    }
                }
            }
        }

        private Transform? FindNearestKillTrigger(Vector3 playerPosition)
        {
            Transform? nearestTrigger = null;
            float minDistanceSqr = float.MaxValue; // Initialize with squared maximum distance
            float minCenterDistanceSqr = float.MaxValue; // Initialize with squared maximum distance

            // Cache player position as Vector2 (for 2D distance calculations)
            Vector2 playerPosition2D = new(playerPosition.x, playerPosition.z);

            foreach (Transform trigger in cachedKillTriggers)
            {
                if (trigger != null)
                {
                    // 1. Check squared distance to the KillTrigger center first
                    float distanceToTriggerSqr = (playerPosition - trigger.position).sqrMagnitude;
                    if (distanceToTriggerSqr <= minCenterDistanceSqr)
                    {
                        // Get the KillTrigger's BoxCollider (cache this if called frequently)
                        BoxCollider boxCollider = trigger.GetComponent<BoxCollider>();

                        // Calculate the closest point on the BoxCollider's bounds 
                        Vector3 closestPoint3D = boxCollider.ClosestPointOnBounds(playerPosition);
                        Vector2 closestPoint2D = new(closestPoint3D.x, closestPoint3D.z);

                        // Check if the trigger is below the player and the player is horizontally within the bounds
                        if (trigger.position.y < playerPosition.y &&
                            Mathf.Abs(closestPoint2D.x - playerPosition2D.x) < boxCollider.bounds.extents.x + 1f &&
                            Mathf.Abs(closestPoint2D.y - playerPosition2D.y) < boxCollider.bounds.extents.z + 1f)
                        {
                            // Calculate SQUARED 2D distance to the closest point
                            float distanceSqr = (closestPoint2D - playerPosition2D).sqrMagnitude;

                            if (distanceSqr < minDistanceSqr)
                            {
                                minDistanceSqr = distanceSqr;
                                nearestTrigger = trigger;
                            }
                        }
                    }
                }
            }

            distToKillTriggerSqr = minDistanceSqr; // Store the squared distance

            // Check if the player is INSIDE the kill trigger
            if (nearestTrigger != null)
            {
                BoxCollider boxCollider = nearestTrigger.GetComponent<BoxCollider>();
                Vector3 playerPosition3D = new(playerPosition.x, boxCollider.bounds.center.y, playerPosition.z); // Align player's y with trigger's center

                if (boxCollider.bounds.Contains(playerPosition3D))
                {
                    distToKillTriggerSqr = 0f; // Set distance to 0 if inside (ignoring y)
                }
            }

            return nearestTrigger;
        }

        private Transform? FindNearestRailing(Vector3 position)
        {
            Transform? nearestRailing = null;
            float minDistanceSqr = float.MaxValue;

            Collider[] colliders = Physics.OverlapSphere(position, 2f, 1 << LayerMask.NameToLayer("Railing"));
            foreach (Collider collider in colliders)
            {
                float distanceSqr = (position - collider.transform.position).sqrMagnitude;
                if (distanceSqr < minDistanceSqr)
                {
                    minDistanceSqr = distanceSqr;
                    nearestRailing = collider.transform;
                }
            }

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
                    float distanceSqr = (enemy.transform.position - transform.position).sqrMagnitude;
                    if (distanceSqr <= distWarnSqr)
                    {
                        return enemy;
                    }
                }
            }
            return null;
        }

        private Dictionary<PlayerControllerB, int> GetGargoyleTargetCounts()
        {
            UpdateValidPlayersAndGargoyles();
            Dictionary<PlayerControllerB, int> targetCounts = [];
            foreach (var player in validPlayers)
            {
                targetCounts[player] = 0;
            }

            foreach (var gargoyle in gargoyles)
            {
                if (gargoyleTargets.TryGetValue(gargoyle.myID, out var target) && target != null && validPlayers.Contains(target))
                {
                    targetCounts[target]++;
                }
            }

            return targetCounts;
        }

        bool FoundClosestPlayerInRange()
        {
            Dictionary<PlayerControllerB, int> targetCounts = GetGargoyleTargetCounts();

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
                //Pass fairShare to FindBestTarget
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
            Dictionary<PlayerControllerB, int> targetCounts = GetGargoyleTargetCounts();

            //Check if any player has MORE than their "fair share" of gargoyles
            int fairShare = Mathf.CeilToInt((float)gargoyles.Count / validPlayers.Count);
            bool hasOverTargetedPlayer = false;
            foreach (var kvp in targetCounts)
            {
                if (kvp.Value > fairShare)
                {
                    hasOverTargetedPlayer = true;
                    break;
                }
            }

            if (targetPlayer != null &&
                gargoyleTargets.ContainsKey(myID) &&
                gargoyleTargets[myID] == targetPlayer &&
                targetCounts.ContainsKey(targetPlayer) &&
                targetCounts[targetPlayer] > fairShare && // This gargoyle is on an "over-targeted" player
                validPlayers.Count > 1 &&
                hasOverTargetedPlayer)             // There's at least one over-targeted player
            {
                LogIfDebugBuild("Checking if I need to change targets");

                // Get a list of gargoyle IDs
                List<int> gargoyleIDs = new(gargoyles.Select(g => g.myID).OrderBy(id => id));
                int myIndex = gargoyleIDs.IndexOf(myID);
                int switchIndex = (lastGargoyleToSwitch + 1) % gargoyleIDs.Count;

                // Always update lastGargoyleToSwitch 
                lastGargoyleToSwitch = switchIndex;

                if (myIndex == switchIndex)
                {
                    //Pass fairShare to FindBestTarget
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
            PlayerControllerB? bestTarget = null;
            int minTargetCount = int.MaxValue;
            float minDistanceSqr = awareDistSqr;

            foreach (var kvp in targetCounts)
            {
                if (kvp.Value < fairShare && (transform.position - kvp.Key.transform.position).sqrMagnitude <= awareDistSqr)
                {
                    float distanceSqr = (transform.position - kvp.Key.transform.position).sqrMagnitude;
                    if (kvp.Value < minTargetCount || (kvp.Value == minTargetCount && distanceSqr < minDistanceSqr))
                    {
                        minTargetCount = kvp.Value;
                        minDistanceSqr = distanceSqr;
                        bestTarget = kvp.Key;
                    }
                }
            }

            return bestTarget;
        }

        // Helper method to update validPlayers and gargoyles lists
        // In UpdateValidPlayersAndGargoyles()
        private void UpdateValidPlayersAndGargoyles()
        {
            validPlayers.Clear();
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (!player.isPlayerDead && player.isInsideFactory == !isOutside)
                {
                    validPlayers.Add(player);
                }
            }

            gargoyles.Clear();
            foreach (var enemy in RoundManager.Instance.SpawnedEnemies)
            {
                if (enemy is LethalGargoylesAI g && g.isOutside == isOutside)
                {
                    gargoyles.Add(g);
                }
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
            if (Time.time - pathDelayTimer < 2f && agent.hasPath) { return true; }
            if (distanceToPlayerSqr > idleDistanceSqr) { SetDestinationToPosition(ChooseClosestNodeToPos(targetPlayer.transform.position, true),true); return true; }

            List<Vector3> coverPoints = FindCoverPointsAroundTarget();
            Transform? targetPlayerTransform = targetPlayer?.transform; // Cache targetPlayer transform

            if (coverPoints.Count == 0 || targetPlayerTransform == null)
            {
                return false; // No cover points or no target player
            }

            // Find the best cover point (prioritize points outside buffer distance)
            Vector3 bestCoverPoint = default;
            float minDistanceSqr = awareDistSqr;

            foreach (var coverPoint in coverPoints)
            {
                float distanceSqr = (targetPlayerTransform.position - coverPoint).sqrMagnitude;
                if (distanceSqr >= bufferDistSqr && distanceSqr < minDistanceSqr)
                {
                    bestCoverPoint = coverPoint;
                    minDistanceSqr = distanceSqr;
                }
            }

            if (bestCoverPoint == default) // Check for default Vector3 (0, 0, 0)
            {
                minDistanceSqr = float.MaxValue;
                foreach (var coverPoint in coverPoints)
                {
                    float distanceSqr = (targetPlayerTransform.position - coverPoint).sqrMagnitude;
                    if (distanceSqr >= aggroRangeSqr + 2f && distanceSqr < minDistanceSqr)
                    {
                        bestCoverPoint = coverPoint;
                        minDistanceSqr = distanceSqr;
                    }
                }
            }

            if (bestCoverPoint != default)
            {
                SetDestinationToPosition(bestCoverPoint, true);
                //LogIfDebugBuild($"Heading to {bestCoverPoint}");
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

            List<GameObject> validAINodes = [];
            if (isOutside)
            {
                foreach (var node in cachedOutsideAINodes)
                {
                    if (node != null && (gargoyleBounds.Contains(node.transform.position) || playerBounds.Contains(node.transform.position)))
                    {
                        validAINodes.Add(node);
                    }
                }

                if (validAINodes.Count == 0)
                {
                    foreach (var node in cachedAllAINodes)
                    {
                        if (node != null && (gargoyleBounds.Contains(node.transform.position) || playerBounds.Contains(node.transform.position)))
                        {
                            validAINodes.Add(node);
                        }
                    }
                }
            }
            else
            {
                foreach (var node in cachedInsideAINodes)
                {
                    if (node != null && (gargoyleBounds.Contains(node.transform.position) || playerBounds.Contains(node.transform.position)))
                    {
                        validAINodes.Add(node);
                    }
                }

                if (validAINodes.Count == 0)
                {
                    foreach (var node in cachedAllAINodes)
                    {
                        if (node != null && (gargoyleBounds.Contains(node.transform.position) || playerBounds.Contains(node.transform.position)))
                        {
                            validAINodes.Add(node);
                        }
                    }
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
                        bool isSafe = true;
                        foreach (var player in StartOfRound.Instance.allPlayerScripts)
                        {
                            if (!player.isPlayerDead && player.isPlayerControlled && isOutside != player.isInsideFactory &&
                                (playerBounds.Contains(player.transform.position) || gargoyleBounds.Contains(player.transform.position)) &&
                                (player.HasLineOfSightToPosition(potentialPos, 60f, 60, 25f) || PathIsIntersectedByLOS(potentialPos, false, true)) &&
                                CheckForPath(transform.position, potentialPos))
                            {
                                isSafe = false;
                                break; // Exit the inner loop if not safe
                            }
                        }

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

            List<GameObject> validAINodes = [];
            if (isOutside)
            {
                foreach (var node in cachedOutsideAINodes)
                {
                    if (node != null)
                    {
                        validAINodes.Add(node);
                    }
                }

                if (validAINodes.Count == 0)
                {
                    foreach (var node in cachedAllAINodes)
                    {
                        if (node != null)
                        {
                            validAINodes.Add(node);
                        }
                    }
                }
            }
            else
            {
                foreach (var node in cachedInsideAINodes)
                {
                    if (node != null)
                    {
                        validAINodes.Add(node);
                    }
                }

                if (validAINodes.Count == 0)
                {
                    foreach (var node in cachedAllAINodes)
                    {
                        if (node != null)
                        {
                            validAINodes.Add(node);
                        }
                    }
                }
            }

            // Optimization 1: Cache orderedNodes and reuse if pos hasn't changed
            if (orderedNodes == null || targetPosition != pos)
            {
                orderedNodes = new GameObject[validAINodes.Count];
                validAINodes.CopyTo(orderedNodes);

                // Optimization 2: Use sqrMagnitude for distance comparisons in sorting
                for (int i = 0; i < orderedNodes.Length - 1; i++)
                {
                    for (int j = i + 1; j < orderedNodes.Length; j++)
                    {
                        if ((pos - orderedNodes[i].transform.position).sqrMagnitude >
                            (pos - orderedNodes[j].transform.position).sqrMagnitude)
                        {
                            (orderedNodes[i], orderedNodes[j]) = (orderedNodes[j], orderedNodes[i]);
                        }
                    }
                }
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
            //LogIfDebugBuild($"Heading to {result.position}");
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
            if ((path1.corners[^1] - RoundManager.Instance.GetNavMeshPosition(targetPos, RoundManager.Instance.navHit, 2.7f)).sqrMagnitude > 2.25f)
            {
                return true; // Path is not complete
            }

            bool flag = false;
            if (calculatePathDistance)
            {
                for (int j = 1; j < path1.corners.Length; j++)
                {
                    pathDistance += Vector3.Distance(path1.corners[j - 1], path1.corners[j]);

                    // Simplified condition: Check line of sight only if needed and within the limit
                    if (j <= 15 && (avoidLineOfSight || checkLOSToTargetPlayer))
                    {
                        if (!flag && j > 8 && (path1.corners[j - 1] - path1.corners[j]).sqrMagnitude < 4f)
                        {
                            flag = true;
                            j++;
                            continue;
                        }

                        flag = false;

                        if (checkLOSToTargetPlayer && targetPlayer != null &&
                            !Physics.Linecast(path1.corners[j - 1], targetPlayer.transform.position + Vector3.up * 0.3f,
                                             StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                        {
                            return true;
                        }
                        if (avoidLineOfSight && Physics.Linecast(path1.corners[j - 1], path1.corners[j], 262144))
                        {
                            return true;
                        }
                    }
                }
            }
            else if (avoidLineOfSight)
            {
                for (int k = 1; k < path1.corners.Length; k++)
                {
                    if (!flag && k > 8 && (path1.corners[k - 1] - path1.corners[k]).sqrMagnitude < 4f)
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

        private Vector3 GetTargetPosition(PlayerControllerB player)
        {
            bool getUnstuck = false;
            if (distanceToPlayerSqr > idleDistanceSqr)
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
                //LogIfDebugBuild("Going Right");
                return RelativeZones[targetZone];
            }
            else if ((leftPath && RelativeZoneToString(currentZone).Contains("Left")) || (leftPath && !rightPath) || (leftPathDist < rightPathDist && leftPath && rightPath))
            {
                //LogIfDebugBuild("Going Left");
                return RelativeZones[targetZone];
            }
            else
            {
                LogIfDebugBuild("Staying Still");
                return transform.position; // No valid path, stay in the current position
            }
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
                    ? (RelativeZones[testZone] - path.corners[1]).sqrMagnitude // Distance from current zone to first corner
                    : 0f; // No corners, no distance
                }
                testZone = nextZone; // Move to the next zone
            } while (testZone != RelativeZone.Back); // Stop once it reaches the back

            //LogIfDebugBuild($"{side} side test was successful | Total distance: {pathDist}");

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
            if ((path.corners[^1] - RoundManager.Instance.GetNavMeshPosition(targetPosition, RoundManager.Instance.navHit, 2.7f)).sqrMagnitude > 2.4025f)
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
            bool gIsSeen = false;
            targetSeesGargoyle = false;
            var players = StartOfRound.Instance.allPlayerScripts; // Cache player list

            // Cache gargoylePoints array for efficiency
            Vector3[] gargoylePoints = [
                t.position + Vector3.up * 0.25f, // bottom
                t.position + Vector3.up * 1.3f, // top
                t.position + Vector3.left * 1.6f, // Left shoulder
                t.position + Vector3.right * 1.6f, // Right shoulder
            ];

            foreach (var player in players)
            {
                if ((player.transform.position - t.position).sqrMagnitude <= awareDistSqr)
                {
                    foreach (var point in gargoylePoints)
                    {
                        if (!player.isPlayerDead && PlayerIsFacingGargoyle(player) &&
                            player.HasLineOfSightToPosition(point, 68f) &&
                            PlayerHasHorizontalLOS(player))
                        {
                            gIsSeen = true;

                            // Check if this is the targetPlayer, no need to check further for this player
                            if (targetPlayer != null && player.playerUsername == targetPlayer.playerUsername)
                            {
                                targetSeesGargoyle = true;
                                break; // Exit the inner loop since targetPlayer is seen
                            }
                        }
                    }
                }
            }
            return gIsSeen;
        }

        bool PlayerIsFacingGargoyle(PlayerControllerB player)
        {
            RelativeZone zone = GetRelativeZone(player);
            return zone == RelativeZone.Front || zone == RelativeZone.FrontRight || zone == RelativeZone.FrontLeft;
        }

        public bool PlayerHasHorizontalLOS(PlayerControllerB player)
        {
            Vector3 to = transform.position - player.transform.position;
            to.y = 0f;
            return Vector3.Angle(player.transform.forward, to) < 68f;
        }

        public bool CanSeePlayer(PlayerControllerB player, float width = 180f, int rangeSqr = 120, int proximityAwarenessSqr = -1)
        {
            if (player.isPlayerDead || !player.isPlayerControlled) return false;

            Vector3 position = player.gameplayCamera.transform.position;
            Vector3 eyePos = eye.position; // Cache eye position

            return (position - eyePos).sqrMagnitude < rangeSqr &&
                   !Physics.Linecast(eyePos, position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore) &&
                   (Vector3.Angle(eye.forward, position - eyePos) <= width ||
                    (proximityAwarenessSqr != -1 && (eyePos - position).sqrMagnitude < proximityAwarenessSqr));
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            if (currentBehaviourStateIndex == (int)State.PushTarget) return;
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
            gargoyleTargets.TryRemove(myID, out _);

            // Remove from playerPushStates
            foreach (var player in playerPushStates)
            {
                player.Value.TryRemove(myID, out _);
            }

            int randomIndex = UnityEngine.Random.Range(0, Utility.AudioManager.deathClips.Count());
            TauntClientRpc(Utility.AudioManager.deathClips[randomIndex].name, "death");

            StopCoroutine(searchCoroutine);

            activeGargoyles.Remove(this); // Remove this gargoyle from the list when it dies
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
            if (targetPlayer == null) return;

            string? priorCauseOfDeath = GetPriorCauseOfDeath(targetPlayer);
            string? playerClass = GetPlayerClass(targetPlayer);
            List<PlayerActivityType> validActivities = GetValidActivities(targetPlayer);

            int randInt = GetRandomTauntIndex(priorCauseOfDeath, playerClass);
            bool isTalking = GargoyleIsTalking();

            if (!isTalking)
            {
                if (TryPlayPlayerSpecificTaunt(randInt, targetPlayer)) return;
                if (TryPlayEnemyTaunt(randInt)) return;
                if (TryPlayPriorDeathTaunt(randInt, priorCauseOfDeath)) return;
                if (TryPlayClassTaunt(randInt, playerClass)) return;
                if (TryPlayActivityTaunt(randInt, validActivities)) return;

                PlayGeneralTaunt(randInt);
            }
            else
            {
                lastGenTauntTime = Time.time;
                randGenTauntTime = 2f;
            }
        }

        private int GetRandomTauntIndex(string? priorCauseOfDeath, string? playerClass)
        {
            if ((genTauntCount >= 10 && playerClass != null && priorCauseOfDeath != null) ||
                (genTauntCount >= 15 && (playerClass != null || priorCauseOfDeath != null)))
            {
                return UnityEngine.Random.Range(170, 200);
            }
            return UnityEngine.Random.Range(1, 200);
        }
      
        private bool TryPlayPlayerSpecificTaunt(int randInt, PlayerControllerB player)
        {
            if (randInt >= 160 && randInt < 175 && player.playerSteamId != 0 && Time.time - lastSteamIDTauntTime > 90f &&
                ChooseRandomClip($"{player.playerSteamId}", "SteamIDs", out string? playerClip) && playerClip != null)
            {
                TauntClientRpc(playerClip, "steamids");
                LogIfDebugBuild($"Random Taunt Number: {randInt} | # of general taunts: {genTauntCount}");
                genTauntCount = 0;
                return true;
            }
            return false;
        }

        private bool TryPlayEnemyTaunt(int randInt)
        {
            if (randInt >= 175 && randInt < 180)
            {
                OtherTaunt("enemy", ref lastGenTaunt, ref lastGenTauntTime, ref randGenTauntTime);
                LogIfDebugBuild($"Random Taunt Number: {randInt} | # of general taunts: {genTauntCount}");
                genTauntCount = 0;
                return true;
            }
            return false;
        }

        private bool TryPlayPriorDeathTaunt(int randInt, string? priorCauseOfDeath)
        {
            if (randInt >= 180 && randInt < 190 && priorCauseOfDeath != null)
            {
                if (ChooseRandomClip("taunt_priordeath_" + priorCauseOfDeath, "PriorDeath", out string? randClip) && randClip != null)
                {
                    TauntClientRpc(randClip, "priordeath");
                    LogIfDebugBuild($"Random Taunt Number: {randInt} | # of general taunts: {genTauntCount}");
                    genTauntCount = 0;
                    return true;
                }
                Plugin.Logger.LogError($"Clip missing for {priorCauseOfDeath} death.");
            }
            return false;
        }

        private bool TryPlayClassTaunt(int randInt, string? playerClass)
        {
            if (randInt >= 190 && playerClass != null)
            {
                if (ChooseRandomClip("taunt_employeeclass_" + playerClass, "Class", out string? randClip) && randClip != null)
                {
                    TauntClientRpc(randClip, "class");
                    LogIfDebugBuild($"Random Taunt Number: {randInt} | # of general taunts: {genTauntCount}");
                    genTauntCount = 0;
                    return true;
                }
                Plugin.Logger.LogError($"Clip missing for {playerClass} class.");
            }
            return false;
        }

        private bool TryPlayActivityTaunt(int randInt, List<PlayerActivityType> validActivities)
        {
            if (targetPlayer != null && validActivities.Count > 0)
            {
                PlayerActivityType randomActivity = validActivities[UnityEngine.Random.Range(0, validActivities.Count)];
                string? activityClip = GetActivityClip(randomActivity);

                if (activityClip != null)
                {
                    TauntClientRpc(activityClip, "activity");
                    RemoveActivity(targetPlayer, randomActivity);
                    UpdateLastActivityTime(randomActivity);
                    LogIfDebugBuild($"Random Taunt Number: {randInt} | # of general taunts: {genTauntCount}");
                    genTauntCount++;
                    return true;
                }
            }
            return false;
        }

        private void PlayGeneralTaunt(int randInt)
        {
            OtherTaunt("general", ref lastGenTaunt, ref lastGenTauntTime, ref randGenTauntTime);
            LogIfDebugBuild($"Random Taunt Number: {randInt} | # of general taunts: {genTauntCount}");
            genTauntCount++;
        }

        private string? GetPriorCauseOfDeath(PlayerControllerB player)
        {
            string? priorCauseOfDeath = null;
            int randSource = UnityEngine.Random.Range(1, 4);

            foreach (var (playerName, causeOfDeath, source) in GetDeathCauses.previousRoundDeaths)
            {
                if (playerName.Equals(player.playerUsername) &&
                    (source == "Vanilla" || (randSource != 1 && source == "Coroner" && Plugin.Instance.IsCoronerLoaded)))
                {
                    LogIfDebugBuild($"{playerName}'s cause of death last round was {causeOfDeath}");
                    priorCauseOfDeath = causeOfDeath;
                    break;
                }
            }

            return priorCauseOfDeath;
        }

        private string? GetPlayerClass(PlayerControllerB player)
        {
            if (Plugin.Instance.IsEmployeeClassesLoaded)
            {
                if (!playerClasses.TryGetValue(player, out var playerClass))
                {
                    playerClass = EmployeeClassesClass.GetPlayerClass(player) ?? "Employee";
                    playerClasses[player] = playerClass;
                }
                return playerClass;
            }
            return null;
        }

        private List<PlayerActivityType> GetValidActivities(PlayerControllerB player)
        {
            List<PlayerActivityType> validActivities = [];
            float activityTime;

            foreach (PlayerActivityType activityType in PlayerActivityType.GetValues(typeof(PlayerActivityType)))
            {
                ActivityData activityData = GetPlayerActivity(player, activityType);
                activityTime = activityType switch
                {
                    PlayerActivityType.InFacility => PlayerActivityTracker.GetPlayerTauntTimer(player, "lastLostTauntTime"),
                    PlayerActivityType.PickedUpItem => PlayerActivityTracker.GetPlayerTauntTimer(player, "lastGrabTauntTime"),
                    PlayerActivityType.KilledEnemy => PlayerActivityTracker.GetPlayerTauntTimer(player, "lastKillTauntTime"),
                    _ => 0f,
                };

                LogIfDebugBuild($"Activity type {activityType} | Timer: {Time.time - activityTime} | Data: {activityData.Data} | TimeValue: {activityData.TimeValue}");

                if ((activityData.Data != null || activityData.TimeValue > 0) && Time.time - activityTime > 60f)
                {
                    LogIfDebugBuild($"Adding activity type {activityType}");
                    validActivities.Add(activityType);
                }
            }

            return validActivities;
        }

        private string? GetActivityClip(PlayerActivityType activityType)
        {
            return activityType switch
            {
                PlayerActivityType.KilledEnemy => ChooseRandomClip($"taunt_activity_killedenemy_{GetPlayerActivity(targetPlayer, activityType).Data}", "Activity", out string? clip) ? clip : null,
                PlayerActivityType.PickedUpItem => ChooseRandomClip($"taunt_activity_pickup_{GetPlayerActivity(targetPlayer, activityType).Data}", "Activity", out string? clip) ? clip : null,
                PlayerActivityType.InFacility => ChooseRandomClip("taunt_activity_facilitytime_", "Activity", out string? clip) ? clip : null,
                _ => null,
            };
        }

        private void UpdateLastActivityTime(PlayerActivityType activityType)
        {
            switch (activityType)
            {
                case PlayerActivityType.InFacility:
                    PlayerActivityTracker.UpdatePlayerTauntTimer(targetPlayer, "lastLostTauntTime");
                    break;
                case PlayerActivityType.PickedUpItem:
                    PlayerActivityTracker.UpdatePlayerTauntTimer(targetPlayer, "lastGrabTauntTime");
                    break;
                case PlayerActivityType.KilledEnemy:
                    PlayerActivityTracker.UpdatePlayerTauntTimer(targetPlayer, "lastKillTauntTime");
                    break;
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
                        "BLOB" => "taunt_enemy_Blob",
                        "BUTLER" => "taunt_enemy_Butler",
                        "CENTIPEDE" => "taunt_enemy_Centipede",
                        "GIRL" => "taunt_enemy_Girl",
                        "HOARDINGBUG" => "taunt_enemy_Hoarding Bug",
                        "JESTER" => "taunt_enemy_Jester",
                        "MANEATER" => "taunt_enemy_Maneater",
                        "MASKED" => "taunt_enemy_Masked",
                        "CRAWLER" => "taunt_enemy_Crawler",
                        "BUNKERSPIDER" => "taunt_enemy_Bunker Spider",
                        "SPRING" => "taunt_enemy_Spring",
                        "NUTCRACKER" => "taunt_enemy_Nutcracker",
                        "FLOWERMAN" => "taunt_enemy_Flowerman",
                        "MOUTHDOG" => "taunt_enemy_Mouthdog",
                        _ => null
                    };

                    if (clip != null && UnityEngine.Random.Range(1, 100) < 3)
                    {
                        LogIfDebugBuild(enemy.enemyType.enemyName);
                        lastEnemy = enemy.enemyType.enemyName;
                        ChooseRandomClip(clip, "Enemy", out string? randomClip);
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

        public static AudioClip? FindClip(string clipName, List<AudioClip> clips)
        {
            string lowerClipName = clipName.ToLowerInvariant();

            foreach (AudioClip clip in clips)
            {
                if (clip.name.ToLowerInvariant().StartsWith(lowerClipName))
                {
                    return clip;
                }
            }
            return null;
        }

        public static bool ChooseRandomClip(string clipName, string listName, out string? audioClip)
        {
            List<AudioClip> clipList = AudioManager.GetClipListByCategory(listName);
            List<AudioClip> tempList = [];

            string lowerClipName = clipName.ToLowerInvariant();

            foreach (AudioClip clip in clipList)
            {
                if (clip.name.ToLowerInvariant().StartsWith(lowerClipName))
                {
                    tempList.Add(clip);
                }
            }

            if (tempList.Count == 0)
            {
                audioClip = null;
                return false;
            }

            int intRand = UnityEngine.Random.Range(0, tempList.Count);
            audioClip = tempList[intRand].name;
            return true;
        }

        public bool GargoyleIsTalking()
        {
            for (int i = activeGargoyles.Count - 1; i >= 0; i--)
            {
                var gargoyle = activeGargoyles[i];
                if (gargoyle == null)
                {
                    activeGargoyles.RemoveAt(i);
                    continue;
                }

                if (gargoyle.creatureVoice.isPlaying)
                {
                    return true;
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
                case "activity":
                    clipList = Utility.AudioManager.activityClips; break;
                case "steamids":
                    clipList = Utility.AudioManager.playerClips; break;
            }

            AudioClip? clip = FindClip(clipName, clipList);

            if (clipList.Count > 0 && clip != null)
            {
                LogIfDebugBuild(clipType + " taunt: " + clip.name);
                RoundManager.Instance.PlayAudibleNoise(base.transform.position, creatureVoice.maxDistance / 3f, creatureVoice.volume);
                creatureVoice.PlayOneShot(clip);
                StartCoroutine(PlayNoiseWhileTalking());
            }
        }

        //This is so the MouthDog can "hear" the Gargoyle
        IEnumerator PlayNoiseWhileTalking()
        {
            while (creatureVoice.isPlaying)
            {
                RoundManager.Instance.PlayAudibleNoise(transform.position, creatureVoice.maxDistance / 3f, creatureVoice.volume);
                yield return new WaitForSeconds(3f); // Adjust the interval as needed
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