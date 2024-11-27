using System.Collections;
using System.Diagnostics;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

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
        System.Random enemyRandom = null!;
        bool isDeadAnimationDone;
        enum State
        {
            SearchingForPlayer,
            ChasingPlayer,
            PlayTaunt,
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
            StartSearch(transform.position);
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead)
            {
                // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
                if (!isDeadAnimationDone)
                return;
            }

            var state = currentBehaviourStateIndex;
            if (targetPlayer == null) return;
        }

        public override void DoAIInterval()
        {

            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    agent.speed = 2f;
                    if (FoundClosestPlayerInRange(25f, 3f))
                    {
                        LogIfDebugBuild("Start Target Player");
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                    }
                    break;

                case (int)State.ChasingPlayer:
                    agent.speed = 2f * 2f;
                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (!TargetClosestPlayerInAnyCase() || Vector3.Distance(transform.position, targetPlayer.transform.position) > 20 && !CheckLineOfSightForPosition(targetPlayer.transform.position))
                    {
                        LogIfDebugBuild("Stop Target Player");
                        StartSearch(transform.position);
                        SwitchToBehaviourServerRpc((int)State.SearchingForPlayer);
                        return;
                    }
                    SetDestinationToPosition(targetPlayer.transform.position);
                    SwitchToBehaviourServerRpc((int)State.SearchingForPlayer);
                    break;

                case (int)State.PlayTaunt:
                    agent.speed = 0f;
                    //todo add random play sound
                    break;

                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        }

        public void PlayTaunt()
        {
            //Play Random Clip
            DoAnimationClientRpc("startWalk");
            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
        }

        bool FoundClosestPlayerInRange(float range, float senseRange)
        {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if (targetPlayer == null)
            {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }

        bool TargetClosestPlayerInAnyCase()
        {
            mostOptimalDistance = 2000f;
            targetPlayer = null;
            for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
            {
                tempDist = Vector3.Distance(transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
                if (tempDist < mostOptimalDistance)
                {
                    mostOptimalDistance = tempDist;
                    targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
                }
            }
            if (targetPlayer == null) return false;
            return true;
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null)
            {
                LogIfDebugBuild("Gargoyle Collision with Player!");
                SwitchToBehaviourClientRpc((int)State.PlayTaunt);
            }
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

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }
    }
}