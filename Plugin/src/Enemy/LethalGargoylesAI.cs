using GameNetcodeStuff;
using LethalGargoyles.src.Patch;
using LethalGargoyles.src.SoftDepends;
using LethalGargoyles.src.Utility;
using PathfindingLib.API.SmartPathfinding;
using PathfindingLib.Utilities;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using static LethalGargoyles.src.Enemy.LethalGargoylesAI.PlayerActivityTracker;

namespace LethalGargoyles.src.Enemy
{
    public class LethalGargoylesAI : EnemyAI, ISmartAI
    {
        // ============================================================
        // 1) Types / nested types
        // ============================================================

        enum State
        {
            SearchingForPlayer,
            StealthyPursuit,
            GetOutOfSight,
            AggressivePursuit,
            Idle,
            PushTarget,
        }

        public enum AnimState : byte
        {
            Idle = 0,
            Walk = 1,
            Chase = 2,
            SwingAttack = 3
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
                    TimeValue = timeValue,
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
                return new ActivityData { Data = null, TimeValue = 0f, LastActivityTime = 0f };
            }

            public static void RemoveActivity(PlayerControllerB player, PlayerActivityType activityType, string? dataValue = null)
            {
                if (playerActivities.TryGetValue(player, out var activities))
                {
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

        // ============================================================
        // 2) Constants + static caches
        // ============================================================

        // Animator hashes
        private static readonly int TrigIdle = Animator.StringToHash("startIdle");
        private static readonly int TrigWalk = Animator.StringToHash("startWalk");
        private static readonly int TrigChase = Animator.StringToHash("startChase");
        private static readonly int TrigSwingAttack = Animator.StringToHash("swingAttack");

        // Smart path constants
        private const float DEST_EPSILON_SQR = 0.01f;
        private const float REPATH_INTERVAL = 0.75f; // tune
        private const float DEST_CHANGE_SQR = 1.0f; // ~1m

        // Teleport constants
        private const float TELEPORT_COOLDOWN = 10.0f; // tune
        private const float TELEPORT_MIN_DIST_SQR = 45f * 45f;
        private const float TELEPORT_RANGE_MIN = 10f;
        private const float TELEPORT_RANGE_MAX = 18f;
        private const int TELEPORT_ATTEMPTS = 10;

        // Throttle constants
        private const float SlowMs = 2.0f; // tune threshold
        private const float AGGRO_EVAL_INTERVAL = 0.20f; // 5Hz; tune 0.15–0.35
        private const float HIDE_EVAL_INTERVAL = 0.35f; // tune 0.25–0.6

        // Static collections/caches
        public static readonly HashSet<string> trackedItems =
        [
            "Key",
            "Apparatus",
            "Comedy",
            "Tragedy",
            "Maneater",
        ];

        protected static ConcurrentDictionary<int, PlayerControllerB?> gargoyleTargets = [];
        protected static ConcurrentDictionary<PlayerControllerB, ConcurrentDictionary<int, bool>> playerPushStates = [];

        private static readonly List<GameObject> cachedOutsideAINodes = [];
        private static readonly List<GameObject> cachedInsideAINodes = [];
        private static readonly List<GameObject> cachedAllAINodes = [];
        private static readonly List<LethalGargoylesAI> activeGargoyles = [];
        private static readonly List<Transform> cachedRailings = [];
        private static int s_cachedRailingsSceneHandle = -1;

        // Kill trigger caching
        private struct KillTriggerInfo
        {
            public Transform T;
            public BoxCollider C;
        }
        private static readonly List<KillTriggerInfo> cachedKillTriggerInfos = new();

        // Railing OverlapSphereNonAlloc helpers
        private static readonly int RailingMask = 1 << LayerMask.NameToLayer("Railing");
        private static readonly Collider[] _tmpRailingColliders = new Collider[16];

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

        // Static ids/locks
        private static int s_nextGargoyleSerial;
        private static readonly object PlayerPushStatesLock = new();

        private static int lastGenTaunt = -1;
        private static int lastAgrTaunt = -1;
        private static int lastGargoyleToSwitch = 0;
        private static float lastNodeCheckTime = 0f;

        // ============================================================
        // 3) Serialized + identity + config + runtime fields
        // ============================================================

#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
#pragma warning restore 0649

        // Identity / instance bookkeeping
        public static LethalGargoylesAI? LGInstance { get; private set; }

        private int _gargoyleSerial; // assigned once per instance
        public int myID;

        private string GargoyleTag
            => $"LG#{_gargoyleSerial}(agentId={myID}, netId={(NetworkObject != null ? NetworkObject.NetworkObjectId : 0UL)})";

        private bool _smartRegistered;

        // Navigation/Pathing state
        private SmartPathTask? pathingTask;
        private SmartPathDestination? activeDestination;
        private Vector3 _lastActiveDestination = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        private Vector3 _lastRequestedDest;
        private float _nextPathRequestTime;
        private float pathDelayTimer = 0f;

        // Cache current door's AnimatedObjectTrigger (if available)
        private AnimatedObjectTrigger? currentDoorTrigger;

        // Targeting / perception state
        private PlayerControllerB closestPlayer = null!;
        private PlayerControllerB aggroPlayer = null!;
        private PlayerControllerB? _lastTarget = null;

        private Vector3 cachedTargetPosition;

        private float distanceToPlayerSqr = 0f;
        private float distanceToClosestPlayerSqr = 0f;

        private bool isSeen;
        private bool canSeePlayer;
        private bool targetSeesGargoyle;

        private Transform? killTrigger;
        private float distToKillTriggerSqr;

        // Behavior / push
        private float pushTimer = 0f;
        private int pushStage = 0;
        private float targetTimer = 0f;

        private float _nextAggroEvalTime;
        private float playerCheckTimer = 0f;
        private int previousStateIndex;
        private float lastSeenCheckTime = 0f;

        // Zone/path-around-player
        private readonly Dictionary<RelativeZone, Vector3> RelativeZones = [];
        private RelativeZone currentZone;
        private RelativeZone nextZoneRight;
        private RelativeZone nextZoneLeft;
        private float leftPathDist;
        private float rightPathDist;
        private float _nextZoneFailLogTime;

        // Cover/hide
        private Vector3 lastCoverSearchPosition;
        private float coverSearchCooldown = 3f;
        private float lastCoverSearchTime = -10f;
        private List<Vector3> cachedCoverPoints = new();
        private float _nextHideEvalTime;

        // Doors
        public DoorLock? currentDoor = null;
        public float lastDoorCloseTime = 0f;

        // Config values
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

        // Taunts/audio bookkeeping
        private float randGenTauntTime = 0f;
        private float randAgrTauntTime = 0f;
        private float randEnemyTauntTime = 0f;

        private float lastGenTauntTime = 0f;
        private float lastAgrTauntTime = 0f;
        private float lastEnemyTauntTime = 0f;

        private float lastSteamIDTauntTime = 0f;
        private int genTauntCount;

        private string? lastEnemy = null;
        private string? _lastEnemyLogName;
        private float _nextEnemyLogTime;

        private float _nextActivityLogTime;
        private float _nextDoAiLogTime;

        // Misc
        private readonly float nodeCheckInterval = 5f;
        private float lastAttackTime;

        private readonly List<PlayerControllerB> validPlayers = [];
        private readonly List<LethalGargoylesAI> gargoyles = [];
        private readonly Dictionary<PlayerControllerB, string> playerClasses = [];

        private AnimState _lastAnim = AnimState.Idle;

        // Teleport optimization runtime
        private float _nextTeleportTime;

        private static bool IsInvalidPos(Vector3 p) => p.sqrMagnitude < 0.0001f;

        // ============================================================
        // 4) Unity lifecycle + teardown (Start/Update/DoAIInterval)
        // ============================================================

        public override void Start()
        {
            base.Start();
            _gargoyleSerial = System.Threading.Interlocked.Increment(ref s_nextGargoyleSerial);
            myID = agent.GetInstanceID();

            SmartPathfinding.RegisterSmartAgent(agent);
            _smartRegistered = true;

            LGInstance = this;
            LogIfDebugBuild($"{GargoyleTag} Spawned");
            SetAnim(AnimState.Walk);

            SwitchState(State.SearchingForPlayer);
            StartSearch(transform.position);

            baseSpeed = Plugin.BoundConfig.baseSpeed.Value;

            attackDamage = Plugin.BoundConfig.attackDamage.Value;

            minTaunt = Plugin.BoundConfig.minTaunt.Value;
            maxTaunt = Plugin.BoundConfig.maxTaunt.Value;

            attackRangeSqr = Plugin.BoundConfig.attackRange.Value;
            attackRangeSqr *= attackRangeSqr;

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

            gargoyleTargets[myID] = targetPlayer;
            _lastTarget = targetPlayer;

            _nextAggroEvalTime = Time.time;

            creatureVoice.maxDistance *= 3;
            pathDelayTimer = Time.time;

            lastSteamIDTauntTime = Time.time - 91f;

            cachedOutsideAINodes.Clear();
            foreach (var node in RoundManager.Instance.outsideAINodes)
                if (node != null)
                    cachedOutsideAINodes.Add(node);

            cachedInsideAINodes.Clear();
            foreach (var node in RoundManager.Instance.insideAINodes)
                if (node != null)
                    cachedInsideAINodes.Add(node);

            cachedAllAINodes.Clear();
            foreach (var node in allAINodes)
                if (node != null)
                    cachedAllAINodes.Add(node);

            LogIfDebugBuild($"Nodes Initialized | Node Counts: Outside = {cachedOutsideAINodes.Count}, Inside = {cachedInsideAINodes.Count}, All = {cachedAllAINodes.Count}");

            CacheKillTriggers();

            playerClasses.Clear();
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                playerClasses[player] = EmployeeClassesClass.GetPlayerClass(player) ?? "Employee";
            }

            activeGargoyles.Add(this);
        }

        public override void Update()
        {
            base.Update();

            cachedTargetPosition = targetPlayer != null ? targetPlayer.transform.position : transform.position;

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) return;
            if (!agent.enabled || !agent.isOnNavMesh) return;
            if (!IsOwner) return;

            var sw = Stopwatch.StartNew();

            if (_lastTarget != targetPlayer)
            {
                gargoyleTargets[myID] = targetPlayer;
                _lastTarget = targetPlayer;
            }

            if (Time.time - lastNodeCheckTime > nodeCheckInterval)
            {
                var t0 = (float)sw.Elapsed.TotalMilliseconds;
                CheckAndRefreshAINodes();
                LogIfSlow("CheckAndRefreshAINodes", (float)sw.Elapsed.TotalMilliseconds - t0);
                lastNodeCheckTime = Time.time;
            }

            {
                float t0 = (float)sw.Elapsed.TotalMilliseconds;
                HandleTargetPlayer();
                LogIfSlow("HandleTargetPlayer", (float)sw.Elapsed.TotalMilliseconds - t0);
            }

            if (Time.time - lastSeenCheckTime > 0.33f)
            {
                float t0 = (float)sw.Elapsed.TotalMilliseconds;

                closestPlayer = GetClosestPlayer();
                distanceToClosestPlayerSqr = closestPlayer != null ? (transform.position - closestPlayer.transform.position).sqrMagnitude : 0f;
                isSeen = GargoyleIsSeen(transform);

                LogIfSlow("Seen/Closest", (float)sw.Elapsed.TotalMilliseconds - t0,
                    $"closest={(closestPlayer != null ? closestPlayer.playerUsername : "null")} seen={isSeen}");

                lastSeenCheckTime = Time.time;
            }

            {
                float t0 = (float)sw.Elapsed.TotalMilliseconds;
                HandlePushStage();
                LogIfSlow("HandlePushStage", (float)sw.Elapsed.TotalMilliseconds - t0);
            }

            {
                float t0 = (float)sw.Elapsed.TotalMilliseconds;
                HandleBehaviorState();
                LogIfSlow("HandleBehaviorState", (float)sw.Elapsed.TotalMilliseconds - t0, $"state={StateToString(currentBehaviourStateIndex)}");
            }

            if (currentBehaviourStateIndex != (int)State.Idle)
            {
                float t0 = (float)sw.Elapsed.TotalMilliseconds;
                FollowSmartPath();
                LogIfSlow("FollowSmartPath", (float)sw.Elapsed.TotalMilliseconds - t0);
            }
            sw.Stop();
            LogIfSlow("UpdateTotal", (float)sw.Elapsed.TotalMilliseconds);
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (Time.time >= _nextDoAiLogTime)
            {
                _nextDoAiLogTime = Time.time + 3.0f; // tune: 0.5–2s
                string stateName = StateToString(currentBehaviourStateIndex);
                string netInfo = $"Owner={IsOwner} Server={IsServer} Spawned={(NetworkObject != null && NetworkObject.IsSpawned)}";

                string targetInfo = targetPlayer == null
                    ? "Target=null"
                    : $"Target={targetPlayer.playerUsername} id={targetPlayer.playerClientId} dead={targetPlayer.isPlayerDead} controlled={targetPlayer.isPlayerControlled} insideFactory={targetPlayer.isInsideFactory}";

                string closestInfo = closestPlayer == null
                    ? "Closest=null"
                    : $"Closest={closestPlayer.playerUsername} id={closestPlayer.playerClientId} dead={closestPlayer.isPlayerDead} insideFactory={closestPlayer.isInsideFactory}";

                float distToTarget = targetPlayer != null ? Mathf.Sqrt(distanceToPlayerSqr) : -1f;
                float distToClosest = closestPlayer != null ? Mathf.Sqrt(distanceToClosestPlayerSqr) : -1f;

                string distInfo =
                    $"dist(target)={distToTarget:0.0}m dist(closest)={distToClosest:0.0}m " +
                    $"ranges: aware={Mathf.Sqrt(awareDistSqr):0.0} aggro={Mathf.Sqrt(aggroRangeSqr):0.0} idle={Mathf.Sqrt(idleDistanceSqr):0.0} atk={Mathf.Sqrt(attackRangeSqr):0.0} buffer={Mathf.Sqrt(bufferDistSqr):0.0}";

                string perceptionInfo =
                    $"seen={isSeen} targetSees={targetSeesGargoyle} canSee={canSeePlayer} " +
                    $"push: enable={enablePush} stage={pushStage} timerIn={(pushTimer - Time.time):0.0}s";

                string killInfo =
                    $"killTrigger={(killTrigger != null ? killTrigger.name : "null")} dist={((distToKillTriggerSqr == float.MaxValue) ? -1f : Mathf.Sqrt(distToKillTriggerSqr)):0.0}m";

                bool iAmPushingThisTarget = false;
                if (targetPlayer != null && playerPushStates.TryGetValue(targetPlayer, out var innerDict) && innerDict.TryGetValue(myID, out var pushing))
                    iAmPushingThisTarget = pushing;

                string pushMapInfo = targetPlayer != null
                    ? $"pushMap: iAmPushing={iAmPushingThisTarget} entries={(playerPushStates.TryGetValue(targetPlayer, out var d) ? d.Count : 0)}"
                    : "pushMap: n/a";

                Vector3 pos = transform.position;
                Vector3 agentDest = agent.destination;
                Vector3 vel = agent.velocity;

                string agentInfo =
                    $"agent: enabled={agent.enabled} onNavMesh={agent.isOnNavMesh} " +
                    $"speed={agent.speed:0.00} angSpeed={agent.angularSpeed:0.0} stopDist={agent.stoppingDistance:0.00} " +
                    $"hasPath={agent.hasPath} pending={agent.pathPending} status={agent.pathStatus} " +
                    $"remDist={agent.remainingDistance:0.00} " +
                    $"pos=({pos.x:0.0},{pos.y:0.0},{pos.z:0.0}) dest=({agentDest.x:0.0},{agentDest.y:0.0},{agentDest.z:0.0}) vel=({vel.x:0.0},{vel.y:0.0},{vel.z:0.0})";

                string smartInfo;
                if (pathingTask == null)
                {
                    smartInfo = "smartPath: task=null";
                }
                else
                {
                    bool started = pathingTask.IsStarted;
                    bool ready = started && pathingTask.IsResultReady(0);
                    string requested = $"requested=({_lastRequestedDest.x:0.0},{_lastRequestedDest.y:0.0},{_lastRequestedDest.z:0.0})";
                    string lastActive = $"lastActive=({_lastActiveDestination.x:0.0},{_lastActiveDestination.y:0.0},{_lastActiveDestination.z:0.0})";

                    string result;
                    if (!ready)
                    {
                        result = "result=not-ready";
                    }
                    else
                    {
                        var r = pathingTask.GetResult(0);
                        if (r == null)
                        {
                            result = "result=null";
                        }
                        else
                        {
                            var dest = r.Value;
                            Vector3 dp = dest.Position;
                            result = $"result: type={dest.Type} pos=({dp.x:0.0},{dp.y:0.0},{dp.z:0.0})";
                        }
                    }

                    smartInfo = $"smartPath: started={started} ready={ready} {requested} {lastActive} {result}";
                }

                string doorInfo = currentDoor == null
                    ? "door=null"
                    : $"door={currentDoor.name} locked={currentDoor.isLocked} trigCached={(currentDoorTrigger != null)} trigBool={(currentDoorTrigger != null ? currentDoorTrigger.boolValue : false)} lastCloseAgo={(Time.time - lastDoorCloseTime):0.00}s";

                LogIfDebugBuild(
                    $"{GargoyleTag} DoAIInterval[{myID}] state={stateName} | {netInfo} | outside={isOutside} | " +
                    $"{targetInfo} | {closestInfo} | {distInfo} | " +
                    $"{perceptionInfo} | {killInfo} | {pushMapInfo} | " +
                    $"{agentInfo} | {smartInfo} | {doorInfo}"
                );
            }

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
                killTrigger = FindNearestKillTrigger(cachedTargetPosition);
                if (Time.time - playerCheckTimer > 3f)
                {
                    ChangeTarget();
                    playerCheckTimer = Time.time;
                }
            }

