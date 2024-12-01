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
        
        private float lastGenTauntTime = 0f;
        private float lastAgrTauntTime = 0f;

        private int lastGenTaunt = -1;
        private int lastAgrTaunt = -1;

        private float timeSinceHittingPlayer;
        private float distanceToPlayer = 0f;
        private float distanceToClosestPlayer = 0f;
        private float idleDistance = 0f;
        private Vector3 lastPosition = Vector3.zero;
        private Vector3 curPosition = Vector3.zero;
        private string? lastEnemy = null;

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
            DoAnimationClientRpc("startWalk");

            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            StartSearch(base.transform.position);

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

            //creatureSFXRun = transform.Find("LethalGargoyleModel").Find("CreatureSFXRun").GetComponent<AudioSource>();

            creatureVoice.maxDistance *= 4;
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;

            closestPlayer = GetClosestPlayer();
            distanceToPlayer = targetPlayer != null ? Vector3.Distance(base.transform.position, targetPlayer.transform.position) : 0f;
            LogIfDebugBuild("TargetPlayer: " + distanceToPlayer);
            distanceToClosestPlayer = closestPlayer != null ? Vector3.Distance(base.transform.position, closestPlayer.transform.position) : 0f;
            LogIfDebugBuild("ClosestPlayer: " + distanceToPlayer);
            LogIfDebugBuild("Aware: " + awareDist);

            if (targetPlayer != null)
            {
                if (!base.isOutside && !targetPlayer.isInsideFactory)
                {
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                }
            }

            bool foundSpot = false;
            bool isSeen = false;
            if (distanceToClosestPlayer <= awareDist) {
                isSeen = GargoyleIsSeen(base.transform);
                if (isSeen && currentBehaviourStateIndex != (int)State.AggressivePursuit)
                {
                    if (targetPlayer != null)
                    {
                        if (distanceToPlayer <= aggroRange)
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
            }
            else if (!isSeen && distanceToPlayer <= idleDistance && currentBehaviourStateIndex != (int)State.AggressivePursuit && currentBehaviourStateIndex != (int)State.SearchingForPlayer)
            {
                LogIfDebugBuild("Not Seen. Switching to idle.");
                SwitchToBehaviourClientRpc((int)State.Idle);
            }

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
                        if (Time.time - lastGenTauntTime >= randGenTauntTime && IsHost)
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
                    SearchForPlayers();
                    if (FoundClosestPlayerInRange())
                    {
                        LogIfDebugBuild("Start Target Player");
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.StealthyPursuit);
                    }
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
                        foundSpot = SetDestinationToHiddenPosition();
                        if (!foundSpot && distanceToPlayer < idleDistance)
                        {
                            SwitchToBehaviourClientRpc((int)State.AggressivePursuit);
                        }
                        else
                        {
                            if (Time.time - lastGenTauntTime >= randGenTauntTime && IsHost)
                            {
                                Taunt();
                            }
                        }
                    }
                    else
                    {
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
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
                            foundSpot = SetDestinationToHiddenPosition();
                            if (foundSpot)
                            {
                                SwitchToBehaviourClientRpc((int)State.GetOutOfSight);
                            }
                        }

                        if (FoundClosestPlayerInRange())
                        {
                            if (Time.time - lastAgrTauntTime >= randAgrTauntTime && IsHost)
                            {
                                OtherTaunt("aggro", ref lastAgrTaunt, ref lastAgrTauntTime, ref randAgrTauntTime);
                            }
                            SetDestinationToPosition(targetPlayer.transform.position);
                            timeSinceHittingPlayer += Time.deltaTime;
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

                        if (Time.time - lastGenTauntTime >= randGenTauntTime && IsHost)
                        {
                            Taunt();
                        }

                        foundSpot = SetDestinationToHiddenPosition();
                        if (!foundSpot && isSeen)
                        {
                            SwitchToBehaviourClientRpc((int)State.AggressivePursuit);
                        } 
                        else if (!foundSpot && closestPlayer != null)
                        {
                            SetDestinationToPosition(targetPlayer.transform.position);
                        } 
                        else if (!isSeen)
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

            lastPosition = curPosition;
            curPosition = transform.position;

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            }

            if ((currentBehaviourStateIndex == (int)State.StealthyPursuit || currentBehaviourStateIndex == (int)State.Idle) && IsHost) {
                EnemyTaunt();
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.StealthyPursuit:
                    if (curPosition == lastPosition)
                    {
                        SwitchToBehaviourClientRpc((int)State.Idle);
                        DoAnimationClientRpc("startIdle");
                    }
                    else
                    {
                        DoAnimationClientRpc("startWalk");
                    }
                    break;
                case (int)State.Idle:
                    DoAnimationClientRpc("startIdle");
                    break;
                case (int)State.SearchingForPlayer:
                    if (curPosition == lastPosition)
                    {
                        DoAnimationClientRpc("startIdle");
                    }
                    else
                    {
                        DoAnimationClientRpc("startWalk");
                    }
                    break;
                case (int)State.AggressivePursuit:
                    if (curPosition == lastPosition)
                    {
                        DoAnimationClientRpc("startIdle");
                    }
                    else
                    {
                        DoAnimationClientRpc("startChase");
                    }
                    break;
                case (int)State.GetOutOfSight:
                    if (curPosition == lastPosition)
                    {
                        DoAnimationClientRpc("startIdle");
                    }
                    else
                    {
                        DoAnimationClientRpc("startWalk");
                    }
                    break;
                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        }

        AudioClip? FindClip(string clipName, List<AudioClip> clips)
        {
            foreach (AudioClip clip in clips)
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

        public void SearchForPlayers()
        {
            //Do Stuff
            if (searchForPlayers != null)
            {
                if (!searchForPlayers.inProgress)
                {
                    StartSearch(base.transform.position, searchForPlayers);
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
                LogIfDebugBuild("Found a hiding spot!");
                return true;
            }
            else
            {
                LogIfDebugBuild("No suitable hiding spot found.");
                return false;
            }
        }

        public void FindCoverPointsAroundTarget(ref List<Transform> coverPoints)
        {
            Bounds playerBounds = new(targetPlayer.transform.position, new Vector3(40, 20, 40));
            Bounds gargoyleBounds = new(transform.position, new Vector3(40, 20, 40));

            for (int i = 0; i < allAINodes.Length; i++)
            {
                if (gargoyleBounds.Contains(allAINodes[i].transform.position) || playerBounds.Contains(allAINodes[i].transform.position))
                {
                    bool isSafe = true;
                    foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
                    {
                        if (playerBounds.Contains(player.transform.position) || gargoyleBounds.Contains(player.transform.position))
                        {
                            if (player.HasLineOfSightToPosition(allAINodes[i].transform.position, 60f, 60, 25f) || PathIsIntersectedByLineOfSight(allAINodes[i].transform.position, false, true))
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
                }else if (distance < bestDistance2 && distance >= aggroRange + 1f)
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

        bool GargoyleIsSeen(Transform t)
        {
            bool isSeen = false;
            bool partSeen = false;

            Vector3[] gargoylePoints = [
                t.position + Vector3.up * 0.25f, // bottom
                t.position + Vector3.up * 1.3f, // top
                t.position + Vector3.left * 1.6f, // Left shoulder
                t.position + Vector3.right * 1.6f, // Right shoulder
                // Add more points as needed
            ];

            for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
            {
                PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[i];
                float distance = Vector3.Distance(player.transform.position, t.position);

                if (distance <= awareDist)
                {
                    if ((!base.isOutside && player.isInsideFactory) || (base.isOutside))
                    {
                        foreach (Vector3 point in gargoylePoints)
                        {
                            if (player.HasLineOfSightToPosition(point, 68f))
                            {
                                partSeen = true;
                                break;
                            }
                        }

                        if ((PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i]) && partSeen) && PlayerHasHorizontalLOS(StartOfRound.Instance.allPlayerScripts[i]))
                        {
                            isSeen = true;
                        }
                    }
                }
            }
            LogIfDebugBuild("GargoyleSeen: " + isSeen);
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
                timeSinceHittingPlayer += Time.deltaTime;
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
            DoAnimationClientRpc("startIdle");
            DoAnimationClientRpc("swingAttack");
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
                int randomIndex = Random.Range(0, Plugin.deathClips.Count() - 1);
                TauntClientRpc(Plugin.deathClips[randomIndex].name, "death");
            }
        }

        public void Taunt()
        {
            int randInt = Random.Range(1, 100);

            if (randInt < 3)
            {
                OtherTaunt("enemy", ref lastGenTaunt, ref lastGenTauntTime, ref randGenTauntTime);
            }
            else
            {
                OtherTaunt("general", ref lastGenTaunt, ref lastGenTauntTime, ref randGenTauntTime);
            }
        }

        public void OtherTaunt(string clipType, ref int lastTaunt, ref float lastTauntTime, ref float randTime)
        {
            List<AudioClip> clipList = [];

            switch (clipType)
            {
                case "general":
                    clipList = Plugin.tauntClips;
                    break;
                case "enemy":
                    clipList = Plugin.enemyClips;
                    break;
                case "aggro":
                    clipList = Plugin.aggroClips;
                    break;
                case "death":
                    clipList = Plugin.deathClips;
                    break;
            }

            if (clipList.Count != 0)
            {
                if (!GargoyleIsTalking())
                {
                    // Play a random taunt clip
                    int randomIndex;
                    do
                    {
                        randomIndex = Random.Range(0, clipList.Count() - 1);
                    } while (randomIndex == lastTaunt);

                    LogIfDebugBuild(clipType + "Taunt");
                    TauntClientRpc(clipList[randomIndex].name, clipType);
                    lastTauntTime = Time.time;
                    randTime = Random.Range(minTaunt, (int)(maxTaunt));
                }
            }
            else
            {
                LogIfDebugBuild(clipType + " TAUNTS ARE NULL! WHY!?");
                return;
            }
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

        public void EnemyTaunt()
        {
            int randInt = Random.Range(1, 200);
            string? clip = null;

            EnemyAI? enemy;

            if (randInt < 2)
            {
                if (!creatureVoice.isPlaying)
                {
                    enemy = EnemyNearGargoyle();

                    if (enemy != null)
                    {
                        lastEnemy = enemy.enemyType.enemyName;
                        if (enemy.enemyType.enemyName != lastEnemy)
                        {
                            {
                                LogIfDebugBuild(enemy.enemyType.enemyName);
                                switch (enemy.enemyType.enemyName.ToUpper())
                                {
                                    case "BLOB":
                                        clip = "taunt_enemy_Slime1";
                                        break;
                                    case "BUTLER":
                                        clip = "taunt_enemy_Butler1";
                                        break;
                                    case "CENTIPEDE":
                                        clip = "taunt_enemy_Centipede1";
                                        break;
                                    case "GIRL":
                                        clip = "taunt_enemy_GhostGirl1";
                                        break;
                                    case "HOARDINGBUG":
                                        clip = "taunt_enemy_HoardingBug1";
                                        break;
                                    case "JESTER":
                                        clip = "taunt_enemy_Jester1";
                                        break;
                                    case "MANEATER":
                                        clip = "taunt_enemy_Maneater1";
                                        break;
                                    case "MASKED":
                                        clip = "taunt_enemy_Masked1";
                                        break;
                                    case "CRAWLER":
                                        clip = "taunt_enemy_Thumper1";
                                        break;
                                    case "BUNKERSPIDER":
                                        clip = "taunt_enemy_Spider1";
                                        break;
                                    case "SPRING":
                                        clip = "taunt_enemy_SpringHead1";
                                        break;
                                    case "NUTCRACKER":
                                        clip = "taunt_enemy_Nutcracker1";
                                        break;
                                    case "FLOWERMAN":
                                        clip = "taunt_enemy_Bracken1";
                                        break;
                                }

                                if (clip != null)
                                {
                                    TauntClientRpc(clip, "enemy");
                                }
                            }
                        }
                    }
                }
            }
        }

        [ClientRpc]
        private void TauntClientRpc(string clipName, string clipType)
        {
            List<AudioClip> clipList = [];

            switch (clipType)
            {
                case "general":
                    clipList = Plugin.tauntClips;
                    break;
                case "enemy":
                    clipList = Plugin.enemyClips;
                    break;
                case "aggro":
                    clipList = Plugin.aggroClips;
                    break;
                case "death":
                    clipList = Plugin.deathClips;
                    break;
            }
            if (clipList.Count > 0)
            {
                creatureVoice.PlayOneShot(FindClip(clipName, clipList));
            }
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

    }
}