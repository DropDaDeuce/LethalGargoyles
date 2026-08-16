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

        // Push constants
        private const int PUSH_DAMAGE = 2;

        // Throttle constants
        private const float SlowMs = 2.0f; // tune threshold
        private const float AGGRO_EVAL_INTERVAL = 0.20f; // 5Hz; tune 0.15–0.35
        private const float HIDE_EVAL_INTERVAL = 0.35f; // tune 0.25–0.6

        /// <summary>
        /// Default sight range for <see cref="CanSeePlayer"/>, as a SQUARED distance.
        ///
        /// 120 squared-units = <b>10.95 metres</b>, NOT 120 metres. This trips people up because
        /// the value reads like a distance while it is compared against sqrMagnitude, and both
        /// per-frame callers use the default. 10.95m is the intended behaviour and has shipped
        /// this way - do NOT "fix" this to 14400 without deciding that deliberately, because it
        /// would turn a ~11m awareness bubble into a 120m one.
        /// </summary>
        public const int DEFAULT_SIGHT_RANGE_SQR = 120;

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

        // Resolved once in Start from config. See GetAllowedLinks for why InternalTeleports alone
        // is effectively "no links at all" in the base game.
        private SmartPathfindingLinkFlags _allowedLinks = SmartPathfindingLinkFlags.InternalTeleports;

        // Reused across every CheckZonePath probe. NavMeshPath wraps native memory, and this used
        // to be allocated inside the zone loop - up to 8 per probe, 16 per evaluation. b8's G6 added
        // _scratchPath and converted the other call site but missed this one. Kept as its own field
        // rather than sharing _scratchPath: nothing forces those two call stacks to stay disjoint,
        // and a second NavMeshPath costs one allocation for the lifetime of the Gargoyle.
        private NavMeshPath? _zoneScratchPath;

        // "Cornered" tracking. _seenSinceTime is when the CURRENT unbroken stretch of being watched
        // began (0 = not currently seen); _corneredAggroUntil is how long the resulting charge is
        // committed for, which has to exist or HandleOutOfAggroRange yanks him straight back into
        // GetOutOfSight on the very next tick while the player is still looking at him.
        private float _corneredSince;
        private Vector3 _corneredAnchorPos;
        private float _corneredAggroUntil;
        private float corneredAggroDelay;
        private const float CORNERED_AGGRO_COMMIT = 8f;

        // How far he has to actually GET, while being watched, to count as escaping rather than
        // being trapped. At GetOutOfSight speed (baseSpeed * 1.5, so ~6 m/s by default) a genuine
        // run for cover clears this in well under two seconds, while pacing, jamming on a corner or
        // shuffling between two equally exposed spots never clears it at all.
        private const float CORNERED_PROGRESS_DIST = 8f;

        // Did the LAST hide evaluation that actually ran find somewhere genuinely hidden?
        // This is the half b18 was missing. Being watched is not the same as being trapped, and
        // escalating on line of sight alone turned him aggressive while he still had perfectly
        // good cover to walk to. Starts true so he never counts as cornered before he has tried.
        private bool _hideFoundCover = true;

        // Set by ChooseClosestNodeToPos: true when nothing passed the filters and it had to return
        // a merely-best-ranked node instead of a hidden one.
        private bool _nodeChoiceFellBack;

        // Self-throttle for the hide/destination trace below. Deliberately NOT relying on LGLog's
        // diagRepeatLimit: these messages interpolate a live distance, so every one is a different
        // string and the repeat limiter would never collapse them.
        private float _nextHideTraceTime;

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
        private float steamIDTauntCooldown = 90f;
        private float distWarnSqr = 0f;
        private float bufferDistSqr = 0f;
        private float awareDistSqr = 0f;
        private float idleDistanceSqr = 0f;
        private bool enablePush = false;

        // HYSTERESIS. Both of these used to be a single hard line, and a single line is a machine
        // for making an AI twitch: the moment the Gargoyle sits ON the threshold, one step of the
        // player's toggles the state, every tick, forever.
        //
        // Measured in Mathew's 2026-08-16 log, not theorised. `Idle Distance` (20m): THIRTEEN
        // consecutive AI dumps at the identical position with velocity 0 while the player wandered
        // between 10.5m and 19.8m, and ten more hovering at 18.5-19.8m inching a metre at a time -
        // which is the "walks back and forth a few feet" report, exactly. `Awareness` (40m): four
        // acquire-then-lose cycles back to back, every acquisition followed immediately by a hide
        // evaluation at 39.6-40.0m.
        //
        // Enter on the configured distance, LEAVE on the wider one. The player's setting still
        // means what it says (that is the distance at which the behaviour starts); the widened
        // value only decides when it stops, so nothing needs a new config entry.
        private const float IDLE_HYSTERESIS = 1.2f;
        private const float AWARE_HYSTERESIS = 1.15f;
        private float idleKeepDistSqr = 0f;
        private float awareKeepDistSqr = 0f;

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

        // Last footstep volume ApplyStateAudio pushed into creatureSFX. Starts off any real value
        // so the first Update applies it on every machine, clients included.
        private float _lastSfxVolume = -1f;

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

            // Deliberately NOT StartSearch(transform.position) any more. That is VANILLA roaming,
            // and it was always immediately superseded by SearchForPlayers()'s StartSmartSearch on
            // the first Update - harmlessly, back when that ran unguarded every frame.
            //
            // It stops being harmless the moment SearchForPlayers is guarded on
            // currentSearch.inProgress (P1): vanilla StartSearch sets inProgress = true right here,
            // so the guard would see a search already running and the SMART search would never
            // start at all. The Gargoyle would silently fall back to vanilla roaming for the whole
            // round - no error, no log line, just worse pathing than v0.7.0 shipped with.
            //
            // HandleSearchingForPlayerState starts the smart search on the first tick instead.
            // currentSearch is a serialized field, so it is non-null with inProgress false here.

            baseSpeed = Plugin.BoundConfig.baseSpeed.Value;

            attackDamage = Plugin.BoundConfig.attackDamage.Value;

            minTaunt = Plugin.BoundConfig.minTaunt.Value;
            maxTaunt = Plugin.BoundConfig.maxTaunt.Value;
            steamIDTauntCooldown = Plugin.BoundConfig.steamIDTauntCooldown.Value;

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
            awareKeepDistSqr = awareDistSqr * (AWARE_HYSTERESIS * AWARE_HYSTERESIS);

            idleKeepDistSqr = idleDistanceSqr * (IDLE_HYSTERESIS * IDLE_HYSTERESIS);

            enablePush = Plugin.BoundConfig.enablePush.Value;
            corneredAggroDelay = Plugin.BoundConfig.corneredAggroDelay.Value;

            // InternalTeleports is always allowed (other mods' portals register into it and it is
            // empty in vanilla). The two that change real behaviour are opt-in and default off.
            // Runtime-writable, unlike the prefab field it was baked from - so this can be tuned
            // without a bundle rebuild. Only touched when it differs, so the default is a no-op.
            float cfgRadius = Plugin.BoundConfig.agentRadius.Value;
            if (cfgRadius > 0f && !Mathf.Approximately(agent.radius, cfgRadius))
            {
                LGLog.Info(LogCat.Movement, $"{GargoyleTag} agent radius {agent.radius:0.00} -> {cfgRadius:0.00} (config)");
                agent.radius = cfgRadius;
            }

            _allowedLinks = SmartPathfindingLinkFlags.InternalTeleports;
            if (Plugin.BoundConfig.allowExitFacility.Value)
                _allowedLinks |= SmartPathfindingLinkFlags.MainEntrance | SmartPathfindingLinkFlags.FireExits;
            if (Plugin.BoundConfig.allowElevators.Value)
                _allowedLinks |= SmartPathfindingLinkFlags.Elevators;
            lastAttackTime = Time.time;
            pushTimer = Time.time;

            gargoyleTargets[myID] = targetPlayer;
            _lastTarget = targetPlayer;

            _nextAggroEvalTime = Time.time;

            creatureVoice.maxDistance *= 3;
            pathDelayTimer = Time.time;

            // Backdate past the cooldown so the first personal line is not gated at spawn. Must
            // track the config value, or raising the cooldown would silently re-gate that first one.
            lastSteamIDTauntTime = Time.time - (steamIDTauntCooldown + 1f);

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

            // Deliberately ABOVE both returns below. See ApplyStateAudio.
            ApplyStateAudio();

            if (!agent.enabled || !agent.isOnNavMesh) return;
            if (!IsOwner) return;

            // #if DEBUG, not [Conditional]. LogIfSlow below is [Conditional("DEBUG")] so its
            // CALLS vanish in Release - but the Stopwatch allocation and every
            // `float t0 = (float)sw.Elapsed...` local are ordinary statements that do not, so a
            // shipped build was allocating a Stopwatch and taking ~7 high-resolution timer
            // readings per frame per gargoyle to feed calls that had been compiled away.
#if DEBUG
            var sw = Stopwatch.StartNew();
