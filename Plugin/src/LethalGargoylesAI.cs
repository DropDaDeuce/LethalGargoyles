using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using GameNetcodeStuff;
using LethalGargoyles.Configuration;
using LethalLib.Modules;
using Unity.Netcode;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TextCore.Text;
using static UnityEngine.GraphicsBuffer;

namespace LethalGargoyles
{

    // You may be wondering, how does the Gargoyle know it is from class LethalGargoyleAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.

    public class LethalGargoylesAI : EnemyAI
    {
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
#pragma warning restore 0649

        PlayerControllerB closestPlayer = null!;

        private float randTime = 0f;
        private float timeSinceHittingPlayer;
        private float lastTauntTime = 0f;
        private int lastTaunt = -1;
        private float distanceToPlayer = 0f;
        private float distanceToClosestPlayer = 0f;
        private float idleDistance = 0f;
        private float baseSpeed = 0f;
        private float attackRange = 0f;
        private int attackDamage = 0;
        private float aggroRange = 0f;
        private int minTaunt = 0;
        private int maxTaunt = 0;

        enum State
        {
            SearchingForPlayer,
            StealthyPursuit,
            GetOutOfSight,
            AggressivePursuit,
            Idle,
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start()
        {
            base.Start();
            LogIfDebugBuild("Gargoyle Spawned");
            creatureAnimator.SetTrigger("startWalk");

            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            // We make the enemy start searching. This will make it start wandering around.
            StartSearch(base.transform.position);
            
            baseSpeed = Plugin.BoundConfig.baseSpeed.Value;
            attackRange = Plugin.BoundConfig.attackRange.Value;
            attackDamage = Plugin.BoundConfig.attackDamage.Value;
            aggroRange = Plugin.BoundConfig.aggroRange.Value;
            idleDistance = Plugin.BoundConfig.idleDistance.Value;
            minTaunt = Plugin.BoundConfig.minTaunt.Value;
            maxTaunt = Plugin.BoundConfig.maxTaunt.Value;

            creatureVoice.maxDistance = creatureVoice.maxDistance * 4;
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;

            bool isSeen = GargoyleIsSeen(base.transform);

            if (isSeen && currentBehaviourStateIndex != (int)State.AggressivePursuit)
            {

                if (targetPlayer != null)
                {

                    if (Vector3.Distance(targetPlayer.transform.position, base.transform.position) <= aggroRange)
                    {
                        LogIfDebugBuild("Is Seen. Switching to aggression");
                        SwitchToBehaviourClientRpc((int)State.AggressivePursuit);
                    }
                    else
                    {
                        LogIfDebugBuild("Is Seen and Not Aggressive");
                        SwitchToBehaviourClientRpc((int)State.GetOutOfSight);
                    }
                }
                else
                {
                    LogIfDebugBuild("Is Seen and Not Aggressive");
                    SwitchToBehaviourClientRpc((int)State.GetOutOfSight);
                }

            }
            else if (!isSeen && distanceToPlayer <= idleDistance && currentBehaviourStateIndex != (int)State.AggressivePursuit && currentBehaviourStateIndex != (int)State.SearchingForPlayer)
            {
                LogIfDebugBuild("Not Seen. Switching to idle.");
                SwitchToBehaviourClientRpc((int)State.Idle);
            }

            if (targetPlayer != null)
            {
                distanceToPlayer = Vector3.Distance(base.transform.position, targetPlayer.transform.position);
            }

            closestPlayer = GetClosestPlayer(true, true, false);
            if (closestPlayer != null)
            {
                distanceToClosestPlayer = Vector3.Distance(base.transform.position, closestPlayer.transform.position);
            }
            timeSinceHittingPlayer += Time.deltaTime;

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Idle:
                    agent.speed = 0f;
                    if (targetPlayer != null)
                    {
                        LookAtTarget(targetPlayer.transform.position);
                        LogIfDebugBuild("Watching and Waiting");
                        if (!targetPlayer.isInsideFactory)
                        {
                            LogIfDebugBuild("Target Player Left The Facility. Switching Targets");
                            StartSearch(transform.position);
                            lastTauntTime = 0f;
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                            return;
                        }
                        if (Time.time - lastTauntTime >= randTime)
                        {
                            Taunt();
                        }
                        if (!isSeen && distanceToPlayer > idleDistance)
                        {
                            SwitchToBehaviourClientRpc((int)State.StealthyPursuit);
                        }
                    }
                    break;
                case (int)State.SearchingForPlayer:
                    agent.speed = baseSpeed * 1.5f;
                    LogIfDebugBuild("Searching For Closest Player");
                    if (FoundClosestPlayerInRange())
                    {
                        LogIfDebugBuild("Start Target Player");
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.StealthyPursuit);
                    }
                    creatureSFX.volume = 1f;
                    break;

