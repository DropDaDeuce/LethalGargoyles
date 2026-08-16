# Optimization & Improvement Audit — findings and batch plan

**Status: LIVE, PARTIALLY APPLIED.** Produced 2026-08-15 by board batch **b5**, phase 1 of the improve/optimize pass.

> ## APPLIED 2026-08-15 by board batch b8 — read this before acting on anything below
>
> Mathew directed a batch run while away, accepting the risk. **Five commits landed, one per batch, so any single batch reverts in isolation.** Every one is **compile-verified only — 0 warnings, 0 errors in Debug AND Release — and NONE of it has ever been run.**
>
> | Commit | Covers |
> |---|---|
> | `61cc313` | **Batch A** — `LGLog`, crash guards, all statue fixes (incl. C1, E11, E12) |
> | `0a9ff00` | **Batch E (partial)** — E1, E2, E3, E4, E5, E9, E10, D3, G12 |
> | `3f92b72` | **Batch F** — F1, F2, F3, F6, A9, A10 |
> | `a1dac4b` | **Batches C/D/G (partial)** — C2, D1, D2, D4, G3, G4 |
>
> **NOT applied, and deliberately so:**
> - **Batch B, the partial-class split.** Skipped: it would have consumed the session's remaining budget rewriting 2,900 lines for zero behaviour gain, and its entire purpose was to make *later* diffs readable — the later diffs happened first. **It is still worth doing, and it is now a bigger job**, because four commits have landed on top. Do it as its own session.
> - **C3, the push/damage owner-authority fix.** This is the only top-tier finding still open and it stays open on purpose: it is the one marked **PLAUSIBLE**, it rewrites how damage reaches players, and it cannot be verified without a second machine. Shipping an unverified rewrite of the damage path autonomously was a worse bet than leaving a known bug documented.
> - **D5, D6, D7, E6 (already correct), E7, E8, F4, F5, F7, F8, F9, G1, G2, G5–G11, H2, H4–H8.**
>
> **One thing found while fixing E2 that is not in the original audit:** `GetTargetPosition` computed `targetZone` *before* the left/right decision, so once the distances actually differed the "left wins" branch still returned the **right** zone. It was unreachable only because E2 kept both distances equal. Fixing E2 alone would have swapped one bug for another; both went in together.

**How this was produced:** six parallel read-only audits over `Plugin\src\**` — AI hot paths, state machine + the shared static layer, the audio/voice-line subsystem end to end, Harmony patches + soft deps + the entry point, config + scrap + packaging, and a structural map of `LethalGargoylesAI.cs` for the partial-class split. Every finding below was traced in code by the agent that reported it. Where two agents found the same defect independently it is marked **×2** — that is corroboration, and those are the safest bets in the document.

**Tags** — `safe` = provably behaviour-neutral. `watch` = compiles fine, a player can notice; the "what you'd see" is stated. `your call` = a design or tuning decision that is Mathew's, not a session's.
**Confidence** — CONFIRMED = traced in our code. PLAUSIBLE = depends on vanilla behaviour that could not be read from here; each one names a cheap way to settle it.

---

## The headline

**This is not primarily a performance problem. Nine features in the mod do not work, or work only for the host.**

Ranked by how much a player is missing out on today:

| # | Feature | What is actually happening | Confidence |
|---|---|---|---|
| 1 | **Multi-gargoyle target balancing** | Never worked at all. `gargoyleTargets[myID]` is never written on target acquisition, so every count is zero, `ChangeTarget` is unreachable dead code, and **every gargoyle stalks the same player.** The rest of the crew is never stalked or taunted. | CONFIRMED |
| 2 | **Push and push-damage on clients** | Applied server-side to a remote player's copy. Works on the host, evaporates for everyone else. Mathew tests as host, which is why this has never been seen. | PLAUSIBLE — test below |
| 3 | **Circling left vs right** | `CheckZonePath` resets *both* `leftPathDist` and `rightPathDist` to `1000f` at the top of every call, so the `"Right"` call wipes what the `"Left"` call measured. Every comparison downstream decides against a constant. | CONFIRMED |
| 4 | **The Gargoyle Statue, for non-hosts** | `ItemActivate` gates a `[ServerRpc(RequireOwnership = false)]` behind `IsServer`. A client clicks it and nothing happens, with no log line. | CONFIRMED ×2 |
| 5 | **The SteamID taunt rarity** | The 90s cooldown is dead (`lastSteamIDTauntTime` is set once in `Start` and never again) and the rate is **7.5%, not 2.5%** — `randInt >= 160 && randInt < 175` against `Random.Range(1, 200)`. | CONFIRMED |
| 6 | **Door closing behind the gargoyle** | Gated on the static `LGInstance`, which every gargoyle overwrites and nobody nulls. When the last-spawned one dies, **every survivor stops closing doors** for the rest of the round. | CONFIRMED |
| 7 | **Stereo voice lines, on clients** | Decode to exactly half length. You ship one stereo file — `taunt_employeeclass_Scout.ogg` — so that line is cut off mid-sentence for every client while the host hears it whole. | CONFIRMED |
| 8 | **The push, after any round the crew survives** | Static state is never cleared on a normal round end, so a stale `playerPushStates[player][deadGargoyleID] == true` makes that player **permanently un-pushable for the rest of the session.** | CONFIRMED |
| 9 | **`CanSeePlayer`'s range** | Default is `int rangeSqr = 120` compared against `sqrMagnitude` — that's a **10.95 m** sight range, not 120 m. Both per-frame callers use the default. | CONFIRMED |

