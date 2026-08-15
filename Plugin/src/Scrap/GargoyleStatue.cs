using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using LethalGargoyles.src.Utility;
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

        // Instance, not static. These were static, so every statue in a level shared one "last
        // clip played" index and one dog-taunt cooldown - activating statue A silenced statue B
        // for reasons unrelated to B. Unlike the AI's deliberate cross-instance layer, that
        // sharing was never intended.
        private int lastTaunt = 0;
        private float dogHearDistSqr = 0f;
        private float dogCooldown = 0f;
        private bool dogHear = false;
        private float lastDogCheck = 0f;
        private float lastDogTaunt = 0f;

        public override void Start()
        {
            base.Start();
            scrapAudio = base.GetComponentInParent<AudioSource>();
            // Own setting now. This used to read General > Enemy Distance Warning, which is the
            // gargoyle's enemy-warning range - a completely different mechanic that happened to
            // share one slider.
            dogHearDistSqr = Plugin.BoundConfig.dogHearDist.Value;
            dogCooldown = Plugin.BoundConfig.dogCooldown.Value;
            dogHear = Plugin.BoundConfig.dogHear.Value;
            dogHearDistSqr *= dogHearDistSqr;
            lastDogTaunt =  Time.time - dogCooldown;
        }

        public override void Update()
        {
            base.Update();
            if (scrapAudio != null)
            {
                // GrabbableObject.Update runs on every machine, so this must not fire a ClientRpc
                // from a client - NGO throws "only the server can invoke a ClientRpc" and the
                // taunt is lost. The dog-detection decision is a gameplay decision and belongs on
                // the server anyway.
                if (IsServer && dogHear && Time.time - lastDogTaunt > dogCooldown && Time.time - lastDogCheck > 1f && !scrapAudio.isPlaying)
                {
                    lastDogCheck = Time.time;
                    if (DogNearStatue())
                    {
                        if (Enemy.LethalGargoylesAI.ChooseRandomClip("taunt_enemy_Mouthdog", "Enemy", out string? clip))
                        {
                            if (clip != null && NetworkObject != null && NetworkObject.IsSpawned)
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
                if (distanceSqr > dogHearDistSqr)
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
            // NO IsServer GUARD HERE. ItemActivate runs on the client holding the item, and
            // ItemActivateServerRpc is RequireOwnership = false precisely so that client can ask
            // the server to act. Gating the SEND on IsServer meant only the host ever sent it, so
            // any other player clicking the statue got nothing at all, with no log line.
            // (The "guard every RPC send with IsServer" rule is for ClientRpcs, not ServerRpcs.)
            if (scrapAudio != null && !scrapAudio.isPlaying)
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
            // COPY the shared list. This used to alias AudioManager.tauntClips and then Add() to
            // it, which permanently appended the holder's personal SteamID line into the global
            // general-taunt pool - so the MONSTER started using someone's personal line as a
            // generic insult, forever, at rising odds. And it then sent clipType "general", which
            // no client could resolve the SteamID clip under, so they heard nothing at all.
            List<AudioClip> clipList = new(Utility.AudioManager.tauntClips);
            AudioClip? steamIdClip = null;
            if (playerHeldBy != null)
            {
                steamIdClip = Enemy.LethalGargoylesAI.FindClip($"{playerHeldBy.playerSteamId}", Utility.AudioManager.playerClips);
                if (steamIdClip != null)
                {
                    // Still just one candidate among the general lines, exactly as before - the
                    // only change is that it goes into the local copy instead of the shared list.
                    clipList.Add(steamIdClip);
                }
            }

            if (clipList.Count > 0)
            {
                // Play a random taunt clip
                int randomIndex = UnityEngine.Random.Range(0, clipList.Count);
                if (randomIndex == lastTaunt)
                {
                    // Wrap, don't increment - see the same fix in LethalGargoylesAI.OtherTaunt.
                    randomIndex = (randomIndex + 1) % clipList.Count;
                }
                lastTaunt = randomIndex;
                AudioClip chosen = clipList[randomIndex];
                // Send the type the receiving client will actually resolve it under. The SteamID
                // clip lives in playerClips, not tauntClips, so announcing it as "general" meant
                // every client failed to find it and played nothing.
                string clipType = (steamIdClip != null && chosen == steamIdClip) ? "steamids" : "general";
                TauntClientRpc(chosen.name, clipType);
            }
            else
            {
                LGLog.Warn(LogCat.Scrap, "Gargoyle Statue was activated but the general taunt list is empty - nothing to play.");
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
                LGLog.Debug(LogCat.Scrap, $"Statue {clipType} taunt: {clip.name}");
                scrapAudio.PlayOneShot(clip);
                if (dogHear) StartCoroutine(PlayNoiseWhileTalking());
            }
            else if (scrapAudio == null)
            {
                LGLog.Warn(LogCat.Scrap, "Gargoyle Statue has no AudioSource - it cannot play anything. The prefab is likely missing its AudioSource component.");
            }
            else
            {
                LGLog.Warn(LogCat.Audio,
                    $"Statue taunt '{clipName}' (type '{clipType}') did not resolve locally - this machine has {clipList.Count} clip(s) for that type. " +
                    "The audio transfer for that category was incomplete.");
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