using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Netcode;
using UnityEngine;

namespace LethalGargoyles.src.Scrap
{
    internal class GargoyleStatue : GrabbableObject
    {
        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        AudioSource? scrapAudio;

        private static int lastTaunt = 0;
        private float distWarnSqr = 0f;
        private float dogCooldown = 0f;
        private bool dogHear = false;
        private float lastDogCheck = 0f;
        private static float lastDogTaunt = 0f;

        public override void Start()
        {
            base.Start();
            scrapAudio = base.GetComponentInParent<AudioSource>();
            distWarnSqr = Plugin.BoundConfig.distWarn.Value;
            dogCooldown = Plugin.BoundConfig.dogCooldown.Value;
            dogHear = Plugin.BoundConfig.dogHear.Value;
            distWarnSqr *= distWarnSqr;
            lastDogTaunt =  Time.time - dogCooldown;
        }

        public override void Update()
        {
            base.Update();
            if (scrapAudio != null)
            {
                if (dogHear && Time.time - lastDogTaunt > dogCooldown && Time.time - lastDogCheck > 1f && !scrapAudio.isPlaying)
                {
                    lastDogCheck = Time.time;
                    if (DogNearStatue())
                    {
                        if (Enemy.LethalGargoylesAI.ChooseRandomClip("taunt_enemy_Mouthdog", "Enemy", out string? clip))
                        {
                            if (clip != null)
                            {
                                lastDogTaunt = Time.time;
                                TauntClientRpc(clip, "enemy");
                            }
                        }
                    }
                }
            }
        }

        bool DogNearStatue()
        {
            foreach (EnemyAI enemy in RoundManager.Instance.SpawnedEnemies)
            {
                float distanceSqr = (enemy.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr > distWarnSqr)
                    continue;

                // Only care about Eyeless Dog
                if (string.Equals(enemy.enemyType.enemyName, "MouthDog", StringComparison.OrdinalIgnoreCase))
                {
                    LogIfDebugBuild("MOUTHDOG near statue");
                    return true;
                }
            }

            return false;
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);
            if (IsServer && scrapAudio != null && !scrapAudio.isPlaying)
            {
                // Call the server RPC to handle the interaction
                ItemActivateServerRpc(used, buttonDown);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ItemActivateServerRpc(bool used, bool buttonDown)
        {
            GeneralTaunt();
        }

        public void GeneralTaunt()
        {
            List<AudioClip> clipList = Utility.AudioManager.tauntClips;
            if (playerHeldBy != null)
            {
                AudioClip? playerClip = Enemy.LethalGargoylesAI.FindClip($"{playerHeldBy.playerSteamId}", Utility.AudioManager.playerClips);
                if (playerClip != null)
                {
                    clipList.Add(playerClip);
                }
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
                TauntClientRpc(clipList[randomIndex].name, "general");
            }
            else
            {
                LogIfDebugBuild("General TAUNTS ARE NULL! WHY!?");
                return;
            }
        }

        [ClientRpc]
        private void TauntClientRpc(string clipName, string clipType)
        {
            List<AudioClip> clipList = [];

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

            AudioClip? clip = Enemy.LethalGargoylesAI.FindClip(clipName, clipList);

            if (clipList.Count > 0 && clip != null && scrapAudio != null)
            {
                LogIfDebugBuild(clipType + " taunt: " + clip.name);
                scrapAudio.PlayOneShot(clip);
                if (dogHear) StartCoroutine(PlayNoiseWhileTalking());
            }
        }

        //This is so the MouthDog can "hear" the Gargoyle
        IEnumerator PlayNoiseWhileTalking()
        {
            while (scrapAudio != null && scrapAudio.isPlaying)
            {
                RoundManager.Instance.PlayAudibleNoise(transform.position, scrapAudio.maxDistance / 1.2f, scrapAudio.volume);
                yield return new WaitForSeconds(3f); // Adjust the interval as needed
            }
        }
    }
}