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

        enum State
        {
            SearchingForPlayer,
            StealthyPursuit,
            GetOutOfSight,
            AggressivePursuit,
            Idle,
            PushTarget,
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

            if (pushStage < 1 || matPlayerIsOn != "Catwalk")
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
                    else if (distanceToClosestPlayer <= attackRange && !isSeen && closestPlayer != null && currentBehaviourStateIndex != (int)State.AggressivePursuit)
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

                    if (!targetSeesGargoyle && targetPlayer != null && currentBehaviourStateIndex != (int)State.AggressivePursuit && Time.time > pushTimer)
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
                    foundSpot = SetDestinationToHiddenPosition(false);
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
                    foundSpot = SetDestinationToHiddenPosition(false);
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
                    agent.autoBraking = false;
                    if (targetPlayer != null)
                    {
                        canSeePlayer = CanSeePlayer(targetPlayer);
                        LogIfDebugBuild($"Distance To Player: {distanceToPlayer} | Target Sees Gargoyle: {targetSeesGargoyle} | Can See Player: {canSeePlayer} | Push Stage: {pushStage} | Push Timer: {pushTimer}");

                        if (pushStage < 1)
                        {
                            if (distanceToPlayer <= aggroRange * 1.5 && !targetSeesGargoyle && canSeePlayer)
                            {
                                pushStage = 1;
                                foundSpot = SetDestinationToPosition(targetPlayer.transform.position);
                            }
                            else
                            {
                                foundSpot = SetDestinationToHiddenPosition(true);
                            }

                            if (!foundSpot)
                            {
                                SetDestinationToPosition(targetPlayer.transform.position);
                                LogIfDebugBuild("Can't Find Spot");
                            }
                        }
                        else
                        {
                            SetDestinationToPosition(targetPlayer.transform.position);
                        }

                        if (distanceToPlayer <= attackRange && !targetSeesGargoyle)
                        {
                            PushPlayer(targetPlayer);
                            pushStage = 0;
                            pushTimer = Time.time + 45f;
                            SwitchToBehaviourClientRpc((int)State.StealthyPursuit);
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
                    if (!moveTowardsDestination)
                    {
                        DoAnimationClientRpc("startIdle");
                    }
                    else if (moveTowardsDestination)
                    {
                        DoAnimationClientRpc("startWalk");
                    }
                    break;
                case (int)State.Idle:
                    DoAnimationClientRpc("startIdle");
                    break;
                case (int)State.AggressivePursuit:
                    if (!moveTowardsDestination)
                    {
                        DoAnimationClientRpc("startIdle");
                    }
                    else if (moveTowardsDestination)
                    {
                        DoAnimationClientRpc("startChase");
                    }
                    break;
                case (int)State.GetOutOfSight:
                    DoAnimationClientRpc("startWalk");
                    break;
                case (int)State.PushTarget:
                    DoAnimationClientRpc("startChase");
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

        bool SetDestinationToHiddenPosition(bool tryToPush)
        {
            Transform? bestCoverPoint = null;
            List<Transform> coverPoints = [];
            float dotProduct = 0f;

            FindCoverPointsAroundTarget(ref coverPoints, tryToPush, ref dotProduct);

            if (coverPoints.Count > 0)
            {
                bestCoverPoint = ChooseBestCoverPoint(coverPoints, tryToPush, ref dotProduct);
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

        public void FindCoverPointsAroundTarget(ref List<Transform> coverPoints, bool tryToPush, ref float dotProduct)
        {
            for (int i = 0; i < allAINodes.Length; i++)
            {
                Vector3 pos = allAINodes[i].transform.position;

                if (!tryToPush) 
                {
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
                else
                {
                    Bounds playerBounds = new(targetPlayer.transform.position, new Vector3(60, 20, 60));
                    if (playerBounds.Contains(pos))
                    {
                        float num = Vector3.Distance(targetPlayer.transform.position, pos);
                        int range = 60;
                        float width = 60f;
                        if (!(num < (float)range && (Vector3.Angle(targetPlayer.playerEye.transform.forward, pos - targetPlayer.gameplayCamera.transform.position) < width)) && !PathIsIntersectedByLineOfSight(pos,false,true))
                        {
                            coverPoints.Add(allAINodes[i].transform);
                        }
                    }
                }
            }
            return;
        }

        public Transform? ChooseBestCoverPoint(List<Transform> coverPoints, bool tryToPush, ref float dotProduct)
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
                if (!tryToPush)
                {
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
                else
                {
                    if (distance < bestDistance && dotProduct < -0.8f) // Prioritize behind and then distance
                    {
                        bestCoverPoint = coverPoint;
                        bestDistance = distance;
                    }
                }
            }

            if (bestCoverPoint == coverPoints[0] && !tryToPush)
            {
                bestCoverPoint = bestCoverPoint2;
            }

            return bestCoverPoint;
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
            Rigidbody playerRigidbody = player.GetComponent<Rigidbody>();
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