Two of these (#1, #3) mean tuning work done on top of them was tuning against broken logic. Expect the mod to *feel* different once they're fixed — that's the point, but it is why they land in their own batch with a playtest attached.

---

## Batch plan

Ordered so that observability lands first, the big diff lands second while everything is still simple, and the feel-changing work lands last where you can judge it.

### Step 0 — commit what's already on disk *(Mathew, before any batch)*

`HEAD` is `decb0d8`. Three commits, not two — `icon.png` turns out to be a genuine content change (114,655 → 75,873 bytes), not just the case rename, and there is untracked thumbnail work beside it.

1. **b2** — root `.gitignore` **and `.gitattributes` (both currently UNTRACKED — see F1)**, the `.pyc` removal, the `Icon.png`→`icon.png` case fix, README + `manifest.json` corrections, and the docs/`.claude` scaffold.
2. **b3** — `Plugin\LethalGargoyles.csproj`, `Plugin\src\Enemy\LethalGargoylesAI.cs`, `CHANGELOG.md`. The game-compat fix, attributable alone.
3. **icon** — the new `icon.png` plus `AssetSources\Images\Thumbnails\*` and `thumbnail_build.py`, once you've settled which thumbnail you're using. Yours.

Without this, reverting an optimization also reverts the game-compatibility fix.

---

### Batch A — diagnostic logging, observability and crash guards `safe`

> **A0 — the diagnostic logging layer is designed in [`Diagnostic_Logging.md`](Diagnostic_Logging.md) (b7) and ships as part of this batch.** Runtime-gated levels and per-subsystem categories, switchable from the BepInEx cfg **in a Release build without recompiling**, free when off, plus `Invariant` assertions aimed at exactly the silent-desync bug shape this audit found nine times. **A1 below is simply its first consumer.** Read that doc before implementing this batch.

**Everything here is behaviour-neutral or turns a silent crash into a log line. Nothing changes how the gargoyle plays.** This goes first because it makes every later playtest diagnosable — particularly A1, which converts the mod's worst bug class into a one-glance log read.

| ID | Finding | Where | Tag |
|---|---|---|---|
| **A1** | **A clip name that doesn't resolve on a client is completely silent in Release.** Clips travel by name; `FindClip` misses return `null` and the RPC falls off the end with no `else` and no log. The only diagnostic is `[Conditional("DEBUG")]`. Every other audio failure funnels into this one no-op. Add a `LogWarning` naming the clip, the type, and the local list count. | `LethalGargoylesAI.TauntClientRpc`, `GargoyleStatue.TauntClientRpc` | `safe` |
| **A2** | **`randomIndex++` can index one past the end.** `if (randomIndex == lastTaunt) randomIndex++;` with no wrap. Reachable at `1/Count²` per call — 25% on the 2-clip `Combat Dialog\Hit` pool. With one clip enabled it is guaranteed. Throws out of `HandleBehaviorState` and aborts the rest of that AI tick. Fix: `randomIndex = (randomIndex + 1) % clipList.Count;` | `LethalGargoylesAI.OtherTaunt`, `GargoyleStatue.GeneralTaunt` | `safe` ×2 |
| **A3** | **`KillEnemy` throws before its own cleanup.** `deathClips[Random.Range(0, Count)]` with no count guard (empty on a client that missed the transfer), unguarded RPC send after `base.KillEnemy` already despawned the NetworkObject, and `transform.GetComponent<Collider>()` can return null. All three land *before* `StopCoroutine(searchCoroutine)` and `activeGargoyles.Remove(this)`. Guard the send, and move the cleanup above it. | `LethalGargoylesAI.KillEnemy` | `safe` ×2 |
| **A4** | **Eleven `Directory.CreateDirectory` calls sit upstream of `harmony.PatchAll()`.** Ten have no try/catch; the one that does `return`s, which *also* skips `PatchAll` and enemy registration. A permissions failure (OneDrive-synced game folders are the common case) means the gargoyle never spawns, and the log shows a bare `IOException` with nothing tying it to voice-line folders. | `Plugin.Awake` | `safe` |
| **A5** | **`InitializeNetworkBehaviours()` is entirely unguarded** — `GetTypes()` can throw `ReflectionTypeLoadException`, and `method.Invoke` re-throws from *any* initializer in the assembly, not just the `CoronerClass` it deliberately skips. Aborts `Awake` at the first real step. | `Plugin.InitializeNetworkBehaviours` | `safe` |
| **A6** | **Four `LoadAsset` results dereferenced without a null check.** `LoadAsset` returns null rather than throwing on a missing/renamed asset — the exact stale-bundle failure class `CLAUDE.md` already documents. **`NetworkObjectManager.Init`'s version throws inside a postfix on `GameNetworkManager.Start`**, and Harmony does not isolate postfixes from each other, so it can stop *other mods'* patches on that method from running. | `Plugin.Awake`, `NetworkObjectManager.Init` / `LoadClipsHostPostFix` | `safe` |
| **A7** | **`ClearUnusedEntries`' reflection hack has no null guard.** `GetProperty("OrphanedEntries", NonPublic\|Instance)` returns null (not throws) if BepInEx renames it; the next line calls `.GetValue` on it → NRE out of `Awake`, skipping scrap registration and the `stepSound` cache. Symptom is "statue never spawns", cause is a config-pruning line. | `PluginConfig.ClearUnusedEntries` | `safe` |
| **A8** | **`FastBufferWriter` has 200 bytes of slack and its capacity guard is dead code.** Budget works out to `category.Length + clipName.Length <= 94`; a descriptively-named custom line (>85 chars — very plausible for `Voicy_...` downloads) overflows and **kills the entire remaining transfer for that client.** The guard `if (writer.Capacity < audioData.Length)` can never be true — capacity is `Length + 200` by construction. It compares total capacity, not remaining space. | `AudioManager.SendAudioClipToClient` | `safe` |
| **A9** | **A missing voice-line folder silences the whole lobby.** `GetFiles("*.*")` throws `DirectoryNotFoundException`; `LoadClipList` probes eleven folders with no try/catch, and the throw escapes `OnNetworkSpawn` *before* `LoadAudioClipsFromConfig()` runs — so the host loads zero clips too. Needs an in-session deletion or an AV quarantine to trigger, but degrades to total silence with only a raw stack trace. | `AudioManager.GetMP3Files` / `OnNetworkSpawn` | `safe` |
| **A10** | **`GetFiles("*.*")` picks up `Thumbs.db`, `.txt`, `.reapeaks`** — each mints a config toggle and an "Unsupported audio file format" error. Change to `"*.ogg"`. | `AudioManager.GetMP3Files` | `safe` |
| **A11** | **`.mp3`/`.wav` are accepted by the sender, rejected by the host loader, and undecodable by the client.** Three different format policies in one pipeline: a player's MP3 gets a host `LogError`, is *still* pushed over the wire, and throws inside NVorbis on every client. Make `AudioFileToByteArray` OGG-only to match the other two. | `AudioManager.AudioFileToByteArray` | `safe` |
| **A12** | **`FindClip` prefix-matches where it should match exactly.** `StartsWith` returns the first prefix hit in list order. `taunt_priordeath_EnemyForestGiant` / `...EnemyForestGiantEaten` exist in the shipped set and resolve correctly **by luck** (alphabetical order puts the shorter first). A custom line named as a prefix of a shipped one makes the client play a different sentence than the host chose. `ChooseRandomClip` keeps `StartsWith` — it needs it. | `LethalGargoylesAI.FindClip` | `safe` |

**Playtest for A:** nothing should change. Host a round, hear the gargoyle taunt normally, kill it, hear the death line. Then check `LogOutput.log` for any *new* `LethalGargoyles` warnings — if A1 starts firing, that is a real pre-existing bug it just exposed, not a regression.

---

### Batch B — the partial-class split `safe`, pure moves

**One commit containing nothing but relocated code. No renames, no logic edits.** It lands here — after A, before everything else — so the enormous diff is behind us while the file is still close to its committed state, and every later diff stays readable.

The structural map recommends **8 files**, and the reasoning holds:

| File | Contents (by banner section) | ~Lines |
|---|---|---|
| `LethalGargoylesAI.cs` | 1 (minus the tracker), 2, 3, 4 — enums, constants, static caches, all fields, lifecycle. Plus `LogIfDebugBuild`/`LogIfSlow`. | ~520 |
| `LethalGargoylesAI.PlayerActivityTracker.cs` | The nested static class alone. Fully self-contained — the cleanest extraction in the file. | ~115 |
| `LethalGargoylesAI.StateMachine.cs` | 5 | ~340 |
| `LethalGargoylesAI.Movement.cs` | **6 + 7 + 9 merged** | ~950 |
| `LethalGargoylesAI.Perception.cs` | 10 | ~300 |
| `LethalGargoylesAI.Targeting.cs` | 8 | ~150 |
| `LethalGargoylesAI.Combat.cs` | 11 | ~135 |
| `LethalGargoylesAI.Audio.cs` | 12 | ~430 |

**Why 6/7/9 merge rather than split three ways:** they call each other bidirectionally — `ChooseClosestNodeToPos` ↔ `SetSmartDestination`, with `ValidateZonePosition` and `CheckForPath` shared between cover-search and zone-walk. Splitting them produces three files that constantly call into each other, which reads *worse* than one merged file.

**Three seam hazards, pre-registered so the move doesn't reintroduce them:**

- **B1** — the `#pragma warning disable/restore 0649` pair must travel as a unit with `turnCompass`/`attackArea`, or "field never assigned" returns and breaks the documented 0-warning state.
- **B2** — **partial classes do not share `using` directives.** `using static ...PlayerActivityTracker;` is consumed only by section 12 and must be copied by hand into `Audio.cs`.
- **B3** — the class name must not change. The netcode patcher keys on the declaring type for `DoAnimationClientRpc` and `TauntClientRpc` (**the only two RPCs in the file — there are zero `[ServerRpc]`s**). Partials are one type after compile, so the split is safe, but this gets verified rather than assumed.

**Verified before the split, worth knowing:** 7 `EnemyAI` overrides (`Start`, `Update`, `DoAIInterval`, `OnDestroy`, `OnCollideWithPlayer`, `HitEnemy`, `KillEnemy`), one `ISmartAI` member (`GoToSmartPathDestination`), no `#region` or `#if DEBUG` block spans a seam, and no method needs cutting in half to fit.

**Incidental:** `turnCompass` and `attackArea` are grep-confirmed **never read or written anywhere in the file.** They're prefab-bound so they aren't safely deletable from here — flagging, not fixing.

**Playtest for B:** the easiest one in the whole pass. Spawn, chase, taunt, teleport, die. Either everything works or nothing does. Confirm `DropDaDeuce.LethalGargoyles_original.dll` appears beside the patched DLL (proof the netcode patcher ran) and that a taunt actually plays on a *second* machine — that exercises both RPCs.

---

### Batch C — netcode authority `watch`, needs two players

Every item here is invisible when you test as host. **This batch cannot be verified single-player.**

| ID | Finding | Tag |
|---|---|---|
| **C1** | **Statue is mute for non-hosts.** `ItemActivate` gates `ItemActivateServerRpc` behind `IsServer` — but that RPC is `RequireOwnership = false` precisely so a non-authoritative client can send it. Drop the `IsServer`; keep the `scrapAudio` check. | `watch` ×2 |
| **C2** | **`TauntClientRpc` sent from non-server clients in four places** — `HitEnemy`, `KillEnemy`, `OnCollideWithPlayer`→`AttackPlayer`, and `GargoyleStatue.Update`. Vanilla `HitEnemy`/`KillEnemy` are driven by ClientRpcs and run on *every* client, so every shovel hit throws an NGO "only the server can invoke a ClientRpc" on every non-host machine. `SetAnim` already has the guard — the taunt sends never got it. | `watch` |
| **C3** | **Push and push-damage never reach clients.** `PushPlayer`/`AttackPlayer` call `DamagePlayer` and set `externalForceAutoFade` on the *server's* copy of a remote `PlayerControllerB`. Damage and movement are owner-authoritative in Lethal Company — that's why vanilla ships `DamagePlayerFromOtherClientServerRpc`. Melee still works for clients only because `OnCollideWithPlayer` runs on the victim's machine. **PLAUSIBLE, and the cheapest test in this document: have a second player stand still, back turned, next to a ledge. If they are never moved, this is it.** | `watch` |
| **C4** | **`AttackPlayer` writes AI state on the colliding client** — `lastAttackTime`, `agent.speed = 0f`, `LookAtTarget`, and `targetPlayer = null` on death. Contained today (the consumers are `IsOwner`-guarded), but it is a gameplay write on a client and turns into a desync the moment someone reads `targetPlayer` earlier in `Update`. | `safe` |

---

### Batch D — lifecycle and the static layer `watch`

**First, a correction to a standing assumption.** Every writer to `gargoyleTargets` / `playerPushStates` / `activeGargoyles` is reachable only from `Update`, `DoAIInterval`, `KillEnemy` and Unity coroutines — **all main thread.** PathfindingLib's workers touch only `SmartPathTask`. There is **no true data race** in the static layer; the `ConcurrentDictionary`s and `PlayerPushStatesLock` are currently decorative. Everything below is a *lifecycle and staleness* bug, not concurrency.

| ID | Finding | Tag |
|---|---|---|
| **D1** | **Nothing clears the static layer when the crew survives.** `ClearAllVariables`' only caller is the `allPlayersDead` branch of `DoAIInterval`; the two round-end hooks in `AIHelperPatches` are **commented out** and there is no `OnNetworkDespawn`. A gargoyle despawned mid-`PushTarget` leaves `playerPushStates[player][ghostID] == true`, and every future gargoyle's `HandlePushTarget` defers by 10s forever — **that player is un-pushable for the rest of the session, on every subsequent moon.** | `watch` |
| **D2** | **`ClearAllVariables` wipes shared caches out from under living gargoyles**, and runs *per tick* while `allPlayersDead`. It clears `activeGargoyles` (survivors start talking over each other), the railing and kill-trigger caches (so the push gate `distToKillTriggerSqr <= 4f` can never pass), and the three node lists — **which can never refill**, because `RefreshNodesIfNull` only refills when `cachedNodes.Any(n => n == null)` and an *empty* list has no null entries. Make it static, call it once from a round-end hook, and fix the refill condition to `Count == 0 \|\| Any(n => n == null)`. | `safe` |
| **D3** | **`LGInstance` gates door closing.** Every gargoyle overwrites the static with itself; nobody nulls it. When the last-spawned one dies, `LGInstance` goes fake-null and **every survivor stops closing doors** — and `DelayDoorClose` still fires the RPC while skipping the animation, so door state and visual disagree. Test `this`, not the static. | `watch` |
| **D4** | **`PlayerActivityTracker.ClearAllPlayerData` is never called** (grep-confirmed across `Plugin\src`). `RemoveActivity` fires for `PickedUpItem` and `InFacility` but **never for `KilledEnemy`** — so "you killed a Bracken" stays valid across moons and across joining a different lobby. Players get taunted about a kill from two moons ago. | `watch` |
| **D5** | **A gargoyle that loses its target while flagged as pusher never clears the flag.** `ResetPushStage` is the only clearer and `HandleTargetPlayer` returns before it when the target is null. Blocks every *other* gargoyle from pushing that player until the first one reacquires. | `safe` |
| **D6** | **`playerPushStates` is written outside its lock in two of three writers.** No live failure (all main thread), but the file reads as if the lock protects "at most one pusher per player" when it protects a third of it. Also: `ResetPushStage` identifies the target by **`playerUsername` string comparison rather than reference** — two crewmates with the same Steam name clear each other's entries. **RULED (b7): lock all three writers**, and compare players by reference rather than `playerUsername`. A lock guarding a third of the writers is worse than none — it implies an invariant it doesn't protect. | ruled |
| **D7** | **`GargoyleIsSeen` counts disconnected players as watchers** — it filters on `isPlayerDead` but not `isPlayerControlled`. A rage-quitter's body left facing the gargoyle pins `isSeen` true forever, which permanently blocks teleporting, forces the `GetOutOfSight` branch, and stops it ever settling into `Idle` to taunt. Also compares the target by username, not reference. **PLAUSIBLE** — depends on whether a disconnected `PlayerControllerB` keeps `isPlayerDead == false`. | `watch` |

---

### Batch E — the features that never worked `watch`, biggest feel change

**This is the batch that changes how the mod plays.** Ship it on its own and give it a full round.

| ID | Finding | Tag |
|---|---|---|
| **E1** | **Target balancing is dead.** `FoundClosestPlayerInRange`'s acquisition branch sets `_lastTarget = targetPlayer` but never `gargoyleTargets[myID]` — and because it *did* set `_lastTarget`, the reconciler in `Update` (`if (_lastTarget != targetPlayer)`) never fires either. So the map stays `null` for every gargoyle's whole life, all counts are zero, `ChangeTarget` is unreachable, and every gargoyle picks the same nearest player. Fix with one setter that writes `targetPlayer`, `_lastTarget` and `gargoyleTargets[myID]` together, used at all five assignment sites. **You will see gargoyles spread across the crew instead of clumping.** | `watch` |
| **E2** | **`CheckZonePath` wipes its own results.** Both `leftPathDist` and `rightPathDist` reset to `1000f` at the top of every call, so `"Right"` clobbers `"Left"`. Move the resets up into `GetTargetPosition` before the pair of calls. Also replace the `string side` parameter and the `RelativeZoneToString(...).Contains("Right")` test with an enum comparison. **The gargoyle will start circling the other way sometimes — that's the feature working.** | `watch` |
| **E3** | **`AggressivePursuit` has no exit when `GetClosestPlayer()` returns null**, because `Update` records a null closest player as **distance 0** — "standing on top of me". `HandleAggressivePursuitState` is wrapped in `if (closestPlayer != null)` so it does nothing; `HandleAggroAndPush`'s three arms all decline; `HandleOutOfAggroRange` is unreachable because dist is 0. The gargoyle stops dead mid-chase, chase animation playing, until the target dies or walks 60m away. Fix: `float.MaxValue` on null, plus an `else { SwitchState(SearchingForPlayer); }`. Trigger is PLAUSIBLE (vanilla `GetClosestPlayer` likely rejects players in an enemy animation or sinking, which our own validity check does not). | `watch` |
| **E4** | **SteamID taunts: cooldown dead, rate 3× the documented figure.** `lastSteamIDTauntTime` is set once in `Start` as `Time.time - 91f` and never again, so the 90s gate is permanently open. Rate is `randInt >= 160 && randInt < 175` over `Random.Range(1, 200)` = **7.5%**, and 16.7% in the `GetRandomTauntIndex` fallback branch. **The documented 2.5% appears to have come from `EnemyTaunt`'s `Random.Range(1,100) < 3`, a different feature — so `CLAUDE.md` and `README.md` are also wrong here** (see F4). | `watch` |
| **E5** | **Activity and SteamID taunts return `true` without advancing the taunt timer.** Every other success path routes through `OtherTaunt`, which sets `lastGenTauntTime`. These two don't, so the gate stays satisfied and `Taunt()` re-runs *every frame* — and its prologue is not cheap (`GetPriorCauseOfDeath` linear scan, `GetValidActivities` allocating a list + `Enum.GetValues` + boxed iteration). Result is bunched, back-to-back voice lines. | `watch` |
| **E6** | **`CanSeePlayer`'s default `rangeSqr = 120` is compared against `sqrMagnitude`** — a **10.95 m** range, not 120 m. Both per-frame callers use the default. If 120 m was intended the constant is `14400`. **RULED (b7): keep 10.95 m.** Value preserved exactly; becomes a named constant with the metres in a comment so it can't be "fixed" to `14400` later. | ruled |
| **E7** | **`FindNearestRailing` has no distance cap.** `minDistanceSqr` starts at `float.MaxValue` and nothing rejects a far result, so any railing anywhere in the level wins and `PushPlayer` takes the railing branch — normalising a direction that may point 150 m across the map. **The kill-trigger branch, the one with the sideways randomisation that actually aims at the pit, is never used on maps that have railings.** | `watch` |
| **E8** | **`HandleAggroAndPush` can re-push every 0.2 s.** That arm calls `PushPlayer` directly with neither the 1s `lastAttackTime` cooldown nor `pushTimer`, and re-runs at `AGGRO_EVAL_INTERVAL`. Wedged in a corner within 2 m, back turned: 2 damage and a fresh 15-unit impulse five times a second with the attack line restarting each time. | `watch` |
| **E9** | **`EnemyNearGargoyle` never skips itself**, so the first candidate at distance 0 is always `this`. Combined with E10's timer bug, the "there's an enemy nearby" warning is largely scanning for itself. | `watch` |
| **E10** | **`EnemyTaunt` scans every spawned enemy every frame.** `lastEnemyTauntTime` is only written inside the 3% success branch, so once the window opens it stays open and the full `SpawnedEnemies` walk plus `GargoyleIsTalking()` runs at frame rate, per gargoyle, until a taunt actually fires. Set the timer on *every* exit. | `watch` |
| **E11** | **The statue permanently pollutes the shared general-taunt pool.** `List<AudioClip> clipList = AudioManager.tauntClips;` is a *reference*, and `clipList.Add(playerClip)` mutates the static list. Every activation appends that player's SteamID line into the global General pool — forever, at rising odds — so the *monster* starts using someone's personal line as a generic insult. And since it then sends `clipType = "general"`, clients don't find it and hear nothing. Copy the list; send `"steamids"`. | `watch` |
| **E12** | **`lastTaunt` and `lastDogTaunt` are `static` on the statue**, so every statue in a level shares one "last clip" index and one dog cooldown. Undocumented, unlike the AI's deliberate static layer. **RULED (b7): not intentional — make them instance fields.** | ruled |

---

### Batch F — audio pipeline `watch`

| ID | Finding | Tag |
|---|---|---|
| **F1** | **Stereo OGGs decode to half length on clients.** `new float[vorbis.TotalSamples]` + `ReadSamples(buf, 0, TotalSamples)` — `TotalSamples` is *per channel*, `ReadSamples`' count is *interleaved values*. Cancels out for mono; halves for stereo. `taunt_employeeclass_Scout.ogg` is your one stereo file (6.49 s → 3.25 s on clients). Fix: `new float[TotalSamples * Channels]`, `AudioClip.Create(..., (int)TotalSamples, Channels, ...)`, `ReadSamples(buf, 0, buf.Length)`. **Also re-export that file as mono — Mathew's step, parked on the board.** | `watch` |
| **F2** | **With Coroner loaded, PriorDeath grows by 61 clips every lobby.** The Coroner files are added in *both* `GetDefaultAudioClipFilePaths` and `LoadClipList`, unconditionally; `LoadClipList` aliases `defaultAudioClipFilePaths` **by reference** and nothing ever clears it. Lobby 1 → 140 entries; lobby 3 → 262. Each duplicate is another AudioClip in host RAM **and another full network push per client** — roughly 14 MB of redundant audio by the third lobby. Delete the block from `LoadClipList` and deep-copy the dictionary. | `watch` |
| **F3** | **The 500 KB cap uses `break`, so one oversized file kills the rest of its category — for clients only.** The host's `LoadAudioClipsFromConfig` has no size check at all, so host and client pools diverge silently. Nothing shipped trips it today (largest is `taunt_general_PajamaWearing.ogg` at 284,502 B = 55.6% of cap), but custom files append to those same lists, and `Taunt - General` has 34 clips to lose. Fix: `continue`, and add the same guard to the host path. | `watch` |
| **F4** | **The transfer's 10-second timeout is dead and the wait is a hot spin.** `Task.Yield()` inside the `while` is never awaited — the `YieldAwaitable` is discarded, so it's a tight loop burning a thread-pool thread at 100%. `cts.CancelAfter(10s)` only applies before the delegate starts; the body never checks the token. And `clientReady` is a plain `Dictionary` read off-thread while the main thread mutates it. **If a client never reaches `fullyLoadedPlayers` (crashed or disconnected mid-load), the loop never exits for the rest of the session, that client receives zero clips, and there is no log line at all.** Replace with a coroutine polling once per frame against a real deadline. | `watch` |
| **F5** | **A client disconnecting mid-transfer throws `KeyNotFoundException` out of an `async void`.** The `break` after `WaitForClientReady` exits only the inner file loop; the outer category loop calls it again and indexes a key `OnClientDisconnectedCallback` already removed. Not `OperationCanceledException`, so the `catch` misses it, and remaining transfer work for other clients is lost. | `watch` |
| **F6** | **`GetMP3Files("EmployeeClass", ...)` is a dead case** — the switch has `"Class"` and `"Coroner"` but no `"EmployeeClass"`, so three call sites silently get `[]`. This is the only reason EmployeeClass escapes F2's duplication. Custom EmployeeClass lines work purely because the `"Class"` case happens to read the same folder. | `safe` |
| **F7** | **Custom voice lines never get a per-clip enable toggle.** `InitializeAudioClipConfigs` runs in `Awake` against the *shipped* folder only; custom files are merged much later in `LoadClipList` at `OnNetworkSpawn`. So every custom line hits the "No config entry found… Loading anyway" branch and can never be turned off. Fix by moving the custom merge into `Awake` before the config pass. **RULED (b7): do it.** Adds new `Enable <clipname>` entries to players' cfg — needs a CHANGELOG note. | ruled |
| **F8** | **Per-clip toggles are keyed by bare filename, ignoring category.** Two clips with the same basename in different categories produce two cfg entries but one dictionary slot. **Zero duplicates in the shipped set today** — latent until a player names a custom line after one in another category. Key on `$"{category}/{clipName}"`. | `your call` |
| **F9** | **The handshake gates on receipt, not decode.** `SetClientReadyServerRpc(true, ...)` is the *first line* of `OnReceivedMessage`, before the payload is read and before `ProcessAudioClip` starts — so "ready" means "bytes arrived" and 170 decodes can pile up behind it. Low priority. (The 5s timeout that "sends anyway" is fine and logs a `LogWarning` — one of the few Release-visible lines in the whole subsystem. Keep it.) | `your call` |

**Late joiners are not at risk** — `OnClientConnectedCallback` is subscribed for the session and Lethal Company only admits joins in orbit, well after `StartOfRound.Awake` spawns the handler. That one was checked and cleared.

---

### Batch G — performance `safe`

Ordered by (frequency × instances × per-call cost). **Everything in this batch is behaviour-neutral except G2, which is marked.**

| ID | Finding | Tag |
|---|---|---|
| **G1** | **`NavMeshPath.corners` allocates a fresh `Vector3[]` on every property read** — it's backed by `CalculateCornersInternal()`. `PathIsIntersectedByLOS` reads it 2–3× per loop iteration across up to 12 corners, ~30 arrays per call; `ChooseClosestNodeToPos` calls it once per AI node, so **one hide evaluation on a ~100-node interior throws away ~3,000 arrays.** Largest GC source in the file by an order of magnitude. Hoist to a local, or use `GetCornersNonAlloc` with a reusable buffer for zero. | `safe` |
| **G2** | **`ChooseClosestNodeToPos` runs a full `CalculatePath` per AI node with no distance pre-filter**, then sorts *after* pathing — so it pays full cost for nodes 300 m away that can never win. 100–200 navmesh solves in one call, per gargoyle. And the throttle doesn't contain it: `ShouldEvaluateHide()` returns `true` unconditionally when the agent has no path / is pending / is invalid — **exactly the state a stuck or re-pathing gargoyle sits in** — so it can run every frame. Pre-sort by squared distance, path only the nearest 8–12, break on first pass. | `watch` — different hiding nodes, so approach routes look different; no state changes |
| **G3** | **`Stopwatch` instrumentation survives into Release.** `LogIfSlow` is `[Conditional]` so the *calls* strip — but `Stopwatch.StartNew()` and the six `float t0 = (float)sw.Elapsed.TotalMilliseconds;` locals are ordinary statements. One heap allocation plus ~7 timer queries per frame per gargoyle in every shipped build. Wrap in `#if DEBUG`. | `safe` |
| **G4** | **The `DoAIInterval` diagnostic block builds ~15 interpolated strings into locals in Release**, boxes a couple dozen values, and calls `agent.remainingDistance` (which forces the agent to walk its corner list) and `pathingTask.GetResult(0)` **purely to format them**. Only the final `LogIfDebugBuild(...)` strips. Move the whole body into a `[Conditional("DEBUG")]` method. | `safe` |
| **G5** | **`GetGargoyleTargetCounts` rebuilds three collections and rescans all spawned enemies every AI interval** (~5 Hz, per gargoyle), and uses `validPlayers.Contains(target)` — an O(n) scan — inside the gargoyle loop. `ChangeTarget` adds `gargoyles.Select(...).OrderBy(...)` into a `new List<int>` — three allocations for a 1–3 element list. Reuse a cleared dictionary; hoist the enemy rescan to one throttled static refresh shared by all gargoyles. | `safe` |
| **G6** | **`FindCoverPointsAroundTarget` allocates a `NavMeshPath` per sample** — up to 120 per rebuild, via a `CheckForPath` overload whose entire body is `NavMeshPath path = new();`. Add one reusable `_scratchPath` field and delete the allocating overload. Also builds two throwaway `List<GameObject>` copies of the cached node lists. | `safe` |
| **G7** | **`GargoyleIsSeen` recomputes three point-independent checks four times per player** (`isPlayerDead`, `PlayerIsFacingGargoyle`, `PlayerHasHorizontalLOS`) — 3 of 4 evaluations are pure waste, ×4 players — allocates a fresh `Vector3[4]` per call, puts the expensive `HasLineOfSightToPosition` raycast *before* the cheap angle test, and never breaks the outer loop once both flags are true. | `safe` |
| **G8** | **`ChooseRandomClip`/`FindClip` allocate two strings per clip in the library, per taunt, per client.** `Object.name` is a native property that mints a new managed string on each read; `.ToLowerInvariant()` mints a second. `FindClip` runs inside `TauntClientRpc`, so this is several hundred allocations per line spoken on every machine. Build a lower-cased lookup dictionary once at load. | `safe` |
| **G9** | **Round-start cost: ~170 concurrent `UnityWebRequest`s and a blocking main-thread spin.** `AudioFileToByteArray` resumes on Unity's `SynchronizationContext` and then does `while (!operation.isDone) { Task.Yield(); }` with the yield discarded — **a guaranteed frame hitch per clip sent per client.** `LoadAudioClipsFromConfig` fires all ~170 loads at once. Use `yield return webRequest.SendWebRequest()` (there's already a correct example in `LoadAudioClip`) and stagger the loads. | `watch` — host stutter on landing should visibly shorten |
| **G10** | **~167 MB of uncompressed PCM per machine** — 992 seconds of 44.1 kHz audio held by both `AudioClip.Create(..., stream: false)` and `DownloadHandlerAudioClip`. Add F2's duplication and the host carries 250 MB+. Per-*session*, not per-round, so it's not a hitch — it's a footprint. `DownloadHandlerAudioClip.compressed = true` would cut it ~15×. **PLAUSIBLE on the exact figure** (Unity's internal storage format isn't guaranteed float32). | `your call` |
| **G11** | Grouped small `safe` items: `Enum.GetValues` allocating in `GetBufferPositions`/`GetValidActivities` (make them `static readonly` arrays) · all six `Handle*State` methods setting `agent.speed`/`angularSpeed`/`stoppingDistance`/`creatureSFX.volume` unconditionally every frame instead of on state transition · dead `List` allocations at the top of `OtherTaunt` and `TauntClientRpc` · `RefreshNodesIfNull`'s LINQ boxing an enumerator · **`PathIsIntersectedByLOS`' entire `if (calculatePathDistance)` block is unreachable** (both call sites pass `false`) · `cachedTargetPosition` computed *before* the `!IsOwner` guard, so every non-owning client pays a transform read per gargoyle per frame. | `safe` |
| **G12** | **`FoundClosestPlayerInRange`/`ChangeTarget` divide by `validPlayers.Count` with no zero guard.** `Mathf.CeilToInt(x / 0)` on floats yields `CeilToInt(Infinity)` — undefined. Reachable whenever every player is dead or across the entrance while a gargoyle lives. | `safe` |