#endif

            if (_lastTarget != targetPlayer)
            {
                gargoyleTargets[myID] = targetPlayer;
                _lastTarget = targetPlayer;
            }

            if (Time.time - lastNodeCheckTime > nodeCheckInterval)
            {
#if DEBUG
                var t0 = (float)sw.Elapsed.TotalMilliseconds;
#endif
                CheckAndRefreshAINodes();
#if DEBUG
                LogIfSlow("CheckAndRefreshAINodes", (float)sw.Elapsed.TotalMilliseconds - t0);
#endif
                lastNodeCheckTime = Time.time;
            }

            {
#if DEBUG
                float t0 = (float)sw.Elapsed.TotalMilliseconds;
#endif
                HandleTargetPlayer();
#if DEBUG
                LogIfSlow("HandleTargetPlayer", (float)sw.Elapsed.TotalMilliseconds - t0);
#endif
            }

            if (Time.time - lastSeenCheckTime > 0.33f)
            {
#if DEBUG
                float t0 = (float)sw.Elapsed.TotalMilliseconds;
#endif

                closestPlayer = GetClosestPlayer();
                // MaxValue, not 0. A null closest player used to be recorded as distance zero -
                // i.e. "a player is standing on top of me" - which stranded AggressivePursuit:
                // HandleAggressivePursuitState is wrapped in `if (closestPlayer != null)` so it
                // did nothing, and HandleAggroAndPush's `dist > aggroRangeSqr` arm could never
                // fire, so HandleOutOfAggroRange was unreachable. The gargoyle stopped dead
                // mid-chase with the chase animation still playing until its target died or
                // walked out of the 60m aware radius.
                distanceToClosestPlayerSqr = closestPlayer != null
                    ? (transform.position - closestPlayer.transform.position).sqrMagnitude
                    : float.MaxValue;
                // RISING EDGE OF "SPOTTED" MUST INTERRUPT THE CURRENT HIDE COMMITMENT.
                //
                // Mathew measured the reaction to being seen varying between instant and ~3s. The
                // timers stack: up to 0.33s to notice (this block), then up to 2.0s on the
                // pathDelayTimer commitment gate at the top of SetDestinationToHiddenPosition,
                // then up to 0.35s on HIDE_EVAL_INTERVAL, then up to 0.75s on REPATH_INTERVAL.
                // Worst case ~3.4s, best case ~0 - which is exactly the "varies" he described.
                //
                // Those gates are all correct for the steady state; they exist so the Gargoyle
                // commits to a hiding spot instead of dithering. Being spotted is the one event
                // that has to override the commitment, so expire them here rather than shortening
                // any of them - shortening them would bring back the dithering they prevent.
                bool nowSeen = GargoyleIsSeen(transform);
                if (nowSeen && !isSeen)
                {
                    pathDelayTimer = Time.time - 2f;
                    _nextHideEvalTime = 0f;
                    _nextPathRequestTime = 0f;
                    LGLog.Debug(LogCat.Movement, $"{GargoyleTag} spotted - clearing hide commitment for an immediate re-evaluation");
                    _corneredSince = Time.time;
                    _corneredAnchorPos = transform.position;
                }
                else if (!nowSeen && isSeen)
                {
                    // He got away. Both the cornered timer and any live charge commitment end here -
                    // breaking line of sight is exactly the outcome the escalation exists to reach,
                    // so reaching it must not leave him aggressive.
                    _corneredSince = 0f;
                    _corneredAggroUntil = 0f;
                }
                isSeen = nowSeen;

                UpdateCorneredTimer();

#if DEBUG
                LogIfSlow("Seen/Closest", (float)sw.Elapsed.TotalMilliseconds - t0,
                    $"closest={(closestPlayer != null ? closestPlayer.playerUsername : "null")} seen={isSeen}");
#endif

                lastSeenCheckTime = Time.time;
            }

            {
#if DEBUG
                float t0 = (float)sw.Elapsed.TotalMilliseconds;
#endif
                HandlePushStage();
#if DEBUG
                LogIfSlow("HandlePushStage", (float)sw.Elapsed.TotalMilliseconds - t0);
#endif
            }

            {
#if DEBUG
                float t0 = (float)sw.Elapsed.TotalMilliseconds;
#endif
                HandleBehaviorState();
#if DEBUG
                LogIfSlow("HandleBehaviorState", (float)sw.Elapsed.TotalMilliseconds - t0, $"state={StateToString(currentBehaviourStateIndex)}");
#endif
            }

            // EXACTLY ONE THING STEERS THE AGENT PER STATE.
            //
            // In SearchingForPlayer that is PathfindingLib's search coroutine, which drives the
            // agent through GoToSmartPathDestination. FollowSmartPath must NOT also run there: no
            // search-state code path ever calls SetSmartDestination, so `pathingTask` is whatever
            // the PREVIOUS state left behind, and FollowSmartPath would spend the whole search
            // dragging him back toward a goal that stopped being relevant a state change ago.
            //
            // The two then fed each other. Both wrote _lastActiveDestination, so each one's
            // SetDestination made the other's `destChanged` fire on the next frame, and they
            // overwrote each other every frame - the agent re-solved a path continuously and
            // never followed one, which reads in the log as velocity 0 with `dest` cycling
            // between nodes while `pos` does not move at all. Measured 2026-08-16: 11 dumps
            // frozen at one position across two separate searches.
            //
            // Link activation still has to happen in both cases, which is why it now lives in its
            // own method rather than at the bottom of FollowSmartPath.
            if (currentBehaviourStateIndex == (int)State.SearchingForPlayer)
            {
                TryActivateSmartLink();
            }
            else if (currentBehaviourStateIndex != (int)State.Idle)
            {
#if DEBUG
                float t0 = (float)sw.Elapsed.TotalMilliseconds;
#endif
                FollowSmartPath();
#if DEBUG
                LogIfSlow("FollowSmartPath", (float)sw.Elapsed.TotalMilliseconds - t0);
#endif
            }
#if DEBUG
            sw.Stop();
            LogIfSlow("UpdateTotal", (float)sw.Elapsed.TotalMilliseconds);