            if (LGInstance != null)
            {
                if (currentDoorTrigger == null && currentDoor != null)
                {
                    currentDoorTrigger = currentDoor.gameObject.GetComponent<AnimatedObjectTrigger>();
                }

                if (Time.time - lastDoorCloseTime >= 0.75f && currentDoor != null &&
                    !currentDoor.isLocked &&
                    currentDoorTrigger != null && currentDoorTrigger.boolValue &&
                    (currentDoor.transform.position - transform.position).sqrMagnitude > (currentBehaviourStateIndex == (int)State.Idle ? 8f : 16f))
                {
                    StartCoroutine(DelayDoorClose(currentDoor));
                    currentDoor = null;
                    currentDoorTrigger = null;
                }
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    if (FoundClosestPlayerInRange())
                    {
                        StopSearch(currentSearch);
                        SwitchState(State.StealthyPursuit);
                    }

                    if (agent.hasPath)
                    {
                        SetAnim(agent.hasPath ? AnimState.Walk : AnimState.Idle);
                    }
                    else
                    {
                        SetAnim(AnimState.Idle);
                    }
                    break;

                case (int)State.StealthyPursuit:
                case (int)State.GetOutOfSight:
                    if (agent.hasPath)
                    {
                        SetAnim(AnimState.Walk);
                    }
                    else
                    {
                        SetAnim(AnimState.Idle);
                    }
                    break;

                case (int)State.Idle:
                    SetAnim(AnimState.Idle);
                    break;

                case (int)State.AggressivePursuit:
                    if (agent.hasPath)
                    {
                        SetAnim(AnimState.Chase);
                    }
                    else
                    {
                        SetAnim(AnimState.Idle);
                    }
                    break;

                case (int)State.PushTarget:
                    if ((Time.time - targetTimer > 0.5f || !agent.hasPath) && targetPlayer != null)
                    {
                        if (distanceToPlayerSqr <= idleDistanceSqr)
                        {
                            Vector3 targetPosition = GetTargetPosition(targetPlayer);
                            SetSmartDestination(targetPosition);
                        }
                        else
                        {
                            SetSmartDestination(cachedTargetPosition);
                        }
                        targetTimer = Time.time;
                    }

                    if (agent.hasPath)
                    {
                        SetAnim(AnimState.Chase);
                    }
                    else
                    {
                        SetAnim(AnimState.Idle);
                    }
                    break;

                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        }