---

## Housekeeping findings

| ID | Finding | Tag |
|---|---|---|
| **H1** | **The root `.gitignore` and `.gitattributes` are UNTRACKED.** `git ls-files --error-unmatch` on both: *did not match any file(s) known to git*. Tracked reality is three `.gitignore` (AssetSources, Plugin, UnityProject) and two `.gitattributes` (Plugin, UnityProject). **`Session_Board.md` rule 12 and `Docs\INDEX.md` both assert otherwise — they are wrong.** This matters: the root `.gitattributes` exists to declare `gargoyleassets` binary, and that file has **no extension**, so with `core.autocrlf=true` git decides by sniffing content. A fresh clone elsewhere has no such protection. Add both in the b2 commit. | fix in step 0 |
| **H2** | ~~Concentus is confirmed dead.~~ **WRONG — CORRECTED 2026-08-15 (b9). Concentus is LOAD-BEARING; do not drop it.** The zero-hit search was accurate and the conclusion drawn from it was not: the package ships four BCL shims beside the codec, and **`NVorbis 0.10.5` (netstandard2.0) requires `System.Memory`, which the game does not ship and this mod does not ship — the only copy on disk is `plugins\qwbarch-Concentus\System.Memory.dll`.** Removing the dependency would leave every client unable to decode any voice line. Full evidence and the optional clean fix (ship `System.Memory.dll` ourselves, then drop Concentus) are in *Rulings* → item 2. **Searching for a dependency by its own name cannot find a transitive one — that is the lesson worth keeping.** | **do not drop** |
| **H3** | **No Harmony patch target has drifted.** All eight vanilla methods verified against the installed `Assembly-CSharp.dll` by reflection — `DoorLock.OnTriggerStay` + field `enemyDoorMeter`, `EnemyAI.HitEnemy`, `PlayerControllerB.GrabObjectServerRpc`, `PlayerControllerB.SetObjectAsNoLongerHeld`, `PlayerControllerB.Update`, `StartOfRound.WritePlayerNotes`, `StartOfRound.Awake`, `GameNetworkManager.Start` — with matching parameter **names**, which matters because Harmony binds postfix args by name. A green build proves nothing here; these resolve at runtime. | verified clean |
| **H4** | **`CLAUDE.md` and `README.md` state the SteamID taunt rate as 2.5%. It is 7.5%** (see E4). Per the repo's own doc-is-the-bug rule, both need correcting — **but `CLAUDE.md` was not claimed by b5**, so this is parked for whichever batch fixes E4. | doc fix |
| **H5** | **`distWarn` silently drives two unrelated mechanics.** Its description says "the distance at which a gargoyle will warn players about enemies", and it does — but `GargoyleStatue.Start` also reads it as the Eyeless Dog's hearing range for the statue. One slider, two behaviours with different tuning needs. **RULED (b7): split into two settings.** The new statue/dog one goes under the existing `Scrap` category; `distWarn` keeps its name and value so existing configs are unaffected. | ruled |
| **H6** | **Release-build log noise:** `StartOfRoundPatch.PostFixWritePlayerNotes` opens with an unconditional `Logger.LogInfo("Getting Causes of Death.")` every round, plus one or two more per dead player. | `your call` |
| **H7** | **Dead branch in `NetworkObjectManager.LoadClipsHostPostFix`** — `if (!IsHost) return;` immediately followed by `if (IsHost \|\| IsServer)`. The second is unreachable-false. Reads as if it covers a dedicated-server case; it doesn't. | `your call` |
| **H8** | **`closestPlayer` and `aggroPlayer` are declared `= null!`** on values the game genuinely returns null for. **This is why E3 got past review** — nothing warned that `closestPlayer` could be null on the very next line. Declare them `PlayerControllerB?` and let nullable analysis point at the real gaps. This is distinct from the legitimate `= null!` Unity-serialized-field idiom, which stays. | `safe` |