                case (int)State.StealthyPursuit:
                    agent.speed = baseSpeed;
                    LogIfDebugBuild("Stealthily follow player.");
                    if (targetPlayer != null)
                    {
                        if (!targetPlayer.isInsideFactory)
                        {
                            LogIfDebugBuild("Target Player Left The Facility. Switching Targets");
                            StartSearch(transform.position);
                            lastTauntTime = 0f;
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                            return;
                        }

                        creatureSFX.volume = 0.5f;
                        if (!SetDestinationToHiddenPosition(targetPlayer.transform.position))
                        {
                            SwitchToBehaviourClientRpc((int)State.AggressivePursuit);
                        }
                        else
                        {
                            if (Time.time - lastTauntTime >= randTime)
                            {
                                Taunt();
                            }
                        }
                    }
                    break;

                case (int)State.AggressivePursuit:
                    agent.speed = baseSpeed * 2.5f;
                    LogIfDebugBuild("Cannot hide, turn to aggression.");
                    if (targetPlayer != null)
                    {
                        if (!targetPlayer.isInsideFactory)
                        {
                            LogIfDebugBuild("Target Player Left The Facility. Switching Targets");
                            StartSearch(transform.position);
                            lastTauntTime = 0f;
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                            return;
                        }
                        creatureSFX.volume = 1.5f;

                        bool canSeePlayer = CanSeePlayer(targetPlayer);
                        if (distanceToPlayer > aggroRange && distanceToClosestPlayer > aggroRange)
                        {
                            if (SetDestinationToHiddenPosition(targetPlayer.transform.position))
                            {
                                SwitchToBehaviourClientRpc((int)State.GetOutOfSight);
                            }
                        }

                        if (FoundClosestPlayerInRange())
                        {
                            SetDestinationToPosition(targetPlayer.transform.position);
                            if (timeSinceHittingPlayer >= 1f && canSeePlayer)
                            {
                                if (distanceToPlayer < attackRange)
                                {
                                    LookAtTarget(targetPlayer.transform.position);
                                    AttackPlayer(targetPlayer);
                                }
                                else if (distanceToClosestPlayer < attackRange && closestPlayer != null)
                                {
                                    targetPlayer = closestPlayer;
                                    LookAtTarget(targetPlayer.transform.position);
                                    AttackPlayer(targetPlayer);
                                }
                                else
                                {
                                    SwitchToBehaviourClientRpc((int)State.GetOutOfSight);
                                }
                            }
                        }
                    }

                    break;

                case (int)State.GetOutOfSight:
                    agent.speed = baseSpeed * 1.5f;
                    if (targetPlayer != null)
                    {
                        LogIfDebugBuild("Gotta find a place to hide!");
                        if (!targetPlayer.isInsideFactory)
                        {
                            if (FoundClosestPlayerInRange())
                            {
                                LogIfDebugBuild("Start Target Player");
                                StopSearch(currentSearch);
                                SwitchToBehaviourClientRpc((int)State.StealthyPursuit);
                            }
                            else
                            {
                                return;
                            }
                        }

                        if (!SetDestinationToHiddenPosition(targetPlayer.transform.position) && isSeen)
                        {
                            SwitchToBehaviourClientRpc((int)State.AggressivePursuit);
                        }
                        else
                        {
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
            };

            if (targetPlayer == null)
            {
                LogIfDebugBuild("Target Player Is Null");
                distanceToPlayer = 0f;
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }
            else
            {
                distanceToPlayer = Vector3.Distance(base.transform.position, targetPlayer.transform.position);
            }
        }

        public void Taunt()
        {
            if (Plugin.tauntClips != null)
            {

                EnemyAI[] enemies = FindObjectsOfType<EnemyAI>();

                foreach (EnemyAI enemy in enemies)
                {
                    if (enemy.enemyType.enemyName == "LethalGargoyle")
                    {
                        if (enemy.creatureVoice.isPlaying)
                        {
                            LogIfDebugBuild("Taunt canceled due to another gargoyle taunting.");
                            lastTauntTime = Time.time;
                            randTime = Random.Range(5, 10);
                            return;
                        }
                    }
                }

                // Play a random taunt clip
                int randomIndex = -1;

                do
                {
                    randomIndex = Random.Range(0, Plugin.tauntClips.Count());
                } while (randomIndex == lastTaunt);

                LogIfDebugBuild("Taunting");
                creatureVoice.PlayOneShot(Plugin.tauntClips[randomIndex]);
                lastTauntTime = Time.time;
                randTime = Random.Range(minTaunt, maxTaunt);
            }
            else
            {
                LogIfDebugBuild("TAUNTS ARE NULL! WHY!?");
                return;
            }
        }

