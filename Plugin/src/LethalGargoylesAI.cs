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

        private float randGenTauntTime = 0f;
        private float randAgrTauntTime = 0f;
        private float randSeenTauntTime = 0f;
        private float randHideTauntTime = 0f;
        //private float randEnemyTauntTime = 0f;
        
        private float lastGenTauntTime = 0f;
        private float lastAgrTauntTime = 0f;
        private float lastSeenTauntTime = 0f;
        private float lastHideTauntTime = 0f;
        //private float lastEnemyTauntTime = 0f;

        private int lastGenTaunt = -1;
        private int lastAgrTaunt = -1;
        private int lastSeenTaunt = -1;
        private int lastHideTaunt = -1;
        //private int lastEnemyTaunt = -1;

        private bool wasSeen;    

        private float timeSinceHittingPlayer;
        private float distanceToPlayer = 0f;
        private float distanceToClosestPlayer = 0f;
        private float idleDistance = 0f;

        private float baseSpeed = 0f;
        private float attackRange = 0f;
        private int attackDamage = 0;
        private float aggroRange = 0f;
        private int minTaunt = 0;
        private int maxTaunt = 0;
        private float distWarn = 0f;

        //private AudioSource creatureSFXRun;

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
            distWarn = Plugin.BoundConfig.distWarn.Value;

            //creatureSFXRun = transform.Find("LethalGargoyleModel").Find("CreatureSFXRun").GetComponent<AudioSource>();

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

            /*if (!isSeen)
            {
                if (wasSeen && !creatureVoice.isPlaying)
                {
                    wasSeen = false;
                    OtherTaunt(Plugin.hideClips, "Hide", ref lastHideTaunt, ref lastHideTauntTime, ref randHideTauntTime);
                } 
                else if (Time.time - lastSeenTauntTime >= randSeenTauntTime * 2 && !wasSeen && !creatureVoice.isPlaying)
                {
                    wasSeen = true;
                    OtherTaunt(Plugin.seenClips, "Seen", ref lastSeenTaunt, ref lastSeenTauntTime, ref randSeenTauntTime);
                }
            }*/

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
                            lastGenTauntTime = 0f;
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                            return;
                        }
                        if (Time.time - lastGenTauntTime >= randGenTauntTime)
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
                            lastGenTauntTime = 0f;
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
                            if (Time.time - lastGenTauntTime >= randGenTauntTime)
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
                            lastAgrTauntTime = 0f;
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
                            if (Time.time - lastAgrTauntTime >= randAgrTauntTime)
                            {
                                OtherTaunt(Plugin.aggroClips, "Aggro", ref lastAgrTaunt, ref lastAgrTauntTime, ref randAgrTauntTime);
                            }
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

            if (currentBehaviourStateIndex == (int)State.StealthyPursuit || currentBehaviourStateIndex == (int)State.Idle) {
                EnemyTaunt();
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.StealthyPursuit:
                    creatureAnimator.SetTrigger("startWalk");
                    break;
                case (int)State.Idle:
                    creatureAnimator.SetTrigger("startIdle");
                    break;
                case (int)State.SearchingForPlayer:
                    creatureAnimator.SetTrigger("startWalk");
                    break;
                case (int)State.AggressivePursuit:
                    creatureAnimator.SetTrigger("startChase");
                    break;
                case (int)State.GetOutOfSight:
                    creatureAnimator.SetTrigger("startWalk");
                    break;
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
                            lastGenTauntTime = Time.time;
                            randGenTauntTime = Random.Range(5, 10);
                            return;
                        }
                    }
                }
                int randomIndex;
                // Play a random taunt clip
                do
                {
                    randomIndex = Random.Range(0, Plugin.tauntClips.Count());
                } while (randomIndex == lastGenTaunt);

                LogIfDebugBuild("General Taunt");
                creatureVoice.PlayOneShot(Plugin.tauntClips[randomIndex]);
                lastGenTauntTime = Time.time;
                randGenTauntTime = Random.Range(minTaunt, maxTaunt);
            }
            else
            {
                LogIfDebugBuild("GENERAL TAUNTS ARE NULL! WHY!?");
                return;
            }
        }

        public void OtherTaunt(List<AudioClip> clips, string clipType, ref int lastTaunt, ref float lastTauntTime, ref float randTime)
        {
            if (clips != null)
            {
                if (!creatureVoice.isPlaying)
                {

                    // Play a random taunt clip
                    int randomIndex;
                    do
                    {
                        randomIndex = Random.Range(0, clips.Count());
                    } while (randomIndex == lastTaunt);

                    LogIfDebugBuild(clipType + "Taunt");
                    creatureVoice.PlayOneShot(clips[randomIndex]);
                    lastTauntTime = Time.time;
                    randTime = Random.Range(minTaunt / 2, (int)(maxTaunt / 2));
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
            int randInt = Random.Range(0, 100);
            AudioClip? clip = null;

            EnemyAI? enemy;

            if (randInt < 3)
            {
                if (!creatureVoice.isPlaying)
                {
                    enemy = EnemyNearGargoyle();
                    if (enemy != null)
                    {
                        LogIfDebugBuild(enemy.enemyType.enemyName);
                        switch(enemy.enemyType.enemyName.ToUpper())
                        {
                            case "BLOB":
                                clip = FindEnemyClip("taunt_enemy_Slime1");
                                break;
                            case "BUTLER":
                                clip = FindEnemyClip("taunt_enemy_Butler1");
                                break;
                            case "CENTIPEDE":
                                clip = FindEnemyClip("taunt_enemy_Centipede1");
                                break;
                            case "GIRL":
                                clip = FindEnemyClip("taunt_enemy_GhostGirl1");
                                break;
                            case "HOARDINGBUG":
                                clip = FindEnemyClip("taunt_enemy_HoardingBug1");
                                break;
                            case "JESTER":
                                clip = FindEnemyClip("taunt_enemy_Jester1");
                                break;
                            case "MANEATER":
                                clip = FindEnemyClip("taunt_enemy_Maneater1");
                                break;
                            case "MASKED":
                                clip = FindEnemyClip("taunt_enemy_Masked1");
                                break;
                            case "CRAWLER":
                                clip = FindEnemyClip("taunt_enemy_Thumper1");
                                break;
                            case "BUNKERSPIDER":
                                clip = FindEnemyClip("taunt_enemy_Spider1");
                                break;
                            case "SPRING":
                                clip = FindEnemyClip("taunt_enemy_SpringHead1");
                                break;
                            case "NUTCRACKER":
                                clip = FindEnemyClip("taunt_enemy_Nutcracker1");
                                break;
                            case "FLOWERMAN":
                                clip = FindEnemyClip("taunt_enemy_Bracken1");
                                break;
                        }

                        if (clip != null)
                        {
                            creatureVoice.PlayOneShot(clip);
                        }
                    }
                }
            }
        }

        AudioClip? FindEnemyClip(string clipName)
        {
            foreach (AudioClip clip in Plugin.enemyClips)
            {
                if (clip.name == clipName)
                {
                    return clip;
                }
            }
            return null;
        }

        EnemyAI? EnemyNearGargoyle()
        {
            if (targetPlayer != null)
            {
                EnemyAI[] enemies = FindObjectsOfType<EnemyAI>();

                foreach (EnemyAI enemy in enemies)
                {
                    float distance = Vector3.Distance(enemy.transform.position, base.transform.position);
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
            creatureAnimator.SetTrigger("swingAttack");
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

        public override void KillEnemy(bool destroy = false)
        {
            base.KillEnemy(destroy);
            Collider col = transform.GetComponent<Collider>();
            col.enabled = false;
            foreach (Collider collider in GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }
            int randomIndex = Random.Range(0, Plugin.deathClips.Count());
            creatureVoice.PlayOneShot(Plugin.deathClips[randomIndex]);
        }
    }
}