---

## Checked and clean

Recorded so a later session doesn't re-audit them.

- **No soft-dep leakage anywhere.** `CoronerClass` keeps its fields typed `object` and does all Coroner work by reflection, so JIT-loading the class never forces the assembly to resolve. `EmployeeClassesClass` never references its assembly at compile time at all. `EnhancedMonstersCompatibilityLayer` does reference its types directly, but its only call site is behind `IsEnhancedMonstersLoaded` — the correct pattern.
- **Zero Harmony prefixes.** Every patch in the mod is a `[HarmonyPostfix]`. No `false`-returning prefix, no compatibility hazard of that class.
- **The seven-place voice-line category table is in sync** across all eleven categories, including both `clipType` switches. No drift today — but still no shared source of truth, so it can drift again.
- **All eleven static clip lists are cleared in `OnNetworkDespawn`**, and the clear list has not drifted from the declaration list. `LogClipCounts` covers all eleven too.
- **The custom voice-line path is correct** — `Plugin.Awake` builds `<game root>\Lethal Gargoyles\Custom Voice Lines` from `Paths.ExecutablePath`, matching the corrected README.
- **No dead or unread config entries.** All sixteen `PluginConfig` fields are read somewhere in `Plugin\src` (grep-verified).
- **The csproj is machine-independent as documented** — `$(ManagedDirectory)` and `$(NuGetPackageRoot)` + `$(NVorbisVersion)`, no absolute paths, no `..\..\..\..` walks.
- **`GetDeathCauses.previousRoundDeaths` is cleared every `WritePlayerNotes`** — no cross-round accumulation. `PlayerInFacilityPatch`'s dictionaries are bounded by player count.
- **`PlayerControllerB.Update`'s patch is throttled** behind a 1-second gate; the unthrottled per-frame cost is one dictionary lookup and a float compare.
- **Sections 6 (movement/smart path/teleport) and 11 (combat/death) are performance-clean.** `SetSmartDestination` is properly gated by `REPATH_INTERVAL` + `DEST_CHANGE_SQR`, `pathingTask` is reused via `??=`, `TryTeleportNearTarget` front-loads its cheap rejections, and `FindNearestKillTrigger` is genuinely well-optimised (struct cache, squared-distance reject before `ClosestPointOnBounds`, stays 2D).
- **No shipped clip is near the 500 KB cap.** 170 files, 10.6 MB, 992 s, all 44.1 kHz. Largest is `taunt_general_PajamaWearing.ogg` at 284,502 B (55.6%).
- **All five states except `AggressivePursuit` have complete exit coverage** — traced end to end.