        bool FoundClosestPlayerInRange()
        {
            PlayerControllerB? closestPlayerInsideFactory = null;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
            {
                if (!creatureVoice.isPlaying)
                {

                }
                PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[i];
        
                if (player.isInsideFactory)
                {
                    float distance = Vector3.Distance(base.transform.position, player.transform.position);
                    if (distance < closestDistance)
                    {
                        closestPlayerInsideFactory = player;
                        closestDistance = distance;
                    }
                }
            }

            if (closestPlayerInsideFactory != null)
            {
                targetPlayer = closestPlayerInsideFactory;
                LogIfDebugBuild("Found You!");
                return true;
            }
            else
            {
                LogIfDebugBuild("Where are you?");
                return false;
            }
        }

        bool SetDestinationToHiddenPosition(Vector3 targetPosition)
        {
            Transform? bestCoverPoint = null;
            List<Transform> coverPoints = FindCoverPointsAroundTarget();

            if (coverPoints.Count > 0)
            {
                bestCoverPoint = ChooseBestCoverPoint(coverPoints);
            }

            if (bestCoverPoint != null)
            {
                agent.SetDestination(bestCoverPoint.position);
                LogIfDebugBuild("Found a hiding spot!");
                return true;
            }
            else
            {
                LogIfDebugBuild("No suitable hiding spot found.");
                return false;
            }
        }

        public List<Transform> FindCoverPointsAroundTarget()
        {
            List<Transform> coverPoints = [];

            for (int i = 0; i < allAINodes.Length; i++)
            {
                bool isSafe = true;
                foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
                {
                    if (player.HasLineOfSightToPosition(allAINodes[i].transform.position, 60f, 60, 25f) || PathIsIntersectedByLineOfSight(allAINodes[i].transform.position, false, true))
                    {
                        isSafe = false;
                        break;
                    }
                }

                if (isSafe)
                {
                    coverPoints.Add(allAINodes[i].transform);
                }
            }

            return coverPoints;
        }

        public Transform? ChooseBestCoverPoint(List<Transform> coverPoints)
        {
            if (coverPoints.Count == 0)
            {
                return null;
            }

            Transform bestCoverPoint = coverPoints[0];
            float bestDistance = Vector3.Distance(targetPlayer.transform.position, bestCoverPoint.position);

            foreach (Transform coverPoint in coverPoints)
            {
                float distance = Vector3.Distance(targetPlayer.transform.position, coverPoint.position);
                if (distance < bestDistance  && distance > aggroRange + 1f)
                {
                    bestCoverPoint = coverPoint;
                    bestDistance = distance;
                }
            }

            return bestCoverPoint;
        }

        bool GargoyleIsSeen(Transform t)
        {
            bool isSeen = false;
            bool partSeen = false;

            Vector3[] gargoylePoints = {
                t.position + Vector3.up * 0.25f, // bottom
                t.position + Vector3.up * 1.3f, // top
                t.position + Vector3.left * 1.6f, // Left shoulder
                t.position + Vector3.right * 1.6f, // Right shoulder
                // Add more points as needed
            };

            for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
            {
                PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[i];
                foreach (Vector3 point in gargoylePoints)
                {
                    if (player.HasLineOfSightToPosition(point, 68f)) {
                        partSeen = true;
                        break;
                    }
                }

                if ((PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i]) && partSeen) && PlayerHasHorizontalLOS(StartOfRound.Instance.allPlayerScripts[i]))
                {
                    isSeen = true;
                }
            }
            return isSeen;
        }

        public bool PlayerHasHorizontalLOS(PlayerControllerB player)
        {
            Vector3 to = base.transform.position - player.transform.position;
            to.y = 0f;
            return Vector3.Angle(player.transform.forward, to) < 68f;
        }

        public bool CanSeePlayer(PlayerControllerB player, float width = 45f, int range = 60, int proximityAwareness = -1)
        {
                Vector3 position = player.gameplayCamera.transform.position;
                if (Vector3.Distance(position, eye.position) < (float)range && !Physics.Linecast(eye.position, position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    Vector3 to = position - eye.position;
                    if (Vector3.Angle(eye.forward, to) < width || (proximityAwareness != -1 && Vector3.Distance(eye.position, position) < (float)proximityAwareness))
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
                LogIfDebugBuild("Gargoyle Collision with Player!");
                if (timeSinceHittingPlayer >= 1f)
                {
                    AttackPlayer(playerControllerB);
                }
            }
        }

        public void AttackPlayer(PlayerControllerB player)
        {
            LogIfDebugBuild("Attack!");
            agent.speed = 0f;
            timeSinceHittingPlayer = 0f;
            player.DamagePlayer(attackDamage, false, true, CauseOfDeath.Bludgeoning);
            GameNetworkManager.Instance.localPlayerController.JumpToFearLevel(1f);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead)
            {
                return;
            }
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

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }
    }
}