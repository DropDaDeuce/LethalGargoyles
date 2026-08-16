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
                //
                // !isPocketed is the fix for "all my statues talk at once". Update() runs on EVERY
                // item in a player's inventory, not just the one in the active slot, so carrying
                // four statues meant four independent dog checks - all at the same position, so
                // all passing together. This was always true; it was HIDDEN because lastDogTaunt
                // used to be static, so whichever statue fired first blocked the rest for
                // dogCooldown (300s by default). Making it per-instance was correct and exposed
                // the real bug underneath. A statue on the floor should still talk; one buried in
                // your pockets should not.
                if (IsServer && !isPocketed && dogHear && Time.time - lastDogTaunt > dogCooldown && Time.time - lastDogCheck > 1f && !scrapAudio.isPlaying)
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
            if (LGLog.On(LogCat.Scrap, LGLevel.Debug))
                LGLog.Debug(LogCat.Scrap,
                    $"Statue#{NetworkObjectId} ItemActivate(used={used}, down={buttonDown}) " +
                    $"pocketed={isPocketed} held={isHeld} " +
                    $"heldBy={(playerHeldBy != null ? playerHeldBy.playerUsername : "null")} " +
                    $"server={IsServer} owner={IsOwner}");

            // IsServer IS REQUIRED HERE. Do not remove it again.
            //
            // Vanilla REPLICATES ItemActivate to every machine - the host runs it for an item held
            // by a client (proven in a log: server=True owner=False heldBy=Player #1). So without
            // this guard EVERY machine forwards the ServerRpc and the server plays one taunt per
            // machine: two players, two voices; four players, four.
            //
            // b8 removed this guard on an audit finding that claimed the statue was mute for
            // non-hosts. That finding was WRONG - it assumed ItemActivate only runs on the
            // activating client. The host's replicated copy is what fires the taunt, and it always
            // did, which is why nobody ever reported the statue being silent.
            if (IsServer && !isPocketed && scrapAudio != null)
            {
                ItemActivateServerRpc(used, buttonDown);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ItemActivateServerRpc(bool used, bool buttonDown)
        {
            LGLog.Debug(LogCat.Scrap, $"Statue#{NetworkObjectId} ItemActivateServerRpc RECEIVED pocketed={isPocketed}");

            // Re-check server-side: RequireOwnership = false means anyone can send this, so the
            // authoritative copy must not take a caller's word for the item's state.
            if (isPocketed) return;
            GeneralTaunt();
        }

        /// <summary>
        /// Server-side: when each player's currently-talking statue will be finished.
        ///
        /// Keyed by client id, so carrying four statues cannot stack four voices - the item that
        /// starts talking holds the floor for that player until its clip ends. Bounded by player
        /// count, so it cannot grow. Statues lying on the floor are keyed separately by object id
        /// (see <see cref="_nextTalkTime"/>) because they are not attached to anyone.
        /// </summary>
        private static readonly Dictionary<ulong, float> s_playerTalkingUntil = [];

        /// <summary>Per-statue floor, so a single statue cannot be spam-clicked into overlapping itself.</summary>
        private float _nextTalkTime = 0f;

        /// <summary>
        /// True when this statue is allowed to start talking, and reserves the slot if so.
        /// Server-only - GeneralTaunt is only ever reached through the ServerRpc.
        ///
        /// This replaces the old <c>!scrapAudio.isPlaying</c> check, which could not do the job:
        /// clips are played with PlayOneShot, and one-shots do not reliably drive isPlaying, so
        /// that guard let taunts overlap.
        /// </summary>
        private bool TryReserveTalkSlot(float clipLength)
        {
            float now = Time.time;
            if (now < _nextTalkTime) return false;

            if (playerHeldBy != null)
            {
                ulong who = playerHeldBy.actualClientId;
                if (s_playerTalkingUntil.TryGetValue(who, out float busyUntil) && now < busyUntil)
                {
                    LGLog.Debug(LogCat.Scrap, $"Statue#{NetworkObjectId} suppressed - {playerHeldBy.playerUsername} already has a statue talking for another {busyUntil - now:0.0}s");
                    return false;
                }
                s_playerTalkingUntil[who] = now + clipLength;
            }

            _nextTalkTime = now + clipLength;
            return true;
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
                AudioClip chosen = clipList[randomIndex];

                // One talking statue per player. Reserve before committing to the clip, so a
                // player holding four of these gets one voice, not four.
                if (!TryReserveTalkSlot(chosen.length))
                    return;

                lastTaunt = randomIndex;
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
                LGLog.Debug(LogCat.Scrap, $"Statue#{NetworkObjectId} PLAYING {clipType}: {clip.name} (audio={scrapAudio.GetInstanceID()})");
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