---

## Rulings — all six settled 2026-08-15 (b7)

1. **`CanSeePlayer` range (E6) → keep 10.95 m.** "I believe 10.95. I guess we'll find out for sure in testing." **Behaviour is preserved exactly** — the value `120` stays, but it stops being a trap: it becomes a named constant `DEFAULT_SIGHT_RANGE_SQR = 120` with the metres stated in a comment, so nobody later "fixes" it to `14400`. Flagged for attention during the batch-E playtest.
2. ~~**Concentus (H2) → drop it.**~~ **REVERSED 2026-08-15 (b9) — DO NOT DROP IT. Concentus is load-bearing.** Mathew spotted it from the Thunderstore listing: the package ships four BCL shims alongside the codec, and **NVorbis needs one of them**. Proof, in order:
   - `NVorbis 0.10.5`'s nuspec declares, for `.NETStandard2.0`: `<dependency id="System.Memory" version="4.5.3" />` — and `CopyNVorbis` copies from `lib/netstandard2.0/`, that exact build.
   - The game ships **no** `System.Memory.dll`. Its `Managed\` folder has 167 assemblies; the only near-match is `System.Numerics.dll`, a different assembly.
   - The mod's own build output contains **only** `NVorbis.dll` and `NVorbis.xml`.
   - **The single copy of `System.Memory.dll` on disk is `BepInEx\plugins\qwbarch-Concentus\System.Memory.dll`.**

   Drop the dependency and NVorbis throws on its first decode, so **every client silently gets no voice lines at all** — indistinguishable from the failure class in A1/F1–F5. `System.ValueTuple`, NVorbis's other declared dependency, is satisfied by Unity's `mscorlib` and is not at risk.

   **Why the audit missed it:** the agent searched the tree for "Concentus", correctly found zero references, and concluded dead. The package's value is the shims it bundles, not the codec it is named after — a search for the dependency's *name* can never find that.

   **The real defect is that this is implicit and undocumented.** The mod depends on a package for a transitive BCL assembly that package does not advertise. If qwbarch ever repackages Concentus without `System.Memory`, the mod breaks with no code change. **The clean fix, if you want one: ship `System.Memory.dll` yourself** with a `CopyNVorbis`-style target — it is already restored into the NuGet cache as an NVorbis dependency — and *then* Concentus can genuinely go. That is a packaging change needing a real install test, so it is a decision, not a chore. **Until then, both manifests stay as they are.**
3. **Custom-line config toggles (F7) → do it.** Custom lines get `Enable <clipname>` entries like shipped ones. Needs a CHANGELOG note since new entries appear in players' cfg files.
4. **`distWarn` (H5) → split into two settings.** The gargoyle's enemy-warning distance and the statue's Eyeless Dog hearing range become separate config entries. The new one goes under the existing `Scrap` category; the old one keeps its name and its value so existing configs are unaffected.
5. **`playerPushStates` locking (D6) → take the lock in all three writers** (session's recommendation, accepted). Rationale: an uncontended lock on the main thread costs ~nothing, and **a lock that guards one third of the writers is worse than no lock** — it makes a reader believe the "at most one pusher per player" invariant is protected when it isn't. Keeping it consistent is self-documenting; deleting it would need a comment to convey the same thing, and comments rot. The `playerUsername` string comparison becomes a reference comparison in the same change.
6. **Statue statics (E12) → make them instance fields.** "Wasn't supposed to be shared." `lastTaunt` and `lastDogTaunt` stop being `static` on `GargoyleStatue`, so two statues in a level no longer share a clip index and a dog cooldown.