        private void OnDisable()
        {
            CleanupSmartPathing();
        }

        public override void OnDestroy()
        {
            CleanupSmartPathing();
            base.OnDestroy();
        }

        private void CleanupSmartPathing()
        {
            pathingTask?.Dispose();
            pathingTask = null;

            if (!_smartRegistered)
                return;

            if (agent != null)
            {
                SmartPathfinding.UnregisterSmartAgent(agent);
            }

            _smartRegistered = false;
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
            cachedKillTriggerInfos.Clear();
            cachedRailings.Clear();
        }

        // ============================================================
        // 5) State machine core (+ push helpers)
        // ============================================================

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

        private void SwitchState(State state)
        {
            if (IsOwner && currentBehaviourStateIndex != (int)state)
                SwitchToBehaviourState((int)state);
        }

        private void SetAnim(AnimState anim)
        {
            if (_lastAnim == anim) return;
            _lastAnim = anim;

            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
                DoAnimationClientRpc(anim);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(AnimState animationState)
        {
            switch (animationState)
            {
                case AnimState.Walk:
                    creatureAnimator.SetTrigger(TrigWalk);
                    break;
                case AnimState.Chase:
                    creatureAnimator.SetTrigger(TrigChase);
                    break;
                case AnimState.SwingAttack:
                    creatureAnimator.SetTrigger(TrigSwingAttack);
                    break;
                default:
                    creatureAnimator.SetTrigger(TrigIdle);
                    break;
            }
        }

        private void HandleTargetPlayer()
        {
            if (targetPlayer == null)
            {
                SwitchState(State.SearchingForPlayer);
                return;
            }

            distanceToPlayerSqr = (transform.position - cachedTargetPosition).sqrMagnitude;
            if (currentBehaviourStateIndex != (int)State.PushTarget)
            {
                ResetPushStage();
            }

            bool sameRegionAsGargoyle = targetPlayer.isInsideFactory != isOutside;

            if (!sameRegionAsGargoyle ||
                !targetPlayer.isPlayerControlled ||
                targetPlayer.isPlayerDead ||
                distanceToPlayerSqr > awareDistSqr)
            {
                targetPlayer = null;
                SwitchState(State.SearchingForPlayer);
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

        private void HandleIdleState()
        {
            agent.speed = 0f;
            agent.angularSpeed = 140f;
            creatureSFX.volume = 0f;
            agent.stoppingDistance = 0.1f;
            if (targetPlayer != null)
            {
                LookAtTarget(cachedTargetPosition);
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
                if (TryTeleportNearTarget(targetPlayer))
                    return;

                bool foundSpot = SetDestinationToHiddenPosition();
                if (!foundSpot)
                {
                    SetSmartDestination(cachedTargetPosition);
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
                SwitchState(State.SearchingForPlayer);
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
                SetSmartDestination(aggroPlayer.transform.position);

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
                    SetSmartDestination(cachedTargetPosition);
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
                    SwitchState(State.StealthyPursuit);
                }

                if (pushStage < 1)
                {
                    if (distanceToPlayerSqr <= aggroRangeSqr * 1.5 && !targetSeesGargoyle && canSeePlayer)
                    {
                        pushStage = 1;
                        SetSmartDestination(cachedTargetPosition);
                        LogIfDebugBuild("Push Stage = 1!");
                    }
                }
                else
                {
                    SetSmartDestination(cachedTargetPosition);
                }
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
            if (pushStage >= 1 || distanceToClosestPlayerSqr > awareDistSqr)
                return;

            if (Time.time < _nextAggroEvalTime)
                return;

            _nextAggroEvalTime = Time.time + AGGRO_EVAL_INTERVAL;

            HandleAggroAndPush();
        }

        private void HandleAggroAndPush()
        {
            if (distanceToClosestPlayerSqr > aggroRangeSqr)
            {
                randAgrTauntTime = Time.time - lastAgrTauntTime;
            }

            if (distanceToClosestPlayerSqr <= aggroRangeSqr && isSeen)
            {
                SwitchState(State.AggressivePursuit);
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
                HandlePushTarget();
            }
        }

        private void HandleOutOfAggroRange()
        {
            if (isSeen)
            {
                SwitchState(State.GetOutOfSight);
            }
            else if (distanceToPlayerSqr <= idleDistanceSqr && targetPlayer != null)
            {
                SwitchState(State.Idle);
            }
            else if (targetPlayer != null)
            {
                SwitchState(State.StealthyPursuit);
            }
            else if (currentBehaviourStateIndex != (int)State.SearchingForPlayer)
            {
                SwitchState(State.SearchingForPlayer);
            }
        }

        private void HandlePushTarget()
        {
            lock (PlayerPushStatesLock)
            {
                if (playerPushStates.TryGetValue(targetPlayer, out var pushStates) &&
                    !pushStates.Any(kvp => kvp.Key != myID && kvp.Value))
                {
                    playerPushStates.GetOrAdd(targetPlayer, new ConcurrentDictionary<int, bool>())[myID] = true;
                    SwitchState(State.PushTarget);
                }
                else
                {
                    pushTimer = Time.time + 10f;
                }
            }
        }

        // ============================================================
        // 6) Movement / smart path / roaming (+ teleport)
        // ============================================================

        void SearchForPlayers()
        {
            const SmartPathfindingLinkFlags allowedLinks = SmartPathfindingLinkFlags.InternalTeleports;
            this.StartSmartSearch(transform.position, allowedLinks);
        }

        public void GoToSmartPathDestination(in SmartPathDestination destination)
        {
            switch (destination.Type)
            {
                case SmartDestinationType.DirectToDestination:
                    SetSmartDestination(destination.Position);
                    break;
                case SmartDestinationType.InternalTeleport:
                    SetSmartDestination(destination.Position);
                    break;
                case SmartDestinationType.EntranceTeleport:
                    SetSmartDestination(destination.Position);
                    break;
                case SmartDestinationType.Elevator:
                    SetSmartDestination(destination.Position);
                    break;
            }
        }

        private void SetSmartDestination(Vector3 destination)
        {
            if (pathingTask != null && pathingTask.IsStarted)
            {
                FollowSmartPath();

                if (!pathingTask.IsResultReady(0) &&
                    (destination - _lastRequestedDest).sqrMagnitude <= DEST_CHANGE_SQR)
                    return;
            }

            if (Time.time < _nextPathRequestTime &&
                (destination - _lastRequestedDest).sqrMagnitude <= DEST_CHANGE_SQR)
                return;

            _lastRequestedDest = destination;
            _nextPathRequestTime = Time.time + REPATH_INTERVAL;

            pathingTask ??= new SmartPathTask();
            pathingTask.StartPathTask(agent, agent.GetPathOrigin(), destination, GetAllowedLinks());
        }

        public void FollowSmartPath()
        {
            if (pathingTask == null || !pathingTask.IsStarted) return;
            if (!pathingTask.IsResultReady(0)) return;

            activeDestination = pathingTask.GetResult(0);
            if (activeDestination == null) return;

            var dest = activeDestination.Value;
            Vector3 destPos = dest.Position;

            bool agentNeedsPath =
                !agent.hasPath ||
                agent.pathPending ||
                agent.pathStatus == NavMeshPathStatus.PathInvalid;

            bool destChanged = (_lastActiveDestination - destPos).sqrMagnitude > DEST_EPSILON_SQR;

            if (destChanged || agentNeedsPath)
            {
                agent.SetDestination(destPos);
                _lastActiveDestination = destPos;
            }

            float activateDist = 1f + agent.stoppingDistance;
            if ((transform.position - destPos).sqrMagnitude <= activateDist * activateDist)
            {
                switch (dest.Type)
                {
                    case SmartDestinationType.InternalTeleport:
                        agent.Warp(dest.InternalTeleport.Destination.position);
                        break;

                    case SmartDestinationType.EntranceTeleport:
                        agent.Warp(dest.EntranceTeleport.exitPoint.position);
                        break;

                    case SmartDestinationType.Elevator:
                        if (dest.CanActivateDestination(transform.position))
                            dest.ElevatorFloor.CallElevator();
                        break;
                }
            }
        }

        private SmartPathfindingLinkFlags GetAllowedLinks()
        {
            return SmartPathfindingLinkFlags.InternalTeleports;
        }

        private bool TryTeleportNearTarget(PlayerControllerB target)
        {
            if (target == null) return false;
            if (Time.time < _nextTeleportTime) return false;
            if (isSeen) return false;
            if (!agent.enabled || !agent.isOnNavMesh) return false;

            Vector3 targetPos = target.transform.position;
            float distSqr = (transform.position - targetPos).sqrMagnitude;
            if (distSqr < TELEPORT_MIN_DIST_SQR) return false;

            if (!TryFindTeleportPointNearTarget(targetPos, out Vector3 teleportPos))
                return false;

            agent.ResetPath();
            agent.Warp(teleportPos);
            _lastActiveDestination = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

            pathingTask?.Dispose();
            pathingTask = null;
            activeDestination = null;

            pathDelayTimer = Time.time;
            _nextTeleportTime = Time.time + TELEPORT_COOLDOWN;

            LogIfDebugBuild($"{GargoyleTag} Teleported near target (dist={Mathf.Sqrt(distSqr):0.0}m)");
            return true;
        }

        private bool TryFindTeleportPointNearTarget(Vector3 targetPos, out Vector3 result)
        {
            result = default;

            var players = StartOfRound.Instance.allPlayerScripts;

            for (int attempt = 0; attempt < TELEPORT_ATTEMPTS; attempt++)
            {
                Vector2 r = Random.insideUnitCircle.normalized * Random.Range(TELEPORT_RANGE_MIN, TELEPORT_RANGE_MAX);
                Vector3 candidate = targetPos + new Vector3(r.x, 0f, r.y);

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                    continue;

                Vector3 p = hit.position;

                if ((p - targetPos).sqrMagnitude < (TELEPORT_RANGE_MIN * TELEPORT_RANGE_MIN) * 0.5f)
                    continue;

                bool visibleToAny = false;
                for (int i = 0; i < players.Length; i++)
                {
                    var pl = players[i];
                    if (pl == null || pl.isPlayerDead || !pl.isPlayerControlled)
                        continue;

                    bool sameRegionAsGargoyle = pl.isInsideFactory != isOutside;
                    if (!sameRegionAsGargoyle)
                        continue;

                    if (pl.HasLineOfSightToPosition(p + Vector3.up * 1.0f, 68f))
                    {
                        visibleToAny = true;
                        break;
                    }
                }

                if (visibleToAny)
                    continue;

                result = p;
                return true;
            }

            return false;
        }

        // ============================================================
        // 7) Hiding / cover selection
        // ============================================================

        private bool ShouldEvaluateHide()
        {
            if (!agent.hasPath || agent.pathPending || agent.pathStatus == NavMeshPathStatus.PathInvalid)
                return true;

            return Time.time >= _nextHideEvalTime;
        }

        bool SetDestinationToHiddenPosition()
        {
            if (Time.time - pathDelayTimer < 2f && agent.hasPath)
                return true;

            if (!ShouldEvaluateHide())
                return true;

            _nextHideEvalTime = Time.time + HIDE_EVAL_INTERVAL;

            if (distanceToPlayerSqr > idleDistanceSqr)
            {
                SetSmartDestination(ChooseClosestNodeToPos(cachedTargetPosition, true));
                return true;
            }

            const float COVER_REBUILD_DIST_SQR = 9f;
            bool shouldRebuild = cachedCoverPoints.Count == 0 ||
                                 (Time.time - lastCoverSearchTime) >= coverSearchCooldown ||
                                 (cachedTargetPosition - lastCoverSearchPosition).sqrMagnitude >= COVER_REBUILD_DIST_SQR;

            if (shouldRebuild)
            {
                cachedCoverPoints = FindCoverPointsAroundTarget();
                lastCoverSearchPosition = cachedTargetPosition;
                lastCoverSearchTime = Time.time;
            }

            List<Vector3> coverPoints = cachedCoverPoints;
            Transform? targetPlayerTransform = targetPlayer?.transform;

            if (coverPoints.Count == 0 || targetPlayerTransform == null)
                return false;

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

            if (bestCoverPoint == default)
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
                SetSmartDestination(bestCoverPoint);
                pathDelayTimer = Time.time;
                return true;
            }

            LogIfDebugBuild("No suitable hiding spot found.");
            return false;
        }

        public List<Vector3> FindCoverPointsAroundTarget()
        {
            List<Vector3> coverPoints = [];

            Vector3 targetPlayerPosition = cachedTargetPosition;
            Bounds playerBounds = new(targetPlayerPosition, new Vector3(40, 2, 40));
            Bounds gargoyleBounds = new(transform.position, new Vector3(40, 2, 40));

            var players = StartOfRound.Instance.allPlayerScripts;

            List<GameObject> validAINodes = [];
            if (isOutside)
            {
                foreach (var node in cachedOutsideAINodes)
                    if (node != null && (gargoyleBounds.Contains(node.transform.position) || playerBounds.Contains(node.transform.position)))
                        validAINodes.Add(node);

                if (validAINodes.Count == 0)
                    foreach (var node in cachedAllAINodes)
                        if (node != null && (gargoyleBounds.Contains(node.transform.position) || playerBounds.Contains(node.transform.position)))
                            validAINodes.Add(node);
            }
            else
            {
                foreach (var node in cachedInsideAINodes)
                    if (node != null && (gargoyleBounds.Contains(node.transform.position) || playerBounds.Contains(node.transform.position)))
                        validAINodes.Add(node);

                if (validAINodes.Count == 0)
                    foreach (var node in cachedAllAINodes)
                        if (node != null && (gargoyleBounds.Contains(node.transform.position) || playerBounds.Contains(node.transform.position)))
                            validAINodes.Add(node);
            }

            const int MAX_COVER_POINTS = 40;
            const int MAX_NODE_SAMPLES = 3;

            for (int n = 0; n < validAINodes.Count && coverPoints.Count < MAX_COVER_POINTS; n++)
            {
                var node = validAINodes[n];
                if (node == null) continue;

                Vector3 nodePos = node.transform.position;

                for (int i = 0; i < MAX_NODE_SAMPLES && coverPoints.Count < MAX_COVER_POINTS; i++)
                {
                    Vector3 potentialPos = nodePos;
                    Vector2 randomOffset = Random.insideUnitCircle * 3f;
                    potentialPos += new Vector3(randomOffset.x, 0f, randomOffset.y);
                    potentialPos = ValidateZonePosition(potentialPos);

                    if (potentialPos == default)
                        continue;

                    if (!CheckForPath(transform.position, potentialPos))
                        continue;

                    if (PathIsIntersectedByLOS(potentialPos, calculatePathDistance: false, avoidLineOfSight: true))
                        continue;

                    bool seenByAnyRelevantPlayer = false;

                    for (int p = 0; p < players.Length; p++)
                    {
                        var player = players[p];
                        if (player.isPlayerDead || !player.isPlayerControlled)
                            continue;

                        if (isOutside != player.isInsideFactory)
                            continue;

                        Vector3 playerPos = player.transform.position;
                        if (!playerBounds.Contains(playerPos) && !gargoyleBounds.Contains(playerPos))
                            continue;

                        if (player.HasLineOfSightToPosition(potentialPos, 60f, 60, 25f))
                        {
                            seenByAnyRelevantPlayer = true;
                            break;
                        }
                    }

                    if (!seenByAnyRelevantPlayer)
                    {
                        coverPoints.Add(potentialPos);
                    }
                }
            }

            return coverPoints;
        }

        public Vector3 ChooseClosestNodeToPos(Vector3 pos, bool avoidLineOfSight = false, int offset = 0)
        {
            List<GameObject> validAINodes = [];
            if (isOutside)
            {
                foreach (var node in cachedOutsideAINodes) if (node != null) validAINodes.Add(node);
                if (validAINodes.Count == 0) foreach (var node in cachedAllAINodes) if (node != null) validAINodes.Add(node);
            }
            else
            {
                foreach (var node in cachedInsideAINodes) if (node != null) validAINodes.Add(node);
                if (validAINodes.Count == 0) foreach (var node in cachedAllAINodes) if (node != null) validAINodes.Add(node);
            }

            int need = Mathf.Max(0, offset) + 1;
            var best = new List<(float distSqr, Transform t)>(need);

            for (int i = 0; i < validAINodes.Count; i++)
            {
                var t = validAINodes[i].transform;

                if (PathIsIntersectedByLOS(t.position, calculatePathDistance: false, avoidLineOfSight))
                    continue;

                float d = (pos - t.position).sqrMagnitude;

                int insertAt = best.FindIndex(x => d < x.distSqr);
                if (insertAt < 0) insertAt = best.Count;

                best.Insert(insertAt, (d, t));
                if (best.Count > need) best.RemoveAt(best.Count - 1);
            }

            if (best.Count == 0)
                return transform.position;

            var chosen = best[best.Count - 1];
            mostOptimalDistance = Mathf.Sqrt(chosen.distSqr);
            return chosen.t.position;
        }

        public bool PathIsIntersectedByLOS(Vector3 targetPos, bool calculatePathDistance = false, bool avoidLineOfSight = true, bool checkLOSToTargetPlayer = false)
        {
            pathDistance = 0f;

            if (!agent.isOnNavMesh)
                return true;

            if (!agent.CalculatePath(targetPos, path1))
                return true;

            if (path1 == null || path1.corners.Length == 0)
                return true;

            const int MAX_CORNERS_TO_SCAN = 12;
            int cornerCount = Mathf.Min(path1.corners.Length, MAX_CORNERS_TO_SCAN);

            if (path1.corners.Length <= 6)
            {
                Vector3 navTarget = RoundManager.Instance.GetNavMeshPosition(targetPos, RoundManager.Instance.navHit, 2.7f);
                if ((path1.corners[^1] - navTarget).sqrMagnitude > 2.25f)
                    return true;
            }

            bool flag = false;

            if (calculatePathDistance)
            {
                for (int j = 1; j < cornerCount; j++)
                {
                    pathDistance += Vector3.Distance(path1.corners[j - 1], path1.corners[j]);

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
                            !Physics.Linecast(path1.corners[j - 1], cachedTargetPosition + Vector3.up * 0.3f,
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
                for (int k = 1; k < cornerCount; k++)
                {
                    if (!flag && k > 8 && (path1.corners[k - 1] - path1.corners[k]).sqrMagnitude < 4f)
                    {
                        flag = true;
                        continue;
                    }

                    if (targetPlayer != null && checkLOSToTargetPlayer &&
                        !Physics.Linecast(Vector3.Lerp(path1.corners[k - 1], path1.corners[k], 0.5f) + Vector3.up * 0.25f,
                                         cachedTargetPosition + Vector3.up * 0.25f,
                                         StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    {
                        return true;
                    }

                    if (Physics.Linecast(path1.corners[k - 1], path1.corners[k], 262144))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ============================================================
        // 8) Target selection / balancing
        // ============================================================

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

            if (targetPlayer != null &&
                targetCounts.ContainsKey(targetPlayer) &&
                targetCounts[targetPlayer] > 1 &&
                validPlayers.Count > 1)
            {
                var newTarget = FindBestTarget(targetCounts, fairShare);

                if (newTarget != null && newTarget != targetPlayer)
                {
                    LogIfDebugBuild($"Changing {myID}'s target from {targetPlayer.playerClientId} to {newTarget.playerClientId}");
                    gargoyleTargets[myID] = newTarget;
                    targetPlayer = newTarget;
                    _lastTarget = newTarget;
                }
            }
            else
            {
                targetPlayer = null;
            }

            if (targetPlayer == null)
            {
                targetPlayer = FindBestTarget(targetCounts, fairShare);

                if (targetPlayer != null)
                {
                    LogIfDebugBuild($"{myID} is targeting {targetPlayer.playerClientId}");
                    _lastTarget = targetPlayer;
                    return true;
                }

                return false;
            }

            return true;
        }

        private void ChangeTarget()
        {
            Dictionary<PlayerControllerB, int> targetCounts = GetGargoyleTargetCounts();

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
                targetCounts[targetPlayer] > fairShare &&
                validPlayers.Count > 1 &&
                hasOverTargetedPlayer)
            {
                LogIfDebugBuild("Checking if I need to change targets");

                List<int> gargoyleIDs = new(gargoyles.Select(g => g.myID).OrderBy(id => id));
                int myIndex = gargoyleIDs.IndexOf(myID);
                int switchIndex = (lastGargoyleToSwitch + 1) % gargoyleIDs.Count;

                lastGargoyleToSwitch = switchIndex;

                if (myIndex == switchIndex)
                {
                    var newTarget = FindBestTarget(targetCounts, fairShare);

                    if (newTarget != null && newTarget != targetPlayer)
                    {
                        LogIfDebugBuild($"Changing {myID}'s target from {targetPlayer.playerClientId} to {newTarget.playerClientId}");
                        gargoyleTargets[myID] = newTarget;
                        targetPlayer = newTarget;
                        _lastTarget = newTarget;
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

        // ============================================================
        // 9) Zone positioning (circle-around logic)
        // ============================================================

        private RelativeZone GetRelativeZone(PlayerControllerB player)
        {
            Vector3 playerPosition = player.transform.position;
            Vector3 aiPosition = transform.position;
            Vector3 directionToAI = aiPosition - playerPosition;

            float signedAngle = Vector3.SignedAngle(player.transform.forward, directionToAI, player.transform.up);
            if (signedAngle < 0)
            {
                signedAngle = 360 + signedAngle;
            }

            if (signedAngle >= 337.5f || signedAngle < 22.5f) { return RelativeZone.Front; }
            if (signedAngle >= 22.5f && signedAngle < 67.5f) { return RelativeZone.FrontRight; }
            if (signedAngle >= 67.5f && signedAngle < 112.5f) { return RelativeZone.Right; }
            if (signedAngle >= 112.5f && signedAngle < 157.5f) { return RelativeZone.BackRight; }
            if (signedAngle >= 157.5f && signedAngle < 202.5f) { return RelativeZone.Back; }
            if (signedAngle >= 202.5f && signedAngle < 247.5f) { return RelativeZone.BackLeft; }
            if (signedAngle >= 247.5f && signedAngle < 292.5f) { return RelativeZone.Left; }
            if (signedAngle >= 292.5f && signedAngle < 337.5f) { return RelativeZone.FrontLeft; }

            LogIfDebugBuild("This log shouldn't happen... Returning front anyways.");
            return RelativeZone.Front;
        }

        private Vector3 GetTargetPosition(PlayerControllerB player)
        {
            bool getUnstuck = false;
            if (distanceToPlayerSqr > idleDistanceSqr)
            {
                return cachedTargetPosition;
            }

            currentZone = GetRelativeZone(player);
            if (currentZone == RelativeZone.Back ||
                currentZone == RelativeZone.BackRight ||
                currentZone == RelativeZone.BackLeft)
            {
                return cachedTargetPosition;
            }

            if (agent.remainingDistance < 2f) getUnstuck = true;
            if (RelativeZones.Count == 0 || currentZone == nextZoneLeft || currentZone == nextZoneRight || getUnstuck)
                GetBufferPositions(player.transform.position);

            nextZoneRight = GetNextZone(currentZone, 1);
            nextZoneLeft = GetNextZone(currentZone, -1);

            bool leftPath = CheckZonePath("Left");
            bool rightPath = CheckZonePath("Right");

            RelativeZone targetZone = rightPath ? nextZoneRight : nextZoneLeft;

            if ((rightPath && RelativeZoneToString(currentZone).Contains("Right")) || (rightPath && !leftPath) || (rightPath && leftPath && rightPathDist <= leftPathDist))
            {
                return RelativeZones[targetZone];
            }
            else if ((leftPath && RelativeZoneToString(currentZone).Contains("Left")) || (leftPath && !rightPath) || (leftPathDist < rightPathDist && leftPath && rightPath))
            {
                return RelativeZones[targetZone];
            }

            Vector3 fallback = ChooseClosestNodeToPos(cachedTargetPosition, avoidLineOfSight: false);
            if (!IsInvalidPos(fallback) && (fallback - transform.position).sqrMagnitude > 1f)
            {
                LogIfDebugBuild("Zone path failed; using fallback node destination.");
                return fallback;
            }

            LogIfDebugBuild("Zone path failed; using player position as fallback.");
            return cachedTargetPosition;
        }

        private void GetBufferPositions(Vector3 playerPos)
        {
            RelativeZones.Clear();
            foreach (RelativeZone position in System.Enum.GetValues(typeof(RelativeZone)))
            {
                Vector3 bufferedPosition = GetBufferedPosition(playerPos, position);
                RelativeZones.Add(position, bufferedPosition);
            }
        }

        public Vector3 GetBufferedPosition(Vector3 playerPOS, RelativeZone position)
        {
            Vector3 playerForward = targetPlayer.transform.forward;
            Vector3 directionVector = GetDirectionVector(position, playerForward);
            float distance = bufferDistances[position];
            Vector3 potentialPos = playerPOS + directionVector * distance;
            Vector2 randomOffset = Random.insideUnitCircle * 2f;
            potentialPos += new Vector3(randomOffset.x, 0f, randomOffset.y);
            return ValidateZonePosition(potentialPos);
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

        private bool CheckZonePath(string side)
        {
            RelativeZone testZone = currentZone;
            RelativeZone nextZone;
            float pathDist = 0f;

            leftPathDist = 1000f;
            rightPathDist = 1000f;

            const int MAX_STEPS = 8;
            int steps = 0;

            while (steps++ < MAX_STEPS)
            {
                nextZone = side == "Right" ? GetNextZone(testZone, 1) : GetNextZone(testZone, -1);

                if (nextZone == RelativeZone.Front)
                {
                    if (Time.time >= _nextZoneFailLogTime)
                    {
                        _nextZoneFailLogTime = Time.time + 1.0f;
                        LogIfDebugBuild("Path calculation failed. nextZone is 'Front'");
                    }
                    return false;
                }

                if (!RelativeZones.TryGetValue(testZone, out var from) || IsInvalidPos(from))
                {
                    testZone = nextZone;
                    continue;
                }

                if (!RelativeZones.TryGetValue(nextZone, out var to) || IsInvalidPos(to))
                {
                    testZone = nextZone;
                    continue;
                }

                NavMeshPath path = new();
                if (!CheckForPath(from, to, path))
                {
                    if (Time.time >= _nextZoneFailLogTime)
                    {
                        _nextZoneFailLogTime = Time.time + 1.0f;
                        LogIfDebugBuild($"Path calculation failed. Path status: {path.status}");
                    }
                    return false;
                }

                pathDist += path.corners.Length > 1
                    ? (from - path.corners[1]).sqrMagnitude
                    : 0f;

                testZone = nextZone;

                if (testZone == RelativeZone.Back)
                    break;
            }

            if (steps >= MAX_STEPS)
            {
                if (Time.time >= _nextZoneFailLogTime)
                {
                    _nextZoneFailLogTime = Time.time + 1.0f;
                    LogIfDebugBuild($"Path calculation failed. Exceeded max steps while checking {side} side.");
                }
                return false;
            }

            if (side == "Right")
                rightPathDist = pathDist;
            else
                leftPathDist = pathDist;

            return true;
        }

        private Vector3 ValidateZonePosition(Vector3 position)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                return hit.position;
            }

            if (NavMesh.SamplePosition(position, out hit, 6.0f, NavMesh.AllAreas))
            {
                return hit.position;
            }

            return Vector3.zero;
        }

        string RelativeZoneToString(RelativeZone relativeZone)
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

        private bool CheckForPath(Vector3 sourcePosition, Vector3 targetPosition, NavMeshPath path)
        {
            if (!NavMesh.CalculatePath(sourcePosition, targetPosition, NavMesh.AllAreas, path))
            {
                return false;
            }

            if ((path.corners[^1] - RoundManager.Instance.GetNavMeshPosition(targetPosition, RoundManager.Instance.navHit, 2.7f)).sqrMagnitude > 2.4025f)
            {
                return false;
            }

            return true;
        }

        private bool CheckForPath(Vector3 sourcePosition, Vector3 targetPosition)
        {
            NavMeshPath path = new();
            return CheckForPath(sourcePosition, targetPosition, path);
        }

        // ============================================================
        // 10) Perception + environment helpers
        // ============================================================

        bool GargoyleIsSeen(Transform t)
        {
            bool gIsSeen = false;
            targetSeesGargoyle = false;
            var players = StartOfRound.Instance.allPlayerScripts;

            Vector3[] gargoylePoints =
            [
                t.position + Vector3.up * 0.25f,
                t.position + Vector3.up * 1.3f,
                t.position + Vector3.left * 1.6f,
                t.position + Vector3.right * 1.6f,
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

                            if (targetPlayer != null && player.playerUsername == targetPlayer.playerUsername)
                            {
                                targetSeesGargoyle = true;
                                break;
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
            Vector3 eyePos = eye.position;

            return (position - eyePos).sqrMagnitude < rangeSqr &&
                   !Physics.Linecast(eyePos, position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore) &&
                   (Vector3.Angle(eye.forward, position - eyePos) <= width ||
                    (proximityAwarenessSqr != -1 && (eyePos - position).sqrMagnitude < proximityAwarenessSqr));
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

        private void CacheKillTriggers()
        {
            var allObjects = GameObject.FindObjectsOfType<GameObject>();
            cachedKillTriggerInfos.Clear();

            foreach (var obj in allObjects)
            {
                if (obj.name.StartsWith("KillTrigger") && obj.TryGetComponent<BoxCollider>(out var bc))
                {
                    cachedKillTriggerInfos.Add(new KillTriggerInfo { T = obj.transform, C = bc });
                }
            }

            CacheRailings();

            LogIfDebugBuild($"Cached {cachedKillTriggerInfos.Count} KillTriggers and {cachedRailings.Count} Railings.");
        }

        private Transform? FindNearestKillTrigger(Vector3 playerPosition)
        {
            Transform? nearestTrigger = null;
            BoxCollider? nearestCollider = null;

            float minDistanceSqr = float.MaxValue;
            float minCenterDistanceSqr = float.MaxValue;

            float px = playerPosition.x;
            float pz = playerPosition.z;

            foreach (var info in cachedKillTriggerInfos)
            {
                Transform trigger = info.T;
                BoxCollider boxCollider = info.C;

                if (trigger == null || boxCollider == null)
                    continue;

                float dxCenter = px - trigger.position.x;
                float dzCenter = pz - trigger.position.z;
                float distanceToTriggerSqr = dxCenter * dxCenter + dzCenter * dzCenter;

                if (distanceToTriggerSqr > minCenterDistanceSqr)
                    continue;

                Vector3 closestPoint3D = boxCollider.ClosestPointOnBounds(playerPosition);
                float cx = closestPoint3D.x;
                float cz = closestPoint3D.z;

                if (trigger.position.y < playerPosition.y &&
                    Mathf.Abs(cx - px) < boxCollider.bounds.extents.x + 1f &&
                    Mathf.Abs(cz - pz) < boxCollider.bounds.extents.z + 1f)
                {
                    float ddx = cx - px;
                    float ddz = cz - pz;
                    float distanceSqr = ddx * ddx + ddz * ddz;

                    if (distanceSqr < minDistanceSqr)
                    {
                        minDistanceSqr = distanceSqr;
                        minCenterDistanceSqr = distanceToTriggerSqr;
                        nearestTrigger = trigger;
                        nearestCollider = boxCollider;
                    }
                }
            }

            distToKillTriggerSqr = minDistanceSqr;

            if (nearestTrigger != null && nearestCollider != null)
            {
                Vector3 playerPosition3D = new(playerPosition.x, nearestCollider.bounds.center.y, playerPosition.z);
                if (nearestCollider.bounds.Contains(playerPosition3D))
                {
                    distToKillTriggerSqr = 0f;
                }
            }

            return nearestTrigger;
        }

        private static void CacheRailings()
        {
            // If scene changed, rebuild.
            int sceneHandle = SceneManager.GetActiveScene().handle;
            if (s_cachedRailingsSceneHandle != sceneHandle)
            {
                cachedRailings.Clear();
                s_cachedRailingsSceneHandle = sceneHandle;
            }

            // Already cached for this scene.
            if (cachedRailings.Count > 0)
                return;

            int railingLayer = LayerMask.NameToLayer("Railing");
            if (railingLayer < 0)
            {
                Plugin.Logger.LogError("Layer 'Railing' not found; railing cache will remain empty.");
                return;
            }

            // Find all colliders on the Railing layer, then cache unique root transforms.
            var allColliders = GameObject.FindObjectsOfType<Collider>();
            var seen = new HashSet<int>();

            for (int i = 0; i < allColliders.Length; i++)
            {
                var col = allColliders[i];
                if (col == null) continue;

                var go = col.gameObject;
                if (go.layer != railingLayer) continue;

                Transform t = col.transform;
                int id = t.GetInstanceID();
                if (seen.Add(id))
                    cachedRailings.Add(t);
            }
        }

        private Transform? FindNearestRailing(Vector3 position)
        {
            // Ensure cache is built (per scene).
            CacheRailings();

            // Fast path: scan cached transforms first.
            Transform? nearestRailing = null;
            float minDistanceSqr = float.MaxValue;

            for (int i = cachedRailings.Count - 1; i >= 0; i--)
            {
                var t = cachedRailings[i];
                if (t == null)
                {
                    cachedRailings.RemoveAt(i);
                    continue;
                }

                float dx = position.x - t.position.x;
                float dz = position.z - t.position.z;
                float distanceSqr = dx * dx + dz * dz;

                if (distanceSqr < minDistanceSqr)
                {
                    minDistanceSqr = distanceSqr;
                    nearestRailing = t;
                }
            }

            // If cache produced a reasonable candidate, return it.
            if (nearestRailing != null)
                return nearestRailing;

            // Fallback: local query (also helps if railings are spawned dynamically).
            int n = Physics.OverlapSphereNonAlloc(position, 2f, _tmpRailingColliders, RailingMask);
            for (int i = 0; i < n; i++)
            {
                var col = _tmpRailingColliders[i];
                if (col == null) continue;

                Transform t = col.transform;

                float dx = position.x - t.position.x;
                float dz = position.z - t.position.z;
                float distanceSqr = dx * dx + dz * dz;

                if (distanceSqr < minDistanceSqr)
                {
                    minDistanceSqr = distanceSqr;
                    nearestRailing = t;
                }

                // Opportunistically cache it for next time.
                if (!cachedRailings.Contains(t))
                    cachedRailings.Add(t);
            }

            return nearestRailing;
        }

        private IEnumerator DelayDoorClose(DoorLock door)
        {
            yield return new WaitForSeconds(0.1f);
            if (LGInstance != null)
            {
                if (door != null && door.gameObject.TryGetComponent<AnimatedObjectTrigger>(out var component))
                {
                    component.TriggerAnimationNonPlayer(playSecondaryAudios: true, overrideBool: true);
                }
            }
            door?.CloseDoorNonPlayerServerRpc();
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

        // ============================================================
        // 11) Combat / collisions / death
        // ============================================================

        public override void OnCollideWithPlayer(Collider other)
        {
            if (currentBehaviourStateIndex == (int)State.PushTarget) return;
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null && !playerControllerB.isPlayerDead)
            {
                if (Time.time - lastAttackTime >= 1f && CanSeePlayer(playerControllerB))
                {
                    AttackPlayer(playerControllerB);
                }
            }
        }

        public void AttackPlayer(PlayerControllerB player)
        {
            LookAtTarget(player.transform.position);
            agent.speed = 0f;
            lastAttackTime = Time.time;
            SetAnim(AnimState.SwingAttack);
            PlayVoice(Utility.AudioManager.attackClips, "attack");
            player.DamagePlayer(attackDamage, false, true, CauseOfDeath.Bludgeoning);

            if (targetPlayer != null)
            {
                if (targetPlayer.isPlayerDead)
                {
                    if (Plugin.Instance.IsCoronerLoaded) SoftDepends.CoronerClass.CoronerSetCauseOfDeath(player, "Attack");
                    targetPlayer = null;
                    PlayVoice(Utility.AudioManager.playerDeathClips, "playerdeath");
                    SwitchState(State.SearchingForPlayer);
                }
            }
        }

        public void PushPlayer(PlayerControllerB player)
        {
            LookAtTarget(player.transform.position);
            agent.speed = 0f;
            lastAttackTime = Time.time;
            SetAnim(AnimState.SwingAttack);
            PlayVoice(Utility.AudioManager.attackClips, "attack");
            player.DamagePlayer(2, false, true, CauseOfDeath.Gravity);

            Vector3 pushDirection;
            if (killTrigger != null)
            {
                Transform? nearestRailing = FindNearestRailing(player.transform.position);

                if (nearestRailing != null)
                {
                    LogIfDebugBuild("Pushing Towards Railing");
                    Vector3 pushDirectionXZ = (nearestRailing.position - player.transform.position).normalized;
                    pushDirection = (pushDirectionXZ + Vector3.up * 1f).normalized * 15f;
                }
                else
                {
                    LogIfDebugBuild("Pushing Towards Kill Trigger");
                    Vector3 pushDirectionXZ = (killTrigger.position - player.transform.position).normalized;

                    Vector3 randomSideways = Random.value < 0.5f ? killTrigger.transform.right : -killTrigger.transform.right;
                    pushDirectionXZ += randomSideways * 1f;

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
            if (player.isPlayerDead)
            {
                if (Plugin.Instance.IsCoronerLoaded) SoftDepends.CoronerClass.CoronerSetCauseOfDeath(player, deathType);
                targetPlayer = null;
                PlayVoice(Utility.AudioManager.playerDeathClips, "playerdeath");
                SwitchState(State.SearchingForPlayer);
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
            SwitchState(State.AggressivePursuit);
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

            foreach (var player in playerPushStates)
            {
                player.Value.TryRemove(myID, out _);
            }

            int randomIndex = UnityEngine.Random.Range(0, Utility.AudioManager.deathClips.Count());
            TauntClientRpc(Utility.AudioManager.deathClips[randomIndex].name, "death");

            if (searchCoroutine != null)
            {
                StopCoroutine(searchCoroutine);
            }

            activeGargoyles.Remove(this);
        }

        // ============================================================
        // 12) Taunts / audio
        // ============================================================

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

            bool doLog = Time.time >= _nextActivityLogTime;
            if (doLog)
                _nextActivityLogTime = Time.time + 2.0f;

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

                if (doLog)
                    LogIfDebugBuild($"Activity type {activityType} | Timer: {Time.time - activityTime} | Data: {activityData.Data} | TimeValue: {activityData.TimeValue}");

                if ((activityData.Data != null || activityData.TimeValue > 0) && Time.time - activityTime > 60f)
                {
                    if (doLog)
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
                    string enemyName = enemy.enemyType.enemyName;
                    if (enemyName != _lastEnemyLogName || Time.time >= _nextEnemyLogTime)
                    {
                        LogIfDebugBuild(enemyName);
                        _lastEnemyLogName = enemyName;
                        _nextEnemyLogTime = Time.time + 1.0f;
                    }

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
                        "LETHALGARGOYLE" => "taunt_enemy_LethalGargoyle",
                        _ => null
                    };

                    if (clip != null && UnityEngine.Random.Range(1, 100) < 3)
                    {
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

        IEnumerator PlayNoiseWhileTalking()
        {
            while (creatureVoice.isPlaying)
            {
                RoundManager.Instance.PlayAudibleNoise(transform.position, creatureVoice.maxDistance / 3f, creatureVoice.volume);
                yield return new WaitForSeconds(3f);
            }
        }

        // ===== Remaining helpers / misc =====
        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        [Conditional("DEBUG")]
        private void LogIfSlow(string section, float ms, string? extra = null)
        {
            if (ms >= SlowMs)
            {
                LogIfDebugBuild($"{GargoyleTag} SLOW {section} {ms:0.00}ms{(extra != null ? $" | {extra}" : "")}");
            }
        }
    }
}