#endif
        }


        /// <summary>
        /// Per-interval state dump. The ENTIRE body is compile-time stripped in Release.
        ///
        /// It used to live inline in DoAIInterval with only the final LogIfDebugBuild call
        /// marked [Conditional("DEBUG")] - so a shipped build still built ~15 interpolated
        /// strings into locals, boxed a couple of dozen values, and called agent.remainingDistance
        /// (which forces the agent to walk its corner list) and pathingTask.GetResult(0) on the
        /// live pathing task, every 3 seconds per gargoyle, purely to format text that was then
        /// discarded. This is exactly the leak [Conditional] does not protect against.
        /// </summary>
        [Conditional("DEBUG")]
        private void LogAiIntervalState()
        {
            if (Time.time < _nextDoAiLogTime) return;

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
                    $"ranges: aware={Mathf.Sqrt(awareDistSqr):0.0}(keep {Mathf.Sqrt(awareKeepDistSqr):0.0}) aggro={Mathf.Sqrt(aggroRangeSqr):0.0} idle={Mathf.Sqrt(idleDistanceSqr):0.0}(keep {Mathf.Sqrt(idleKeepDistSqr):0.0}) atk={Mathf.Sqrt(attackRangeSqr):0.0} buffer={Mathf.Sqrt(bufferDistSqr):0.0}";

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

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            LogAiIntervalState();

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

            // See DelayDoorClose: this was gated on the static LGInstance, which one gargoyle's
            // death disabled for all the others.
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
                        // No StopSearch here any more - SwitchState owns it for every exit.
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

        public override void OnNetworkDespawn()
        {
            // Nothing used to clean up per-instance entries when the crew SURVIVED a round.
            // ClearAllVariables' only caller is the allPlayersDead branch of DoAIInterval, and the
            // two round-end hooks in AIHelperPatches are commented out - so a gargoyle despawned
            // mid-PushTarget left playerPushStates[player][itsID] == true forever. Every future
            // gargoyle's HandlePushTarget then saw a foreign `true` and deferred by 10s, on
            // repeat: that player became un-pushable for the rest of the SESSION, on every
            // subsequent moon.
            RemoveSelfFromSharedState();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            RemoveSelfFromSharedState();
            CleanupSmartPathing();
            base.OnDestroy();
        }

        /// <summary>
        /// Removes ONLY this instance's footprint from the shared static layer. Safe to call more
        /// than once, and deliberately does not touch the shared caches - other gargoyles may
        /// still be alive and using them.
        /// </summary>
        private void RemoveSelfFromSharedState()
        {
            gargoyleTargets.TryRemove(myID, out _);
            foreach (var kvp in playerPushStates)
            {
                kvp.Value.TryRemove(myID, out _);
            }
            activeGargoyles.Remove(this);
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

        /// <summary>
        /// Full reset of the shared static layer. This is a ROUND-END operation.
        ///
        /// It was being called from any gargoyle's DoAIInterval on every tick while allPlayersDead,
        /// which wiped caches out from under gargoyles that were still alive: clearing
        /// activeGargoyles made survivors talk over each other, and clearing the railing and
        /// kill-trigger caches meant FindNearestKillTrigger returned null so the push gate
        /// (distToKillTriggerSqr &lt;= 4f) could never pass again. The AI node lists were worse -
        /// unrecoverable, because RefreshNodesIfNull only refills when a cached entry is null, and
        /// an EMPTY list has no null entries.
        /// </summary>
        private static void ClearAllVariables()
        {
            activeGargoyles.Clear();
            gargoyleTargets.Clear();
            playerPushStates.Clear();
            // playerClasses is per-instance and is repopulated in Start; it is not shared state.
            cachedOutsideAINodes.Clear();
            cachedInsideAINodes.Clear();
            cachedAllAINodes.Clear();
            cachedKillTriggerInfos.Clear();
            cachedRailings.Clear();
            // Activity taunts persisted across moons and across lobbies because nothing ever
            // called this - RemoveActivity fires for PickedUpItem and InFacility but never for
            // KilledEnemy, so "you killed a Bracken" stayed valid all session.
            ClearAllPlayerData();
            LGLog.ResetRound();
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

        /// <summary>
        /// Single choke point for state changes - and the ONLY place the roam search is stopped.
        ///
        /// It used to be stopped in exactly one of the three exits from SearchingForPlayer (the
        /// FoundClosestPlayerInRange arm); HandleOutOfAggroRange's two exits to GetOutOfSight and
        /// StealthyPursuit both left it running. That was harmless while the search restarted every
        /// frame and never got anywhere - but the moment the P1 guard let it actually run, a leaked
        /// search coroutine became a SECOND writer of agent.SetDestination, fighting FollowSmartPath
        /// every frame through the ISmartAI callback. Two writers alternating at frame rate is a
        /// walk-a-few-feet-and-come-back oscillation, which is exactly what Mathew reported.
        ///
        /// Stopping it here rather than at each call site is deliberate: the old arrangement failed
        /// because it relied on every future exit remembering, and two of three did not.
        /// </summary>
        private void SwitchState(State state)
        {
            if (!IsOwner || currentBehaviourStateIndex == (int)state)
                return;

            if (currentBehaviourStateIndex == (int)State.SearchingForPlayer &&
                currentSearch != null && currentSearch.inProgress)
            {
                LGLog.Debug(LogCat.Movement, $"{GargoyleTag} leaving SearchingForPlayer -> stopping smart search");
                StopSearch(currentSearch);
            }

            // Hand the agent over clean. The search coroutine is about to become the only thing
            // steering, so anything the outgoing state was still driving toward has to go with it -
            // otherwise it sits in pathingTask for the whole search as a second opinion nobody
            // asked for. Paired with the Update gate; either alone leaves half the stall in place.
            if (state == State.SearchingForPlayer)
                ClearSmartPath("entering SearchingForPlayer");

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

            // awareKeepDistSqr, not awareDistSqr: this is the FORGET test, and forgetting has to be
            // harder than noticing or he drops and re-acquires the same player on alternate ticks.
            if (!sameRegionAsGargoyle ||
                !targetPlayer.isPlayerControlled ||
                targetPlayer.isPlayerDead ||
                distanceToPlayerSqr > awareKeepDistSqr)
            {
                targetPlayer = null;
                SwitchState(State.SearchingForPlayer);
            }
        }

        /// <summary>
        /// Sets the footstep volume from the current state, ON EVERY MACHINE.
        ///
        /// <para><c>creatureSFX</c> IS the footstep source: <c>LethalGargoylesSFX.PlayStep</c> is
        /// wired to an animation event in the prefab and PlayOneShots the step clip through this
        /// AudioSource. Animation events fire wherever the animation plays, which is everywhere -
        /// but every volume assignment used to live in a state handler, and those run from
        /// <c>Update</c> below its <c>!IsOwner</c> return. So a client's copy sat at the prefab
        /// default for the whole round and NOTHING the AI did to the volume ever reached it.
        /// Confirmed in game 2026-08-16: the push, which is supposed to be near-silent, was
        /// arriving at full volume for the player being pushed. Same shape as the push-damage bug
        /// in <c>ApplyPushClientRpc</c>, different symptom.</para>
        ///
        /// <para>No RPC needed: vanilla already replicates <c>currentBehaviourStateIndex</c>
        /// through <c>SwitchToBehaviourClientRpc</c>, so every machine can derive this for itself.
        /// THIS METHOD IS THE ONLY PLACE THE FOOTSTEP VOLUME IS SET - putting a
        /// <c>creatureSFX.volume</c> line back into a state handler re-breaks it for clients
        /// silently, because the host will sound perfectly correct.</para>
        /// </summary>
        private void ApplyStateAudio()
        {
            if (creatureSFX == null) return;

            float volume = currentBehaviourStateIndex switch
            {
                (int)State.Idle => 0f,
                (int)State.SearchingForPlayer => 1f,
                (int)State.StealthyPursuit => 0.5f,
                // 1.7 is out of range - Unity clamps AudioSource.volume to 0-1, so this has always
                // behaved as 1. Kept at the authored value rather than quietly retuned to 1.
                (int)State.AggressivePursuit => 1.7f,
                (int)State.GetOutOfSight => 1f,
                // Near-silent on purpose. PushTarget is the sneak-up-and-shove state and it used to
                // run at 1.7, the same as an open chase, which announced the one move that only
                // works if you do not hear it coming. Not zero: a faint scrape is still fair warning.
                (int)State.PushTarget => 0.05f,
                _ => 1f,
            };

            // Cached rather than read back off the AudioSource, because the 1.7 above would read
            // back as 1 and make the comparison fire an assignment every frame.
            if (_lastSfxVolume != volume)
            {
                _lastSfxVolume = volume;
                creatureSFX.volume = volume;
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
            agent.stoppingDistance = 0.2f;
            SearchForPlayers();
        }

        private void HandleStealthyPursuitState()
        {
            agent.speed = baseSpeed;
            agent.angularSpeed = 140f;
            agent.stoppingDistance = 0.1f;

            if (targetPlayer != null)
            {
                if (TryTeleportNearTarget(targetPlayer))
                    return;

                bool foundSpot = SetDestinationToHiddenPosition();
                if (!foundSpot)
                {
                    FallBackWhenNoCover();
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
            else
            {
                // Explicit exit. Without this the state had NO way out when GetClosestPlayer()
                // returned null (vanilla rejects players in an enemy animation or sinking, which
                // our own target-validity check does not), so the gargoyle just stood there.
                LGLog.Debug(LogCat.StateMachine, $"{GargoyleTag} AggressivePursuit -> SearchingForPlayer (no closest player)");
                SwitchState(State.SearchingForPlayer);
            }
        }

        /// <summary>
        /// Advances the "cornered" clock. Cornered means: watched continuously, and GOING NOWHERE.
        ///
        /// This is the third definition, and the previous two each failed in an instructive way.
        /// b18 keyed on line of sight alone, which fired while he was legitimately running for
        /// cover - being looked at is not being trapped. b20 keyed on the hide search reporting no
        /// cover, and never fired at all, because on the catwalk Mathew trapped him in there ARE
        /// hidden nodes down the dark end - the search finds them, he simply cannot get to them.
        /// "Can I find cover" and "am I getting away" are not the same question, and only the
        /// second one matches what a player means by cornered.
        ///
        /// So: measure actual displacement. Cover 8m and the clock re-anchors, because whatever he
        /// is doing is working. Fail to cover 8m while a player watches and it does not matter
        /// WHY - no cover, unreachable cover, jammed on a corner, pacing between two bad spots -
        /// he is stuck, and stuck is the thing worth reacting to.
        ///
        /// Runs from the Update seen-check, NOT from a state handler, deliberately: the previous
        /// version sat inside HandleGetOutOfSightState's `targetPlayer != null` body, so it could
        /// not run at all when the player watching him was not the player he had targeted - which,
        /// now that several Gargoyles spread across the crew, is common.
        /// </summary>
        private void UpdateCorneredTimer()
        {
            if (!isSeen)
            {
                _corneredSince = 0f;
                return;
            }

            if (_corneredSince <= 0f)
            {
                _corneredSince = Time.time;
                _corneredAnchorPos = transform.position;
                return;
            }

            // Getting somewhere. Slide the window rather than clearing it, so the test is always
            // "has he covered ground in the LAST few seconds", not "since he was first spotted".
            if ((transform.position - _corneredAnchorPos).sqrMagnitude >= CORNERED_PROGRESS_DIST * CORNERED_PROGRESS_DIST)
            {
                _corneredSince = Time.time;
                _corneredAnchorPos = transform.position;
            }
        }

        /// <summary>
        /// The hide search found nothing. Decide where to go anyway.
        ///
        /// If a player is WATCHING, back off - pick the reachable node furthest from him and head
        /// there, so the next evaluation samples for cover from a completely different place. That
        /// is Mathew's design and it is iterative by construction: sample, nothing, retreat, sample
        /// again from further out. Each retreat also puts real distance on the clock, which is what
        /// stops the cornered escalation firing while he still has somewhere to go.
        ///
        /// **What this replaces was actively backwards.** Both hide callers answered "no cover
        /// found" with SetSmartDestination(cachedTargetPosition) - they walked STRAIGHT AT the
        /// person they were hiding from. That is the "he tries to get close to me and then just
        /// stands there in my sight" Mathew reported, and it was flagged in the b10 audit as a
        /// consequence of the Bounds bug without ever being fixed on its own account.
        ///
        /// When NOT seen, approaching is still right - that is ordinary stalking, and changing it
        /// would turn him into an enemy that runs away from people who have not noticed him.
        /// </summary>
        private void FallBackWhenNoCover()
        {
            if (!isSeen)
            {
                SetSmartDestination(cachedTargetPosition);
                return;
            }

            Vector3 retreat = ChooseClosestNodeToPos(cachedTargetPosition, avoidLineOfSight: false,
                                                     preferFarFromPos: true);

            // Only worth taking if it actually gains ground; otherwise there is genuinely nowhere
            // to go, he stops covering distance, and the cornered clock is left to run out - which
            // is exactly when the aggression is supposed to take over.
            if (!IsInvalidPos(retreat) &&
                (retreat - transform.position).sqrMagnitude > 4f &&
                (retreat - cachedTargetPosition).sqrMagnitude > distanceToPlayerSqr)
            {
                if (HideTraceReady())
                    LGLog.Debug(LogCat.Movement,
                        $"{GargoyleTag} no cover - backing off {Vector3.Distance(transform.position, retreat):0.0}m to re-sample from further out");
                SetSmartDestination(retreat);
                return;
            }

            if (HideTraceReady())
                LGLog.Debug(LogCat.Movement, $"{GargoyleTag} no cover and nowhere to back off to - holding, cornered clock running");
        }

        private bool IsCorneredWhileSeen()
        {
            if (corneredAggroDelay <= 0f) return false;   // switched off in config
            if (!isSeen || _corneredSince <= 0f) return false;
            if (closestPlayer == null) return false;      // AggressivePursuit has nobody to charge
            return Time.time - _corneredSince >= corneredAggroDelay;
        }

        private void HandleGetOutOfSightState()
        {
            agent.speed = baseSpeed * 1.5f;
            agent.angularSpeed = 250f;
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
                    FallBackWhenNoCover();
                }
            }
        }

        private void HandlePushTargetState()
        {
            agent.speed = baseSpeed * 2.5f;
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

        /// <summary>
        /// Distance at which he should be standing still, widened while he already is.
        /// He settles at <c>Idle Distance</c> and does not get moving again until the player has
        /// opened up to 1.2x that, so drifting a step across the line no longer flips the state.
        /// </summary>
        private float CurrentIdleThresholdSqr() =>
            currentBehaviourStateIndex == (int)State.Idle ? idleKeepDistSqr : idleDistanceSqr;

        private void HandleOutOfAggroRange()
        {
            // A cornered charge has to survive this method. Without the guard the `isSeen` arm below
            // fires on the very next tick - the player is still looking at him, after all - and puts
            // him straight back into GetOutOfSight, so the escalation would last a single frame and
            // change nothing. The commitment ends early on its own if he breaks line of sight.
            if (Time.time < _corneredAggroUntil)
                return;

            // LAST RESORT, and it has to be tested HERE rather than inside HandleGetOutOfSightState.
            // That handler's whole body is gated on `targetPlayer != null`, so when the player doing
            // the watching is not the one this Gargoyle has targeted - routine now that several of
            // them spread across the crew - the escalation could never run. This method has no such
            // gate, and it is already the exact decision point for "a player can see me".
            if (IsCorneredWhileSeen())
            {
                _corneredAggroUntil = Time.time + CORNERED_AGGRO_COMMIT;
                _corneredSince = 0f;
                LGLog.Info(LogCat.StateMachine,
                    $"{GargoyleTag} cornered - watched {corneredAggroDelay:0.0}s without covering {CORNERED_PROGRESS_DIST:0}m, going aggressive");
                SwitchState(State.AggressivePursuit);
                return;
            }

            if (isSeen)
            {
                SwitchState(State.GetOutOfSight);
            }
            else if (targetPlayer != null && distanceToPlayerSqr <= CurrentIdleThresholdSqr())
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

        /// <summary>
        /// Starts the roaming search - ONCE per stint in SearchingForPlayer, not once per tick.
        ///
        /// HandleSearchingForPlayerState runs from Update, so this used to be called every single
        /// frame, and PathfindingLib's StartSmartSearch is not a "keep searching" call - it is a
        /// "start over" call. Its first two statements are `newSearch = new AISearchRoutine()` and
        /// `enemy.StopSearch(enemy.currentSearch, clear: true)`, and vanilla StopSearch with
        /// clear:true stops both search coroutines, runs a RoundManager node refresh and does
        /// `search.unsearchedNodes = allAINodes.ToList()`. So every frame the mod threw away the
        /// search that was running, allocated a fresh routine and a fresh copy of every AI node in
        /// the level, and started two coroutines that were killed ~16ms later.
        ///
        /// CurrentSmartSearchCoroutine opens with `yield return null` and then waits on
        /// WaitUntil(choseTargetNode) - it CANNOT complete inside one frame. So the search never
        /// picked even its first node and the Gargoyle did not roam at all while searching; it
        /// stood around waiting for someone to walk into its awareness radius.
        ///
        /// b8's stutter hunt never saw this because it profiled HandleStealthyPursuitState, which
        /// requires a target. This is the state with no target.
        /// </summary>
        void SearchForPlayers()
        {
            if (currentSearch != null && currentSearch.inProgress)
                return;

            LGLog.Debug(LogCat.Movement, $"{GargoyleTag} starting smart search (links={GetAllowedLinks()})");
            this.StartSmartSearch(transform.position, GetAllowedLinks());
        }

        /// <summary>
        /// ISmartAI callback: PathfindingLib telling us where to go next.
        ///
        /// This used to funnel all four arms into SetSmartDestination, which starts a SECOND
        /// SmartPathTask to a position the pathfinder had already solved - a redundant solve
        /// racing the search's own task - and, worse, discarded destination.Type, which is the
        /// one piece of information this callback exists to deliver: whether the waypoint is a
        /// place to walk to or a link to activate. FollowSmartPath does the activation, and it
        /// reads activeDestination, so the type has to survive.
        /// </summary>
        public void GoToSmartPathDestination(in SmartPathDestination destination)
        {
            activeDestination = destination;
            agent.SetDestination(destination.Position);

            // DO NOT write _lastActiveDestination here. It belongs to FollowSmartPath alone - it is
            // that method's record of the destination IT last issued, and its only use is the
            // `destChanged` test. Writing it from this callback meant the search coroutine's node
            // landed in FollowSmartPath's change detector, which read it as "my destination moved"
            // and immediately re-issued its own stale goal on top. That is one half of the stall;
            // the other half is FollowSmartPath running here at all, gated in Update.
        }

        /// <summary>
        /// Requests a smart path to <paramref name="destination"/>, subject to the repath throttle.
        ///
        /// THE BOOKKEEPING MUST NOT RUN UNLESS THE REQUEST ACTUALLY STARTED. SmartPathTask's
        /// StartPathTask opens with `if (jobData == null || IsComplete)` and silently does NOTHING
        /// when a job is still in flight - no exception, no restart, no signal. This method used to
        /// set _lastRequestedDest and arm the REPATH_INTERVAL cooldown BEFORE calling it, so
        /// whenever a job outlived the cooldown the mod believed it had requested a path to the new
        /// position while the task was still solving the old one, and FollowSmartPath kept driving
        /// to the stale result for at least another interval.
        /// </summary>
        private void SetSmartDestination(Vector3 destination)
        {
            if (pathingTask != null && pathingTask.IsStarted)
            {
                FollowSmartPath();

                // A job is still running. StartPathTask would be a no-op, so returning here is not
                // a throttle - it is the truth about what the library will do.
                if (!pathingTask.IsComplete)
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

            // agent.pathPending means "a path to this destination is ALREADY being computed".
            // Treating it as "needs a path" made this re-issue SetDestination every frame for as
            // long as the solve took, and each SetDestination re-queues the request - so under
            // load the agent could sit in the no-path state indefinitely, which also drops the
            // animation to Idle and trips ShouldEvaluateHide's escape hatch on repeat.
            bool destChanged = (_lastActiveDestination - destPos).sqrMagnitude > DEST_EPSILON_SQR;

            if (agent.pathPending)
            {
                // Already working on it. Leave it alone.
            }
            else if (destChanged || !agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                agent.SetDestination(destPos);
                _lastActiveDestination = destPos;
            }

            TryActivateSmartLink();
        }

        /// <summary>
        /// Activates the link at <see cref="activeDestination"/> once we are standing on it.
        ///
        /// SPLIT OUT OF FollowSmartPath ON PURPOSE. Those were two different jobs sharing a method:
        /// following <c>pathingTask</c> (which only the pursuit and hide states ever populate) and
        /// activating a link (which matters in EVERY state that moves, the search included, because
        /// PathfindingLib hands link waypoints to <see cref="GoToSmartPathDestination"/> as well).
        /// Gating the whole method off during the search - which is what stops the two-writer stall
        /// below - would otherwise have taken link traversal down with it.
        /// </summary>
        private void TryActivateSmartLink()
        {
            if (activeDestination == null) return;

            var dest = activeDestination.Value;
            Vector3 destPos = dest.Position;

            float activateDist = 1f + agent.stoppingDistance;
            if ((transform.position - destPos).sqrMagnitude <= activateDist * activateDist)
            {
                switch (dest.Type)
                {
                    case SmartDestinationType.InternalTeleport:
                        agent.Warp(dest.InternalTeleport.Destination.position);
                        InvalidatePathAfterLink("InternalTeleport");
                        break;

                    case SmartDestinationType.EntranceTeleport:
                        // EntranceTeleport.exitPoint was removed from the game; the far side is now
                        // reached through exitScript (the paired teleport) and its entrancePoint.
                        // FindExitPoint() resolves that pairing and returns false when there isn't
                        // one, which is also the null guard the old exitPoint read never had.
                        EntranceTeleport entrance = dest.EntranceTeleport;
                        if (entrance.FindExitPoint() && entrance.exitScript != null)
                        {
                            agent.Warp(entrance.exitScript.entrancePoint.position);

                            // SetEnemyOutside, NOT `isOutside = !isOutside`. The vanilla setter
                            // also calls GetAINodes(), which repopulates allAINodes for the region
                            // it just entered. isOutside gates node selection, cover search and
                            // every same-region player check in this file, so getting it wrong
                            // leaves the Gargoyle hunting for players it can never reach.
                            SetEnemyOutside(!isOutside);
                            InvalidatePathAfterLink("EntranceTeleport");
                        }
                        break;

                    case SmartDestinationType.Elevator:
                        // NOT invalidated here, deliberately. CanActivateDestination returns
                        // IsInsideElevator() for a RIDE destination, so the Gargoyle has to stay
                        // put in the car until the ride finishes; re-pathing mid-ride would walk
                        // it back out. Calling the elevator is idempotent.
                        if (dest.CanActivateDestination(transform.position))
                            dest.ElevatorFloor.CallElevator();
                        break;
                }
            }
        }

        /// <summary>
        /// Throws away every piece of pathing state after the Gargoyle traverses a link.
        ///
        /// Without this it loops the link forever: the warp leaves _lastActiveDestination and
        /// pathingTask still pointing at the link's near side, and agent.hasPath is false straight
        /// after a Warp - so the very next FollowSmartPath calls SetDestination on the teleport it
        /// just came out of and walks back in. GetResult returns intermediate link waypoints before
        /// the final goal, so a multi-hop path is the NORMAL shape here, not an edge case. This
        /// never fired before only because InternalTeleports is the one link flag with nothing
        /// behind it in vanilla.
        /// </summary>
        private void InvalidatePathAfterLink(string linkKind) =>
            ClearSmartPath($"traversed {linkKind}; re-pathing from the far side");

        /// <summary>
        /// Drops every piece of smart-path state: the in-flight task, the active waypoint, the
        /// change-detection record and the repath cooldown.
        ///
        /// <para>Two callers, same requirement for opposite reasons. After a link traversal the
        /// state is stale because the Gargoyle is no longer where it was computed from. On entering
        /// the search it is stale because the search coroutine is taking over the agent and a
        /// leftover task would fight it for <c>SetDestination</c>.</para>
        /// </summary>
        private void ClearSmartPath(string reason)
        {
            LGLog.Debug(LogCat.Movement, $"{GargoyleTag} clearing smart path ({reason})");

            // Guarded: ResetPath on an agent that is off the navmesh logs a Unity error and does
            // nothing useful. InvalidatePathAfterLink always calls this straight after a Warp, so
            // the guard is normally true; it matters on the entering-search path.
            if (agent != null && agent.isOnNavMesh)
                agent.ResetPath();

            pathingTask?.Dispose();
            pathingTask = null;
            activeDestination = null;
            _lastActiveDestination = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

            // Let the next tick request immediately rather than sitting on the old cooldown.
            _nextPathRequestTime = 0f;
        }

        /// <summary>
        /// Which PathfindingLib smart-links the Gargoyle may traverse.
        ///
        /// InternalTeleports is always on and always has been - but it is the ONE flag with nothing
        /// behind it in the base game. It exists so OTHER mods can register portals through
        /// SmartPathfinding.RegisterInternalTeleport, so vanilla-only the list is empty. The three
        /// flags with real content (MainEntrance, FireExits, Elevators) were hardcoded OFF, which
        /// is why the EntranceTeleport arm above has never once executed.
        ///
        /// Both new flags are config-gated and DEFAULT OFF - turning one on is new movement
        /// behaviour, not a tuning change.
        /// </summary>
        private SmartPathfindingLinkFlags GetAllowedLinks() => _allowedLinks;

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

        /// <summary>
        /// Throttles the (expensive) hide evaluation.
        ///
        /// The no-path / pending / invalid escape hatch used to return true UNCONDITIONALLY -
        /// but that is exactly the state a gargoyle sits in while it is re-pathing, which is
        /// precisely when the evaluation is most expensive. So HIDE_EVAL_INTERVAL was bypassed
        /// whenever it mattered and the search could run every single frame. The escape hatch is
        /// still there (it must be, or a stuck agent never re-evaluates) but it now obeys a short
        /// floor instead of no floor at all.
        /// </summary>
        private bool ShouldEvaluateHide()
        {
            if (!agent.hasPath || agent.pathPending || agent.pathStatus == NavMeshPathStatus.PathInvalid)
                return Time.time >= _nextHideEvalTime - (HIDE_EVAL_INTERVAL - 0.10f);

            return Time.time >= _nextHideEvalTime;
        }

        /// <summary>
        /// Rate-limits the hide trace to ~1/s. Diagnostic only; costs nothing when the Movement
        /// category is below Debug, which is the shipped default.
        /// </summary>
        private bool HideTraceReady()
        {
            if (!LGLog.On(LogCat.Movement, LGLevel.Debug)) return false;
            if (Time.time < _nextHideTraceTime) return false;
            _nextHideTraceTime = Time.time + 1.0f;
            return true;
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
                // Ranking stays closest-to-player ON PURPOSE. Vanilla's Bracken uses the closest
                // node to stalk and the FARTHEST to flee, and by that reading this is the wrong
                // primitive - but the Gargoyle is meant to stay near you and be unsettling, not to
                // run away. Nearest-hidden-first gives "creeps as close as he can, but only through
                // places you cannot see", which is the design. Retreat is now handled separately,
                // by FallBackWhenNoCover, and only once this has genuinely found nothing.
                Vector3 node = ChooseClosestNodeToPos(cachedTargetPosition, avoidLineOfSight: true,
                                                      requireHiddenFromPlayers: true);

                _hideFoundCover = !_nodeChoiceFellBack;

                if (HideTraceReady())
                    LGLog.Debug(LogCat.Movement,
                        $"{GargoyleTag} hide=FAR cover={_hideFoundCover} seen={isSeen} " +
                        $"moved={(isSeen && _corneredSince > 0f ? $"{Vector3.Distance(transform.position, _corneredAnchorPos):0.0}m in {Time.time - _corneredSince:0.0}s" : "n/a")} " +
                        $"dist={Mathf.Sqrt(distanceToPlayerSqr):0.0}m idle={Mathf.Sqrt(idleDistanceSqr):0.0}m " +
                        (_hideFoundCover
                            ? $"node={node.ToString("0.0")} ({Vector3.Distance(transform.position, node):0.0}m away)"
                            : "node=NONE -> handing off to the back-off"));

                // Report the truth. This branch used to return true unconditionally, which meant a
                // failed search still looked like success and the caller never got the chance to
                // back off - it just walked to whatever visible node ranked best and stopped there.
                if (!_hideFoundCover)
                    return false;

                SetSmartDestination(node);
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
            {
                _hideFoundCover = false;
                if (HideTraceReady())
                    LGLog.Debug(LogCat.Movement,
                        $"{GargoyleTag} hide=NEAR dist={Mathf.Sqrt(distanceToPlayerSqr):0.0}m -> NO COVER POINTS " +
                        $"(target={(targetPlayerTransform == null ? "null" : "ok")}); caller will walk straight at the player");
                return false;
            }

            // Four rejections is a compromise, not a magic number. IsVisibleToAnyRelevantPlayer
            // costs one HasLineOfSightToPosition per live player, and this can be running on six
            // Gargoyles at once, so an unbounded "keep trying until one is genuinely hidden" loop
            // is not affordable. Whatever is left stale gets pruned on the next evaluation instead.
            const int MAX_STALE_COVER_REJECTS = 4;

            Vector3 bestCoverPoint = default;
            float minDistanceSqr = float.MaxValue;
            int staleRejects = 0;

            for (int attempt = 0; attempt <= MAX_STALE_COVER_REJECTS; attempt++)
            {
                bestCoverPoint = PickCoverPointInBand(coverPoints, targetPlayerTransform.position, out minDistanceSqr);

                if (bestCoverPoint == default)
                    break;

                // RE-TEST VISIBILITY HERE, because the harvest-time verdict goes stale.
                // FindCoverPointsAroundTarget filters every point through this same check once, and
                // the list is then cached until the player moves 3m or the cooldown expires - so a
                // player who stands still and simply TURNS AROUND leaves every point in the cache
                // still flagged hidden while being in plain view. He would walk to one, stand there
                // exposed, re-pick the very same point because it is still the nearest, and never
                // move again until the cornered timer bailed him out.
                //
                // Measured in the 2026-08-16 log: 4 of 23 hide evaluations chose a point 0.0-0.2m
                // away - he was standing ON it - and BOTH cornered escalations in that session were
                // directly preceded by one of those lines.
                if (!IsVisibleToAnyRelevantPlayer(bestCoverPoint))
                    break;

                // Drop it for good rather than merely skipping it this once. Pruning is what stops
                // the same dead point being re-picked on every later evaluation, and an empty list
                // is itself a rebuild trigger, so the cache heals rather than rotting.
                coverPoints.Remove(bestCoverPoint);
                bestCoverPoint = default;
                staleRejects++;
            }

            if (bestCoverPoint != default)
            {
                _hideFoundCover = true;

                if (HideTraceReady())
                    LGLog.Debug(LogCat.Movement,
                        $"{GargoyleTag} hide=NEAR dist={Mathf.Sqrt(distanceToPlayerSqr):0.0}m -> cover point " +
                        $"{Mathf.Sqrt(minDistanceSqr):0.0}m from player, {Vector3.Distance(transform.position, bestCoverPoint):0.0}m away " +
                        $"(from {coverPoints.Count} candidates, band {Mathf.Sqrt(bufferDistSqr):0.0}-{Mathf.Sqrt(awareDistSqr):0.0}m" +
                        (staleRejects > 0 ? $", {staleRejects} stale point(s) pruned)" : ")"));

                SetSmartDestination(bestCoverPoint);
                pathDelayTimer = Time.time;
                return true;
            }

            _hideFoundCover = false;

            // Both passes failed: cover points exist but every one of them is outside the accepted
            // distance band from the player. Worth calling out separately from "no cover points at
            // all" - the two have completely different causes and the old log conflated them.
            if (HideTraceReady())
                LGLog.Debug(LogCat.Movement,
                    $"{GargoyleTag} hide=NEAR dist={Mathf.Sqrt(distanceToPlayerSqr):0.0}m -> {coverPoints.Count} cover points but NONE " +
                    $"in band {Mathf.Sqrt(bufferDistSqr):0.0}-{Mathf.Sqrt(awareDistSqr):0.0}m, and none past fallback {Mathf.Sqrt(aggroRangeSqr + 2f):0.0}m" +
                    (staleRejects > 0 ? $" ({staleRejects} stale point(s) pruned this pass)" : ""));

            LogIfDebugBuild("No suitable hiding spot found.");
            return false;
        }

        /// <summary>
        /// Nearest cover point to the player that sits in the accepted distance band, falling back
        /// to anything past aggro range when the band itself comes up empty. Returns
        /// <c>default</c> when neither pass finds anything.
        ///
        /// <para>Lifted verbatim out of <see cref="SetDestinationToHiddenPosition"/> so the caller
        /// can run it more than once - it now has to be able to reject a pick and ask again.
        /// The two-pass shape and the ordering are unchanged: nearest-to-the-player still wins,
        /// which is what keeps him uncomfortably close rather than fleeing.</para>
        /// </summary>
        private Vector3 PickCoverPointInBand(List<Vector3> coverPoints, Vector3 playerPosition, out float pickedDistSqr)
        {
            Vector3 best = default;
            float minDistanceSqr = awareDistSqr;

            foreach (var coverPoint in coverPoints)
            {
                float distanceSqr = (playerPosition - coverPoint).sqrMagnitude;
                if (distanceSqr >= bufferDistSqr && distanceSqr < minDistanceSqr)
                {
                    best = coverPoint;
                    minDistanceSqr = distanceSqr;
                }
            }

            if (best == default)
            {
                minDistanceSqr = float.MaxValue;
                foreach (var coverPoint in coverPoints)
                {
                    float distanceSqr = (playerPosition - coverPoint).sqrMagnitude;
                    if (distanceSqr >= aggroRangeSqr + 2f && distanceSqr < minDistanceSqr)
                    {
                        best = coverPoint;
                        minDistanceSqr = distanceSqr;
                    }
                }
            }

            pickedDistSqr = minDistanceSqr;
            return best;
        }

        public List<Vector3> FindCoverPointsAroundTarget()
        {
            List<Vector3> coverPoints = [];

            Vector3 targetPlayerPosition = cachedTargetPosition;
            // Y WAS 2, AND UNITY'S Bounds TAKES A FULL SIZE, NOT EXTENTS - so this box was +/-20m
            // horizontally but only +/-1m VERTICALLY. Any AI node more than a metre above or below
            // both the player and the Gargoyle was excluded no matter how close it was, which on
            // stairs or across floors is nearly all of them. The cachedAllAINodes fallback reapplies
            // the same bounds, so it rescued nothing. An empty node list yields zero cover points,
            // and BOTH callers respond to zero cover points by pathing straight at the player -
            // i.e. in a stairwell the "hide" behaviour was literally "walk at the person watching
            // you". 20 is +/-10m, which spans a floor or two without dragging in the whole level.
            Bounds playerBounds = new(targetPlayerPosition, new Vector3(40, 20, 40));
            Bounds gargoyleBounds = new(transform.position, new Vector3(40, 20, 40));

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

                    if (!IsVisibleToAnyRelevantPlayer(potentialPos))
                    {
                        coverPoints.Add(potentialPos);
                    }
                }
            }

            // The node count is the number to watch. Both Bounds above are built as
            // `new Bounds(centre, new Vector3(40, 2, 40))`, and Unity's Bounds takes a FULL size -
            // so that is +/-20m horizontally but only +/-1m VERTICALLY. On stairs or across floors
            // a node one storey up is excluded no matter how close it is horizontally. If this logs
            // nodes=0 while the Gargoyle is clearly near AI nodes, that Y extent is the reason.
            if (LGLog.On(LogCat.Movement, LGLevel.Debug))
                LGLog.Debug(LogCat.Movement,
                    $"{GargoyleTag} cover rebuild: {validAINodes.Count} nodes in bounds (+/-20m XZ, +/-10m Y) -> {coverPoints.Count} cover points" +
                    (validAINodes.Count == 0 ? " [NO NODES near the target - hide will fall through to walking at the player]" : ""));

            return coverPoints;
        }

        /// <summary>
        /// Can any living player in the Gargoyle's own region currently see this point?
        ///
        /// Lifted out of FindCoverPointsAroundTarget so the &gt;idleDistance hide branch can use the
        /// same test. That branch had NO player-visibility check at all - it filtered only on
        /// whether the ROUTE crossed line-of-sight geometry, never on whether the DESTINATION was
        /// somewhere the player could see. So the "get out of sight" path could happily pick a spot
        /// in the middle of the watcher's view, which is precisely the "he can't get out of my view"
        /// report. Deliberately drops the &plusmn;20m bounds filter the cover search applies to
        /// players: at &gt;20m the whole point is that the watcher may be outside that box.
        /// </summary>
        private bool IsVisibleToAnyRelevantPlayer(Vector3 point)
        {
            var players = StartOfRound.Instance.allPlayerScripts;
            for (int p = 0; p < players.Length; p++)
            {
                var player = players[p];
                if (player == null || player.isPlayerDead || !player.isPlayerControlled)
                    continue;

                if (isOutside != player.isInsideFactory)
                    continue;

                if (player.HasLineOfSightToPosition(point, 60f, 60, 25f))
                    return true;
            }
            return false;
        }

        // Scratch buffers, reused across calls. This runs every Update tick from
        // HandleStealthyPursuitState and HandleGetOutOfSightState, so per-call List allocation
        // here was pure garbage.
        private readonly List<GameObject> _nodeScratch = [];
        private readonly List<(float distSqr, Transform t)> _candidateScratch = [];
        private readonly List<(float distSqr, Transform t)> _bestScratch = [];

        /// <summary>
        /// Picks a nearby AI node that is not visible along the path to it.
        ///
        /// SORT FIRST, PATH SECOND. This used to call PathIsIntersectedByLOS - a full
        /// agent.CalculatePath plus a NavMesh.SamplePosition plus up to 12 Physics.Linecasts -
        /// on EVERY node, and only then compute distance and sort. On a 100-200 node interior
        /// that was 100-200 navmesh solves in a single frame, per gargoyle, and it paid full
        /// price for nodes 300m away that could never win. A playtest measured
        /// HandleStealthyPursuitState at 13.6ms average and 26ms peak against a 16.67ms frame
        /// budget, with four gargoyles alive - this was the stutter.
        ///
        /// Now the cheap squared-distance sort happens first and only the nearest few are
        /// pathed, stopping as soon as enough have passed.
        /// </summary>
        /// <param name="requireHiddenFromPlayers">
        /// Reject any node a living same-region player can currently SEE. The hide branch needs
        /// this; the "get to the player" fallback in GetTargetPosition must not have it.
        /// </param>
        /// <param name="preferFarFromPos">
        /// Rank FARTHEST from <paramref name="pos"/> instead of nearest. This is the retreat
        /// primitive, and vanilla splits the same way: the Bracken uses closest-to-player to stalk
        /// and farthest-from-player to flee.
        /// </param>
        public Vector3 ChooseClosestNodeToPos(Vector3 pos, bool avoidLineOfSight = false, int offset = 0,
                                              bool requireHiddenFromPlayers = false,
                                              bool preferFarFromPos = false)
        {
            // How many nodes we are willing to run the expensive check on.
            const int MAX_PATH_CHECKS = 24;

            // Separate, much larger budget for the CHEAP filter. Visibility is a distance and angle
            // test that mostly early-outs, then at worst one linecast per player; PathIsIntersected-
            // ByLOS is a full navmesh solve plus up to 12 linecasts. Testing visibility over a wide
            // ring and pathing only the survivors buys a far longer reach for less work than the old
            // arrangement, which path-checked the 24 nodes NEAREST THE PLAYER and gave up. Those 24
            // are the likeliest nodes in the level to be in the player's view, which is why he kept
            // finding nothing and stopping.
            const int MAX_VISIBILITY_SCANS = 160;
            const int HIDDEN_CANDIDATES_WANTED = 8;

            // When hiding, refuse nodes we are basically standing on. Vanilla's Bracken does the
            // same for its evade destination (FlowermanAI discards results within 5m of itself);
            // without a floor the "best" node is regularly the one under our feet, which produces
            // a destination we have already arrived at.
            const float MIN_SELF_DIST_SQR = 25f;

            _nodeScratch.Clear();
            if (isOutside)
            {
                foreach (var node in cachedOutsideAINodes) if (node != null) _nodeScratch.Add(node);
                if (_nodeScratch.Count == 0) foreach (var node in cachedAllAINodes) if (node != null) _nodeScratch.Add(node);
            }
            else
            {
                foreach (var node in cachedInsideAINodes) if (node != null) _nodeScratch.Add(node);
                if (_nodeScratch.Count == 0) foreach (var node in cachedAllAINodes) if (node != null) _nodeScratch.Add(node);
            }

            int need = Mathf.Max(0, offset) + 1;

            // Cheap pass: squared distance only, no pathing, no raycasts.
            Vector3 self = transform.position;
            _candidateScratch.Clear();
            for (int i = 0; i < _nodeScratch.Count; i++)
            {
                var t = _nodeScratch[i].transform;
                if (requireHiddenFromPlayers && (self - t.position).sqrMagnitude < MIN_SELF_DIST_SQR)
                    continue;
                _candidateScratch.Add(((pos - t.position).sqrMagnitude, t));
            }

            if (_candidateScratch.Count == 0)
            {
                _nodeChoiceFellBack = true;
                return self;
            }

            if (preferFarFromPos)
                _candidateScratch.Sort(static (a, b) => b.distSqr.CompareTo(a.distSqr));
            else
                _candidateScratch.Sort(static (a, b) => a.distSqr.CompareTo(b.distSqr));

            _bestScratch.Clear();

            if (requireHiddenFromPlayers)
            {
                // TWO PHASES, WIDE THEN DEEP. Walk outward from the player running only the cheap
                // visibility test, collecting the first few genuinely hidden nodes however far out
                // they turn out to be, and only then pay for navmesh solves on those. Ordering is
                // preserved, so the nearest hidden node still wins - he creeps as close as he can
                // while staying out of sight, rather than settling for the least-bad visible spot.
                int scans = Mathf.Min(_candidateScratch.Count, MAX_VISIBILITY_SCANS);
                for (int i = 0; i < scans && _bestScratch.Count < HIDDEN_CANDIDATES_WANTED; i++)
                {
                    if (!IsVisibleToAnyRelevantPlayer(_candidateScratch[i].t.position))
                        _bestScratch.Add(_candidateScratch[i]);
                }

                for (int i = 0; i < _bestScratch.Count; i++)
                {
                    var cand = _bestScratch[i];
                    if (PathIsIntersectedByLOS(cand.t.position, calculatePathDistance: false, avoidLineOfSight))
                        continue;

                    _nodeChoiceFellBack = false;
                    mostOptimalDistance = Mathf.Sqrt(cand.distSqr);
                    return cand.t.position;
                }

                _bestScratch.Clear();
            }
            else
            {
                int checks = Mathf.Min(_candidateScratch.Count, MAX_PATH_CHECKS);
                for (int i = 0; i < checks && _bestScratch.Count < need; i++)
                {
                    var cand = _candidateScratch[i];
                    if (PathIsIntersectedByLOS(cand.t.position, calculatePathDistance: false, avoidLineOfSight))
                        continue;
                    _bestScratch.Add(cand);
                }
            }

            if (_bestScratch.Count == 0)
            {
                // NEVER transform.position. Handing our own position back as a destination parks
                // the agent: it arrives instantly, agent.hasPath goes false, the animation drops to
                // Idle, and the next evaluation recomputes the identical answer - a stable stall
                // that only ends if the player moves. Batch G's 24-candidate cap widened this a
                // lot, because it now fires whenever the nearest 24 fail even though node 25 would
                // have passed; before the cap it needed EVERY node in the level to fail.
                //
                // Vanilla's ChooseClosestNodeToPosition falls back to the best-ranked node
                // regardless of its line-of-sight verdict, so do the same. An imperfect destination
                // still moves us, and moving is what breaks the loop.
                _nodeChoiceFellBack = true;
                var fallback = _candidateScratch[0];
                mostOptimalDistance = Mathf.Sqrt(fallback.distSqr);
                return fallback.t.position;
            }

            _nodeChoiceFellBack = false;
            var chosen = _bestScratch[_bestScratch.Count - 1];
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

            // HOIST THE CORNERS. NavMeshPath.corners is a PROPERTY backed by
            // CalculateCornersInternal() - every single read allocates a brand new Vector3[].
            // This method used to read it 2-3 times per loop iteration across up to 12 corners
            // plus 4 more before the loop, and ChooseClosestNodeToPos calls this once per AI
            // node - so one hide evaluation on a ~150-node interior was throwing away thousands
            // of arrays per frame. One read, one array.
            if (path1 == null)
                return true;

            Vector3[] corners = path1.corners;
            if (corners.Length == 0)
                return true;

            const int MAX_CORNERS_TO_SCAN = 12;
            int cornerCount = Mathf.Min(corners.Length, MAX_CORNERS_TO_SCAN);

            if (corners.Length <= 6)
            {
                Vector3 navTarget = RoundManager.Instance.GetNavMeshPosition(targetPos, RoundManager.Instance.navHit, 2.7f);
                if ((corners[^1] - navTarget).sqrMagnitude > 2.25f)
                    return true;
            }

            bool flag = false;

            if (calculatePathDistance)
            {
                for (int j = 1; j < cornerCount; j++)
                {
                    pathDistance += Vector3.Distance(corners[j - 1], corners[j]);

                    if (j <= 15 && (avoidLineOfSight || checkLOSToTargetPlayer))
                    {
                        if (!flag && j > 8 && (corners[j - 1] - corners[j]).sqrMagnitude < 4f)
                        {
                            flag = true;
                            j++;
                            continue;
                        }

                        flag = false;

                        if (checkLOSToTargetPlayer && targetPlayer != null &&
                            !Physics.Linecast(corners[j - 1], cachedTargetPosition + Vector3.up * 0.3f,
                                             StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                        {
                            return true;
                        }

                        if (avoidLineOfSight && Physics.Linecast(corners[j - 1], corners[j], 262144))
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
                    if (!flag && k > 8 && (corners[k - 1] - corners[k]).sqrMagnitude < 4f)
                    {
                        flag = true;
                        continue;
                    }

                    if (targetPlayer != null && checkLOSToTargetPlayer &&
                        !Physics.Linecast(Vector3.Lerp(corners[k - 1], corners[k], 0.5f) + Vector3.up * 0.25f,
                                         cachedTargetPosition + Vector3.up * 0.25f,
                                         StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    {
                        return true;
                    }

                    if (Physics.Linecast(corners[k - 1], corners[k], 262144))
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

        /// <summary>
        /// The ONLY sanctioned way to change this gargoyle's target.
        ///
        /// Multi-gargoyle target balancing was completely dead before this existed. The
        /// acquisition path in <see cref="FoundClosestPlayerInRange"/> set targetPlayer and
        /// _lastTarget but never gargoyleTargets[myID] - and because it DID set _lastTarget, the
        /// reconciler in Update (`if (_lastTarget != targetPlayer)`) saw them equal and never
        /// fixed it either. So the shared map stayed null for every gargoyle's entire life, every
        /// count in GetGargoyleTargetCounts came back zero, ChangeTarget was unreachable dead
        /// code, and FindBestTarget saw `0 &lt; fairShare` for everyone - so every gargoyle picked
        /// the same nearest player and the rest of the crew was never stalked at all.
        ///
        /// Three values, one write. Do not set any of them directly.
        /// </summary>
        private void SetTarget(PlayerControllerB? newTarget)
        {
            targetPlayer = newTarget;
            _lastTarget = newTarget;
            gargoyleTargets[myID] = newTarget;
        }

        /// <summary>
        /// Acquisition and rebalance for SearchingForPlayer. Returns true when this gargoyle holds
        /// a target and may switch to StealthyPursuit.
        ///
        /// THE SUBTLETY THAT BROKE THIS ONCE: this gargoyle's OWN claim is in targetCounts.
        /// GetGargoyleTargetCounts walks every gargoyle in `gargoyles`, us included, so a player we
        /// already hold always comes back with at least 1 against them. The old code compared that
        /// undiscounted count, and in every case EXCEPT an over-subscribed target it fell into
        /// `else { SetTarget(null); }` - dropping a claim it had no reason to drop - then asked
        /// FindBestTarget for a replacement using the SAME now-stale counts, where our own
        /// just-released claim still stood against us. With fairShare 1 (one gargoyle, or fewer
        /// gargoyles than players) the player we had just let go was then the one player
        /// FindBestTarget would refuse, so re-acquiring it always slipped a tick and the claim was
        /// briefly visible to every other gargoyle as unclaimed. Self-correcting, but it made
        /// re-engagement jittery and let two gargoyles double up through the gap.
        ///
        /// So: discount our own claim ONCE, up front. Every question below then reads as "how
        /// crowded is this player other than me", which is what was always meant.
        ///
        /// ChangeTarget deliberately does NOT do this. It asks a different question - "is this
        /// player over-subscribed across the whole pack" - and there our own claim SHOULD count.
        /// </summary>
        bool FoundClosestPlayerInRange()
        {
            Dictionary<PlayerControllerB, int> targetCounts = GetGargoyleTargetCounts();

            int fairShare = CalculateFairShare();

            // targetCounts is keyed on validPlayers, so absence from it IS the validity check:
            // the target died, disconnected, or left through the entrance.
            if (targetPlayer != null && !targetCounts.ContainsKey(targetPlayer))
            {
                LGLog.Debug(LogCat.Targeting, $"{GargoyleTag} dropping target {targetPlayer.playerClientId} (no longer a valid player)");
                SetTarget(null);
            }

            PlayerControllerB? held = targetPlayer;

            if (held != null)
            {
                targetCounts[held]--;

                bool overSubscribed = validPlayers.Count > 1 && targetCounts[held] >= fairShare;
                // Retention range, deliberately wider than the acquisition range FindBestTarget
                // uses. Keeping both at awareDistSqr made a player standing on the boundary get
                // released and re-acquired repeatedly - four full cycles back to back in the
                // 2026-08-16 log, each one costing a state reset and a fresh search.
                bool inRange = (transform.position - held.transform.position).sqrMagnitude <= awareKeepDistSqr;

                // Nothing wrong with what we already have. Keep it, and keep the claim intact.
                if (!overSubscribed && inRange)
                {
                    return true;
                }

                var better = FindBestTarget(targetCounts, fairShare);

                if (better != null)
                {
                    if (better != held)
                    {
                        LGLog.Debug(LogCat.Targeting, $"{GargoyleTag} retargeting from {held.playerClientId} to {better.playerClientId} (fairShare {fairShare})");
                        SetTarget(better);
                    }

                    return true;
                }

                LGLog.Debug(LogCat.Targeting, $"{GargoyleTag} released target {held.playerClientId} (overSubscribed {overSubscribed}, inRange {inRange})");
                SetTarget(null);
                return false;
            }

            var acquired = FindBestTarget(targetCounts, fairShare);

            if (acquired != null)
            {
                LGLog.Debug(LogCat.Targeting, $"{GargoyleTag} acquired target {acquired.playerClientId} (fairShare {fairShare}, {gargoyles.Count} gargoyle(s), {validPlayers.Count} valid player(s))");
                SetTarget(acquired);
                return true;
            }

            return false;
        }

        /// <summary>
        /// How many players each gargoyle may reasonably claim.
        /// Guards the divide: with every player dead or across the entrance while a gargoyle is
        /// still alive, validPlayers.Count is 0 and the old float divide produced
        /// CeilToInt(Infinity), which is undefined.
        /// </summary>
        private int CalculateFairShare()
        {
            if (validPlayers.Count == 0) return int.MaxValue;
            return Mathf.CeilToInt((float)gargoyles.Count / validPlayers.Count);
        }

        private void ChangeTarget()
        {
            Dictionary<PlayerControllerB, int> targetCounts = GetGargoyleTargetCounts();

            int fairShare = CalculateFairShare();
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
                        LGLog.Debug(LogCat.Targeting, $"{GargoyleTag} rebalancing target from {targetPlayer.playerClientId} to {newTarget.playerClientId} (count {targetCounts[targetPlayer]} > fairShare {fairShare})");
                        SetTarget(newTarget);
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

            // The two distances are reset HERE, once, before both probes. They used to be reset
            // at the top of CheckZonePath itself, so the "Right" call wiped whatever the "Left"
            // call had just measured and every comparison below was decided against a constant
            // 1000f - the left/right preference has never actually worked.
            leftPathDist = 1000f;
            rightPathDist = 1000f;

            bool leftPath = CheckZonePath(goRight: false);
            bool rightPath = CheckZonePath(goRight: true);

            bool goRight;
            if (rightPath && !leftPath) goRight = true;
            else if (leftPath && !rightPath) goRight = false;
            else if (rightPath && leftPath)
            {
                // Keep circling the way we are already going when the current zone commits to a
                // side, otherwise take the shorter path.
                if (IsRightZone(currentZone)) goRight = true;
                else if (IsLeftZone(currentZone)) goRight = false;
                else goRight = rightPathDist <= leftPathDist;
            }
            else goRight = false; // neither path is viable; fall through to the node fallback

            if (leftPath || rightPath)
            {
                // Pick the zone matching the direction actually chosen. This was computed before
                // the decision, so once the distances above started differing it would have
                // returned the RIGHT zone while claiming to have chosen left.
                RelativeZone targetZone = goRight ? nextZoneRight : nextZoneLeft;
                if (LGLog.On(LogCat.Movement, LGLevel.Trace))
                    LGLog.Trace(LogCat.Movement, $"{GargoyleTag} circling {(goRight ? "right" : "left")} from {currentZone} -> {targetZone} (L {leftPathDist:0.0} / R {rightPathDist:0.0})");
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

        private static bool IsRightZone(RelativeZone z) =>
            z is RelativeZone.FrontRight or RelativeZone.Right or RelativeZone.BackRight;

        private static bool IsLeftZone(RelativeZone z) =>
            z is RelativeZone.FrontLeft or RelativeZone.Left or RelativeZone.BackLeft;

        /// <summary>
        /// Probes whether the gargoyle can walk around the target in one direction, recording the
        /// path length into <see cref="leftPathDist"/> or <see cref="rightPathDist"/>.
        /// The caller resets both BEFORE calling this for each side - resetting them here made the
        /// second call destroy the first call's measurement.
        /// </summary>
        private bool CheckZonePath(bool goRight)
        {
            RelativeZone testZone = currentZone;
            RelativeZone nextZone;
            float pathDist = 0f;

            const int MAX_STEPS = 8;
            int steps = 0;

            while (steps++ < MAX_STEPS)
            {
                nextZone = goRight ? GetNextZone(testZone, 1) : GetNextZone(testZone, -1);

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

                NavMeshPath path = _zoneScratchPath ??= new NavMeshPath();
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
                    LogIfDebugBuild($"Path calculation failed. Exceeded max steps while checking {(goRight ? "Right" : "Left")} side.");
                }
                return false;
            }

            if (goRight)
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

        /// <summary>
        /// Convenience overload. Uses a REUSED NavMeshPath - it used to allocate a fresh one per
        /// call, and FindCoverPointsAroundTarget calls this up to 120 times per cover rebuild.
        /// NavMeshPath wraps native memory, so that was 120 allocate-and-finalize cycles.
        /// </summary>
        private bool CheckForPath(Vector3 sourcePosition, Vector3 targetPosition)
        {
            _scratchPath ??= new NavMeshPath();
            return CheckForPath(sourcePosition, targetPosition, _scratchPath);
        }
        private NavMeshPath? _scratchPath;

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

        public bool CanSeePlayer(PlayerControllerB player, float width = 180f, int rangeSqr = DEFAULT_SIGHT_RANGE_SQR, int proximityAwarenessSqr = -1)
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
            // `Count == 0` matters as much as the null check: once ClearAllVariables emptied these
            // lists, an empty list has no null entries, so the old condition was false forever and
            // the node caches could never refill without a fresh Start().
            bool nullNodesFound = cachedNodes.Count == 0 || cachedNodes.Any(node => node == null);

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
            // Tests `this`, not the static LGInstance. Every gargoyle overwrites LGInstance with
            // itself on spawn and nobody nulls it on death, so once the LAST-SPAWNED one died the
            // static went fake-null and every survivor silently stopped animating doors shut -
            // while still firing the RPC, so door state and visual disagreed.
            if (this != null)
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
                    // Skip ourselves. Without this the first candidate is always `this` at
                    // distance 0, so the method returned immediately every time and the
                    // "there's an enemy near me" warning was really just finding itself.
                    if (enemy == null || ReferenceEquals(enemy, this) || enemy.isEnemyDead) continue;

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

            Vector3 pushDirection;
            Vector3 pushForce;
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
                pushForce = pushDirection * 1.5f;
            }
            else
            {
                LogIfDebugBuild("Pushing player forward");
                pushDirection = player.transform.forward * 15f;
                pushForce = pushDirection;
            }

            // Damage and knockback are OWNER-AUTHORITATIVE in Lethal Company, and this method
            // only ever runs on the server (Update returns early on !IsOwner). Vanilla
            // DamagePlayer opens with `if (!IsOwner ...) return;`, and externalForceAutoFade is
            // consumed inside PlayerControllerB.Update behind the same owner gate - so writing
            // either one here landed on the server's dead replica of a remote player and did
            // NOTHING for anyone but the host. Confirmed in game 2026-08-16 (audit finding C3).
            // The push therefore has to be handed to the machine that owns the victim.
            // Broadcast-and-filter rather than ClientRpcParams: it is a handful of bytes to the
            // other clients and it avoids mapping player index to NGO client id, which is the
            // part that goes wrong.
            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
            {
                ApplyPushClientRpc((int)player.playerClientId, pushForce);
            }

            StartCoroutine(SetCauseOfDeathDelay(player, "Push"));
        }

        /// <summary>
        /// Applies the push damage and knockback on the victim's OWN machine, which is the only
        /// place Lethal Company will honour either. Every client receives this; all but the
        /// target return immediately.
        /// </summary>
        [ClientRpc]
        private void ApplyPushClientRpc(int playerId, Vector3 pushForce)
        {
            PlayerControllerB? local = GameNetworkManager.Instance != null
                ? GameNetworkManager.Instance.localPlayerController
                : null;

            if (local == null || (int)local.playerClientId != playerId) return;

            LGLog.Debug(LogCat.Combat, $"{GargoyleTag} pushing local player {playerId} with force {pushForce} (magnitude {pushForce.magnitude:F1})");

            local.DamagePlayer(PUSH_DAMAGE, false, true, CauseOfDeath.Gravity);
            local.externalForceAutoFade = pushForce;
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
            if (col != null) col.enabled = false;
            gargoyleTargets.TryRemove(myID, out _);

            foreach (var player in playerPushStates)
            {
                player.Value.TryRemove(myID, out _);
            }

            // Cleanup FIRST. This used to sit below the death taunt, and three separate things
            // in that taunt could throw before reaching it - an empty deathClips list on a client
            // that missed the audio transfer, an RPC send after base.KillEnemy already despawned
            // the NetworkObject, or a null Collider. Any of them left this instance in
            // activeGargoyles with its search coroutine still running on a corpse.
            if (searchCoroutine != null)
            {
                StopCoroutine(searchCoroutine);
            }
            activeGargoyles.Remove(this);

            var deathClips = Utility.AudioManager.deathClips;
            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned && deathClips.Count > 0)
            {
                int randomIndex = UnityEngine.Random.Range(0, deathClips.Count);
                TauntClientRpc(deathClips[randomIndex].name, "death");
            }
            else if (deathClips.Count == 0)
            {
                LGLog.Warn(LogCat.Audio, $"{GargoyleTag} died with no death clips loaded - no death line will play. On a client this usually means the audio transfer for 'Taunt - Gargoyle Death' never completed.");
            }
        }

        // ============================================================
        // 12) Taunts / audio
        // ============================================================

        /// <summary>
        /// Single choke point for the combat voice lines (attack / hit / player death).
        ///
        /// Its callers - HitEnemy, KillEnemy and OnCollideWithPlayer - are NOT server-only.
        /// Vanilla drives HitEnemy and KillEnemy through ClientRpcs so they run on every machine,
        /// and OnCollideWithPlayer runs on the colliding player's client. Sending a ClientRpc from
        /// there makes NGO throw "Only the server can invoke a ClientRpc" on every non-host
        /// machine, once per shovel hit - log spam that buries the real audio diagnostics, and the
        /// taunt does not fire anyway. SetAnim already guards this way; the taunt sends never did.
        /// </summary>
        public void PlayVoice(List<AudioClip> clipList, string clipType, AudioClip? clip = null)
        {
            if (!IsServer || NetworkObject == null || !NetworkObject.IsSpawned) return;

            if (clip == null && clipList.Count > 0)
            {
                int randInt = UnityEngine.Random.Range(0, clipList.Count);
                LGLog.Debug(LogCat.Taunt, $"{GargoyleTag} {clipType} voice ({clipList.Count} available)");
                TauntClientRpc(clipList[randInt].name, clipType, true);
            }
            else if (clipList.Count == 0)
            {
                LGLog.Warn(LogCat.Audio, $"{GargoyleTag} wanted a '{clipType}' line but that list is empty on the host - nothing will play anywhere.");
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

        /// <summary>
        /// Advances the general taunt gate. Any taunt path that does NOT route through
        /// <see cref="OtherTaunt"/> must call this, or the gate its callers read stays satisfied
        /// and Taunt() re-enters every frame.
        /// </summary>
        private void MarkTaunted()
        {
            lastGenTauntTime = Time.time;
            randGenTauntTime = UnityEngine.Random.Range(minTaunt, maxTaunt);
        }

        private bool TryPlayPlayerSpecificTaunt(int randInt, PlayerControllerB player)
        {
            if (randInt >= 160 && randInt < 175 && player.playerSteamId != 0 && Time.time - lastSteamIDTauntTime > steamIDTauntCooldown &&
                ChooseRandomClip($"{player.playerSteamId}", "SteamIDs", out string? playerClip) && playerClip != null)
            {
                TauntClientRpc(playerClip, "steamids");
                LGLog.Debug(LogCat.Taunt, $"{GargoyleTag} SteamID taunt for {player.playerUsername} (roll {randInt}, {genTauntCount} general taunts since last special)");
                genTauntCount = 0;
                // Both timers, on success. lastSteamIDTauntTime was assigned exactly once - in
                // Start, backdated past the cooldown - and never again, so the gate above was
                // permanently satisfied and personal lines could fire back to back. And without
                // the general timer, Taunt() re-ran every frame because its caller gates on
                // lastGenTauntTime, which only OtherTaunt was updating.
                lastSteamIDTauntTime = Time.time;
                MarkTaunted();
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
                    LGLog.Debug(LogCat.Taunt, $"{GargoyleTag} activity taunt ({randomActivity}) for {targetPlayer.playerUsername} (roll {randInt})");
                    genTauntCount++;
                    // Same omission as the SteamID path: every other success route goes through
                    // OtherTaunt, which advances these. This one did not, so the gate stayed open
                    // and Taunt() re-entered on the very next frame - which is not cheap, and
                    // produced bunched, back-to-back voice lines.
                    MarkTaunted();
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
                    // Wrap, don't increment. A bare ++ walks off the end whenever the draw is the
                    // last index AND equals lastTaunt - 1/Count^2 per call, which is 25% on the
                    // two-clip Hit pool, and guaranteed if a player disables all but one clip.
                    randomIndex = (randomIndex + 1) % clipList.Count;
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
            // Advance the timer on EVERY exit, not just on the 3% success. It was only written
            // inside the success branch, so once the window opened it stayed open and this method
            // - which walks all of RoundManager.SpawnedEnemies and calls GargoyleIsTalking() -
            // ran at FRAME RATE, per gargoyle, until a taunt finally fired.
            lastEnemyTauntTime = Time.time;
            randEnemyTauntTime = 1.5f;

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
                            LGLog.Debug(LogCat.Taunt, $"{GargoyleTag} enemy-warning taunt for {enemy.enemyType.enemyName}");
                            // Success gets the full cooldown; the early exit above only holds off
                            // the scan for a moment.
                            lastEnemyTauntTime = Time.time;
                            randEnemyTauntTime = UnityEngine.Random.Range(minTaunt, maxTaunt);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Resolves a clip the sender already picked by exact name.
        ///
        /// This used to be a StartsWith prefix match, which returned the first prefix hit in list
        /// order. The shipped set contains taunt_priordeath_EnemyForestGiant and
        /// ...EnemyForestGiantEaten, and it only resolved correctly because alphabetical order
        /// happens to put the shorter one first. A custom line named as a prefix of a shipped one
        /// would make a client play a different sentence than the host chose.
        /// ChooseRandomClip still uses StartsWith - it genuinely needs prefix semantics.
        /// </summary>
        public static AudioClip? FindClip(string clipName, List<AudioClip> clips)
        {
            foreach (AudioClip clip in clips)
            {
                // Fully qualified: a bare `using System;` in this file makes `Random` ambiguous
                // against UnityEngine.Random, which is used unqualified in five places.
                if (string.Equals(clip.name, clipName, System.StringComparison.OrdinalIgnoreCase))
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
                LGLog.Debug(LogCat.Taunt, $"{GargoyleTag} {clipType} taunt: {clip.name}");
                RoundManager.Instance.PlayAudibleNoise(base.transform.position, creatureVoice.maxDistance / 3f, creatureVoice.volume);
                creatureVoice.PlayOneShot(clip);
                StartCoroutine(PlayNoiseWhileTalking());
            }
            else
            {
                // THE mod's signature failure. Clips travel by name and are resolved against the
                // receiving client's own list; a miss used to fall off the end of this method in
                // total silence, with the only diagnostic behind [Conditional("DEBUG")]. Every
                // upstream audio bug - a transfer that never completed, a clip over the size cap,
                // a half-decoded stereo file, an unknown clipType - lands here looking identical,
                // and the player just reports "the gargoyle isn't talking".
                LGLog.Warn(LogCat.Audio,
                    $"{GargoyleTag} taunt '{clipName}' (type '{clipType}') did not resolve locally - " +
                    $"this machine has {clipList.Count} clip(s) for that type. " +
                    (clipList.Count == 0
                        ? "The list is EMPTY, which means either the audio transfer for this category never completed or the clipType is not one this build knows."
                        : "The clip is missing from this machine's list, so the audio transfer was incomplete."));
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