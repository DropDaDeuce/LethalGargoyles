# Pathfinding Audit — how the Gargoyle navigates, and what is wrong with it

**Batch b10, 2026-08-16.** Audited read-only; **b11 then applied N1, N2 and N4 the same day** — see *Status* below for exactly what shipped and what did not.
Companion to [`Optimization_Audit.md`](Optimization_Audit.md), which covered all of `Plugin\src\**`. This one goes deep on one subsystem: navigation.

**Everything here was measured, not inferred.** PathfindingLib 2.4.1 and the game's `Assembly-CSharp.dll` (v73) were both decompiled with `ilspycmd` and the relevant method bodies read. Where a claim rests on a decompiled body, the body is quoted. Scratch copies of `EnemyAI.cs` and `DoorLock.cs` were left in the session scratchpad; they are disposable, re-generate them with the `game-update` skill's recipe if needed.

---

## Status (updated 2026-08-16, board b11)

**Applied and compile-verified in Debug AND Release, 0 warnings / 0 errors. NOT ONE LINE HAS BEEN RUN.**

| Finding | State | Note |
|---|---|---|
| **P1** search restarts every frame | **APPLIED** | Guard on `currentSearch.inProgress` in `SearchForPlayers`. |
| **P2/P3/P14/P15/P16** prefab values | **APPLIED by Mathew in the Editor, and `gargoyleassets` rebuilt.** | `AIIntervalTime: 0.1`, `updatePositionThreshold: 1`, `syncMovementSpeed: 0.22`, `m_AutoBraking: 1`, `m_ObstacleAvoidanceType: 2`. Verified in the prefab YAML, and the bundle's mtime is newer than the prefab's. **`m_Radius` deliberately left at `1.25`** — it was a "try it" suggestion, not a defect. |
| **P4** link flags | **APPLIED, behind config, DEFAULT OFF.** | Mathew's ruling. `Pathfinding > Follow Players Through Exits` and `Pathfinding > Use Mineshaft Elevator`. Resolved once in `Start` into `_allowedLinks`. |
| **P6** silently-dropped path request | **APPLIED** | Now returns on `!pathingTask.IsComplete` instead of committing bookkeeping for a call that would no-op. |
| **P7** `SetDestination` while pending | **APPLIED** | `pathPending` moved out of the "needs a path" set. |
| **P8** `GoToSmartPathDestination` | **APPLIED** | Stores the destination and drives the agent directly; no longer re-enters the pathfinder or discards `destination.Type`. |
| **P10** no re-path after a link | **APPLIED** | New `InvalidatePathAfterLink`. **Elevator deliberately excluded** — a RIDE destination requires staying in the car. |
| **P11** `NavMeshPath` in `CheckZonePath`'s loop | **APPLIED** | New `_zoneScratchPath`. |
| **P13** smart agent never re-registers | **NOT APPLIED** | Deferred with N2's remainder. Cheap insurance, but no evidence the game ever disables an enemy GameObject mid-round, and this batch was already large. |
| **P5, P9, P12** | **NOT APPLIED** | Batches N5/N6. These are the ones that need the multi-destination API and a real restructure. |

**Second thing found while applying, and it would have silently undone the whole batch:** `Start()` called vanilla `StartSearch(transform.position)` immediately after `SwitchState(State.SearchingForPlayer)`. That was harmless while `SearchForPlayers` ran unguarded — the smart search simply overwrote it on the first frame. **The moment P1's `inProgress` guard went in, it inverted:** vanilla `StartSearch` sets `currentSearch.inProgress = true` right there in `Start`, so the guard would see a search already running and `StartSmartSearch` would **never be called at all**, for the entire round. The Gargoyle would have fallen back to vanilla roaming with no error, no log line, and worse pathing than v0.7.0 shipped with — a "fix" that quietly disabled PathfindingLib's roaming entirely. The vanilla call is now removed and `HandleSearchingForPlayerState` starts the smart search on the first tick. **`currentSearch` is a serialized field, so it is non-null with `inProgress` false at spawn** — verified in the prefab YAML, and vanilla `StopSearch` null-guards anyway.

**Found while applying P10 and NOT in the original audit:** the entrance-teleport arm had to flip `isOutside`, and the naive `isOutside = !isOutside` would have been wrong. Vanilla's setter is `SetEnemyOutside(bool)`, which **also calls `GetAINodes()`** to repopulate `allAINodes` for the region just entered. `isOutside` gates node selection, cover search and every same-region player check in the file, so a raw flag flip would have left the Gargoyle hunting players it could never reach, on the wrong node set. `SetEnemyOutside` is what shipped.

**Noted, not fixed, do not lose:** `cachedAllAINodes` is a **static** cache fed from `allAINodes`, which is a **per-instance** field that `GetAINodes()` replaces. With several Gargoyles alive on opposite sides of an entrance, that static holds whichever instance filled it last. It only feeds the *fallback* path (used when the region-specific list is empty), and the inside/outside caches come from `RoundManager` and are region-independent, so it is not currently a live defect — but it becomes one the moment the fallback matters. Pre-existing; predates this batch.

---

## The one-paragraph answer

**No — PathfindingLib is being used for roughly the least valuable thing it does, and the expensive part of the AI still runs the old way.** The library's headline feature is that it can solve *many* paths at once, *off the main thread*, and tell you the *real walking distance* to each one. The Gargoyle uses it to solve exactly one path at a time, and everywhere it actually needs to compare candidates — picking a hiding spot, picking a cover point, deciding whether to circle left or right — it falls back to Unity's synchronous `CalculatePath`, on the main thread, up to 120 times in a single call. On top of that, three capabilities are switched off by a hardcoded constant: the Gargoyle can never path through a fire exit, never through the main entrance, and never use the mineshaft elevator. And the search/roam state calls the library in a way that destroys its own progress every single frame.

---

## Headline table

Ordered by how much they cost you. **"Where" tells you whose job the fix is.**

| # | What | Severity | Where |
|---|---|---|---|
| **P1** | Roaming restarts itself every frame — the Gargoyle cannot search | **Breaks a feature + biggest per-frame cost in the mod** | C# |
| **P2** | `AIIntervalTime = 0` on the prefab — the AI tick has no throttle at all | **Multiplies every other cost by ~12×** | Unity |
| **P3** | `updatePositionThreshold = 0` — a position RPC to every client, every frame | **Network flood, host-invisible** | Unity |
| **P4** | Fire exits, main entrance and elevators are switched off by a hardcoded flag | Feature never existed | C# |
| **P5** | The three "compare N candidates" searches all use synchronous `CalculatePath` | The remaining stutter | C# |
| **P6** | `SetSmartDestination` can silently believe it requested a path it never requested | Stale destination, intermittent | C# |
| **P7** | `agent.SetDestination` re-issued every frame while a path is still pending | Agent can fail to ever get a path | C# |
| **P8** | `GoToSmartPathDestination` throws away the destination *type* it was handed | Latent; bites the moment P4 is fixed | C# |
| **P9** | `CheckZonePath`'s "path length" is not a path length | Left/right circling still decided by noise | C# |
| **P10** | Link activation does not re-request a path — the Gargoyle would loop a fire exit | Latent; bites the moment P4 is fixed | C# |
| **P11** | `NavMeshPath` allocated inside `CheckZonePath`'s loop | GC churn; b8 fixed the twin and missed this one | C# |
| **P12** | Teleport-near-target warps to a point it never proved it can walk out of | Rare hard stuck | C# |
| **P13** | Smart agent unregisters on disable and never re-registers | Silent permanent loss of smart pathing | C# |
| **P14** | `syncMovementSpeed = 0` — clients snap instead of interpolating | Visual only, but couples to P3 | Unity |
| **P15** | Obstacle avoidance at max quality, agent radius 1.25 | Tuning | Unity |
| **P16** | `autoBraking` off with a 0.1–0.3 stopping distance | Overshoot / jitter at cover points | Unity |

**Verified clean, recorded so nobody re-checks:** door opening works — the prefab's `openDoorSpeedMultiplier: 0` is *overwritten* in `EnemyAI.Start()` by `enemyType.doorSpeedMultiplier`, which is `10` on `LethalGargoyle.asset` (very fast). `moveTowardsDestination: 0` and `movingTowardsTargetPlayer: 0` on the prefab are *correct* — they disable vanilla's own destination pump, which is exactly right for an AI that drives `agent.SetDestination` itself.

---

## P1 — Roaming restarts itself every frame

**Plain version:** while the Gargoyle is in its "look for a player" state, it tells PathfindingLib "start searching" once per frame. Each of those calls throws away the search that was already running and starts a brand-new one. A search takes many frames to pick even its first destination, so it never gets there. The Gargoyle in `SearchingForPlayer` doesn't roam — it waits for a player to wander into its awareness bubble.

**The chain**, all verified first-hand:

1. `Update()` calls `HandleBehaviorState()` — **per frame**, not per AI interval ([LethalGargoylesAI.cs:563](../../Plugin/src/Enemy/LethalGargoylesAI.cs)).
2. `HandleBehaviorState()`'s switch calls `HandleSearchingForPlayerState()`.
3. That method's last line is `SearchForPlayers()`.
4. `SearchForPlayers()` is `this.StartSmartSearch(transform.position, allowedLinks)`.

Two arguments binds to PathfindingLib's generic overload, `newSearch` defaulting to `null`. Its decompiled body starts:

```csharp
if (newSearch == null)
    newSearch = new AISearchRoutine();      // fresh allocation, every call
enemy.StopSearch(enemy.currentSearch, true); // kills the running search, clear: TRUE
...
enemy.searchCoroutine = StartCoroutine(enemy.CurrentSmartSearchCoroutine(config, traversalFunction));
enemy.currentSearch.inProgress = true;
```

And vanilla `StopSearch(search, clear: true)` is not cheap:

```csharp
StopCoroutine(searchCoroutine);
StopCoroutine(chooseTargetNodeCoroutine);
...
RoundManager.Instance.GetOutsideAINodes();
search.unsearchedNodes = allAINodes.ToList();   // full List copy of every AI node in the level
```

So every frame, per Gargoyle, the mod: allocates an `AISearchRoutine`, stops two coroutines, runs a `RoundManager` node refresh, copies the entire AI-node list into a fresh `List`, and starts two coroutines that will be killed 16 milliseconds later. `CurrentSmartSearchCoroutine` opens with `yield return null` and then waits on `WaitUntil(() => choseTargetNode)` — it is structurally incapable of completing inside one frame.

**This was never caught by b8's profiling** because the stutter hunt profiled `HandleStealthyPursuitState`, which needs a target. `SearchingForPlayer` is the state with *no* target, so it never showed up.

**Fix:** call it once on entering the state, not every tick. The state-enter/state-tick split doesn't exist in `HandleBehaviorState` today, so the smallest honest fix is a guard on `currentSearch.inProgress`:

```csharp
void SearchForPlayers()
{
    if (currentSearch != null && currentSearch.inProgress) return;
    this.StartSmartSearch(transform.position, SmartPathfindingLinkFlags.InternalTeleports);
}
```

Cleaner is to move it into `SwitchState`'s `SearchingForPlayer` arm. Either way, `StopSearch(currentSearch)` on leaving the state is already there ([:748](../../Plugin/src/Enemy/LethalGargoylesAI.cs)) and stays.

**In game you will see:** the Gargoyle actually patrols instead of loitering near where it spawned, and it will find players it previously never found. Expect it to feel *much* more present. That is the intended behaviour returning, not a new one.

---

## P2 — `AIIntervalTime = 0` on the prefab

**Plain version:** vanilla enemies think five times a second. The Gargoyle thinks every frame — about sixty times a second. Every cost in this document is being paid twelve times more often than the game intends.

`LethalGargoyleObj.prefab` line 1218 is `AIIntervalTime: 0`. `EnemyAI`'s declared default is `0.2f`. Vanilla `Update()`:

```csharp
if (updateDestinationInterval >= 0f)
    updateDestinationInterval -= Time.deltaTime;
else {
    DoAIInterval();
    updateDestinationInterval = AIIntervalTime + UnityEngine.Random.Range(-0.015f, 0.015f);
}
```

With `AIIntervalTime` at 0 the reset value is at most `0.015`, which one 60 fps frame (`0.0167`) already exceeds. **`DoAIInterval` runs every frame.**

This is Mathew's to change in the Unity Editor, and it needs a `gargoyleassets` rebuild. **Do not set it to `0.2` in one jump** — the mod's own throttles (`REPATH_INTERVAL` 0.75s, `HIDE_EVAL_INTERVAL` 0.35s, `AGGRO_EVAL_INTERVAL` 0.20s) were all tuned against a tick that fires every frame, and some per-frame work lives in `Update` while some lives in `DoAIInterval`. **Suggested first step: `0.1`**, then playtest, then `0.2` if it still feels responsive.

---

## P3 — `updatePositionThreshold = 0`

**Plain version:** the host tells every other player where the Gargoyle is, every single frame, instead of only when it has actually moved a meter. With three Gargoyles that is around 180 network messages a second for something the game budgets a handful for.

Prefab line 1233 is `updatePositionThreshold: 0`; `EnemyAI`'s default is `1f`. Vanilla `SyncPositionToClients()`, which vanilla `DoAIInterval()` calls unconditionally on its last line:

```csharp
if (Vector3.Distance(serverPosition, base.transform.position) > updatePositionThreshold)
{
    serverPosition = base.transform.position;
    UpdateEnemyPositionRpc(serverPosition);
}
```

A threshold of `0` means "any movement at all". Combined with P2 (`DoAIInterval` every frame) that is one position RPC per frame per Gargoyle to every client.

**You cannot see this as host.** It is the exact shape of the audio bugs in `Optimization_Audit.md` batch F: fine on your machine, degrading for everyone else. This is a strong candidate for any "the Gargoyle is laggy/rubber-bandy for my friends" report.

**P3 and P14 must move together.** `syncMovementSpeed: 0` (prefab line 1239) feeds vanilla's client-side smoothing:

```csharp
base.transform.position = Vector3.SmoothDamp(base.transform.position, serverPosition, ref tempVelocity, syncMovementSpeed);
```

A smooth time of `0` makes `SmoothDamp` snap instantly. Right now the 60 Hz update rate is what hides the snapping. **Raise the threshold without also giving `syncMovementSpeed` a real value and clients will see the Gargoyle teleport in one-meter steps.** Vanilla defaults are `updatePositionThreshold = 1`, `syncMovementSpeed = 0.22`. Start there.

---

## P4 — Three quarters of the smart-link system is switched off

`SmartPathfindingLinkFlags` is `[Flags]` with exactly four members:

```csharp
InternalTeleports = 1,   Elevators = 2,   MainEntrance = 4,   FireExits = 8
```

There is **no `All` member**; the combined value would be `15`. The mod hardcodes `InternalTeleports` in both places it names the flags — `SearchForPlayers` and `GetAllowedLinks()`. Consequences:

* **The Gargoyle can never path between inside and outside.** Not through the main entrance, not through a fire exit. Its only inside↔outside movement is `TryTeleportNearTarget`'s cheat-warp.
* **The mineshaft elevator is unusable.** PathfindingLib ships a `MineshaftElevatorAdapter` for the vanilla elevator specifically so `Elevators` works out of the box. On mineshaft interiors the Gargoyle cannot change floors.
* **`InternalTeleports` is the one flag that does nothing in vanilla.** It exists for *other mods* to register portals via `RegisterInternalTeleport`. Base-game, the list is empty. So the mod has enabled the only flag with nothing behind it and disabled the three with content behind them.
* **The `EntranceTeleport` arm of `FollowSmartPath` is unreachable dead code.** This matters beyond tidiness: b3 spent a session repairing that arm against the game's removal of `EntranceTeleport.exitPoint`, and **board Pending step 5 — "play a round and watch the Gargoyle use an entrance" — cannot pass as written.** Nothing is wrong with b3's fix; it simply cannot execute until this flag changes. Fix P4 and P10 first, then that playtest becomes meaningful.

**Fix:** one constant, used by both sites.

```csharp
private const SmartPathfindingLinkFlags ALLOWED_LINKS =
    SmartPathfindingLinkFlags.InternalTeleports |
    SmartPathfindingLinkFlags.MainEntrance |
    SmartPathfindingLinkFlags.FireExits |
    SmartPathfindingLinkFlags.Elevators;
```

**Do not ship this without P10.** On its own it introduces a fire-exit loop.

**Design call for Mathew, not a bug:** *should* the Gargoyle chase you outdoors and out the fire exit? It already targets outside players and `LethalGargoyle.asset` has `isOutsideEnemy: 0`. Widening the flags makes it genuinely able to follow you out of the facility. That is a gameplay decision, not a defect — say which you want.

---

## P5 — The three expensive searches ignore the library entirely

This is the "am I using it to its full potential" question, answered concretely. `SmartPathTask` has three `StartPathTask` overloads:

```csharp
void StartPathTask(NavMeshAgent agent, Vector3 origin, Vector3 destination,        SmartPathfindingLinkFlags allowedLinks);
void StartPathTask(NavMeshAgent agent, Vector3 origin, Vector3[] destinations,     SmartPathfindingLinkFlags allowedLinks);
void StartPathTask(NavMeshAgent agent, Vector3 origin, List<Vector3> destinations, SmartPathfindingLinkFlags allowedLinks);
```

plus `IsResultReady(i)`, `PathSucceeded(i)`, `GetResult(i)` and **`GetPathLength(i)`** — the real walked distance, per destination, computed on a worker thread. **The mod only ever calls the single-`Vector3` overload, and never calls `GetPathLength` at all.**

Meanwhile, the three places that genuinely need "which of these N is nearest by path" all do it synchronously on the main thread:

| Method | Main-thread solves per call | Ceiling |
|---|---|---|
| `ChooseClosestNodeToPos` → `PathIsIntersectedByLOS` | `agent.CalculatePath` per candidate | 24 (`MAX_PATH_CHECKS`) |
| `FindCoverPointsAroundTarget` → `CheckForPath` + `PathIsIntersectedByLOS` | two solves per sample | ~120 (`40 × 3`) |
| `CheckZonePath` (×2, left and right) | `NavMesh.CalculatePath` per zone hop | 16 |

Every one of those is the exact shape the array/List overload was written for. Batch G already did the cheap half of this — pre-sorting by squared distance so only the nearest 24 get pathed, which is what killed the measured 13.6 ms average. The remaining half is moving those 24 off the main thread and getting a *true* distance instead of a straight-line one as the ranking key.

**Worth being clear about the tradeoff:** `SmartPathTask` is asynchronous. Results arrive a frame or more later, so this is not a drop-in substitution — it means keeping a task alive across frames and acting on last frame's answer, the way `SetSmartDestination`/`FollowSmartPath` already do for the single-destination case. That is a real restructure of `SetDestinationToHiddenPosition`, and it is the *last* thing to do in this list, not the first. **The prize is worth it though:** ranking cover points by true path distance instead of straight-line distance is what stops the Gargoyle picking a spot that is 8 m away through a wall and 40 m away on foot.

There is a second, cheaper win available: `PathIsIntersectedByLOS` is doing two different jobs — "is there a path" and "is the path hidden from the player". Only the first can move to `SmartPathTask`; the `Physics.Linecast` corner walk has to stay. Splitting them means the reachability filter can run async over all candidates and only the survivors pay for line-of-sight raycasts.

---

## P6 — A path request that was silently never made

`StartPathTask`'s decompiled body opens with a guard that is easy to miss:

```csharp
if (jobData == null || IsComplete)   // <- if a job is still running, the whole call is a no-op
{
    ...
    StartJob();
}
```

**Calling it while a job is in flight does nothing at all — no exception, no restart, no leak, no signal.** The mod's `SetSmartDestination` does not check:

```csharp
_lastRequestedDest = destination;
_nextPathRequestTime = Time.time + REPATH_INTERVAL;
pathingTask ??= new SmartPathTask();
pathingTask.StartPathTask(agent, agent.GetPathOrigin(), destination, GetAllowedLinks());
```

It records the new destination and arms the 0.75 s cooldown **before** finding out whether the call did anything. When the in-flight job outlives the cooldown, the mod believes it has asked for a path to the new position while the task is still solving the old one, and `FollowSmartPath` keeps driving to the stale result — for at least another 0.75 s, and longer if it keeps happening.

**Fix:** only commit the bookkeeping when the call can actually start.

```csharp
if (pathingTask != null && pathingTask.IsStarted && !pathingTask.IsComplete)
    return;                       // job in flight; StartPathTask would be a no-op anyway

_lastRequestedDest = destination;
_nextPathRequestTime = Time.time + REPATH_INTERVAL;
pathingTask ??= new SmartPathTask();
pathingTask.StartPathTask(agent, agent.GetPathOrigin(), destination, ALLOWED_LINKS);
```

---

## P7 — `SetDestination` re-issued while the path is still pending

```csharp
bool agentNeedsPath = !agent.hasPath || agent.pathPending || agent.pathStatus == NavMeshPathStatus.PathInvalid;
...
if (destChanged || agentNeedsPath) { agent.SetDestination(destPos); _lastActiveDestination = destPos; }
```

`agent.pathPending` means *"a path to this destination is already being computed"*. Treating that as "needs a path" makes `FollowSmartPath` re-issue `SetDestination` every frame for as long as the calculation takes, and each `SetDestination` re-queues the request. Under load the agent can spend a long stretch never resolving a path — during which `agent.hasPath` is false, so the animation drops to Idle and `ShouldEvaluateHide()`'s escape hatch fires at its accelerated floor, making things worse.

`!agent.hasPath` has a milder version of the same problem: once the agent *arrives*, `hasPath` goes false and it re-issues the same destination forever.

**Fix:** `pathPending` belongs in the "leave it alone" set, not the "needs a path" set.

```csharp
if (agent.pathPending) { /* already working on it */ }
else if (destChanged || !agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid)
{
    agent.SetDestination(destPos);
    _lastActiveDestination = destPos;
}
```

---

## P8 — `GoToSmartPathDestination` discards the answer it was given

This is the `ISmartAI` callback — PathfindingLib's way of saying *"go here next"*. All four arms do the same thing:

```csharp
case SmartDestinationType.DirectToDestination:
case SmartDestinationType.InternalTeleport:
case SmartDestinationType.EntranceTeleport:
case SmartDestinationType.Elevator:
    SetSmartDestination(destination.Position);
```

Two problems. First, `SetSmartDestination` **starts a whole new `SmartPathTask` to a position the pathfinder has already solved** — a redundant solve, and a second `SmartPathTask` racing the search's own. Second and worse, `destination.Type` is thrown away, so the caller loses the one piece of information the callback existed to deliver: whether this waypoint is a place to *walk to* or a link to *activate*.

Today this is masked — P1 means the search never reaches this callback, and P4 means the only reachable type is `DirectToDestination`. Fix either and it stops being masked.

**Fix:** store the destination, drive the agent to it directly, activate on arrival. The activation logic already exists in `FollowSmartPath`; it should be shared, not duplicated.

```csharp
public void GoToSmartPathDestination(in SmartPathDestination destination)
{
    activeDestination = destination;
    _lastActiveDestination = destination.Position;
    agent.SetDestination(destination.Position);
}
```

---

## P9 — `CheckZonePath` is not measuring a path length

The circling logic accumulates `pathDist` like this:

```csharp
pathDist += path.corners.Length > 1
    ? (from - path.corners[1]).sqrMagnitude
    : 0f;
```

That is the **squared** straight-line distance from the zone position to the path's **second corner** — the first leg only. Every corner after it is ignored, and squaring makes the numbers non-additive. The value then decides the tie-break at the top of `GetTargetPosition`:

```csharp
else goRight = rightPathDist <= leftPathDist;
```

**b8's E2 fix was real and necessary but only half the story.** E2 stopped the second probe from wiping the first one's measurement. The measurement itself is still meaningless, so left/right is still being decided by noise — it is just *different* noise now.

Two more things wrong in the same method:

* **`NavMeshPath path = new()` sits inside the loop** ([:2157](../../Plugin/src/Enemy/LethalGargoylesAI.cs)) — up to 8 per probe, 16 per evaluation. `NavMeshPath` wraps native memory. **b8's G6 added `_scratchPath` and converted the other call site but missed this one**; the three-argument `CheckForPath(from, to, path)` overload is right there and takes the buffer. (Listed separately as **P11**.)
* **The `nextZone == Front` early-return is asymmetric.** Walking the ring from a front-ish zone, one direction reaches `Front` on its very first hop and returns `false` immediately, while the other direction has the whole ring to work with. So from `FrontRight`, "left" can never win — regardless of geometry.

**Fix:** real length is `Vector3.Distance` summed over consecutive corners — or, better, delete the hand-rolled probe entirely and use `GetPathLength(i)` from a two-destination `SmartPathTask` (left zone, right zone). That is the smallest, most contained place to introduce the multi-destination API, which makes it a good first exercise before attempting P5.

---

## P10 — Activating a link does not re-path

`FollowSmartPath` warps the agent through a teleport, and then leaves every piece of pathing state pointing at the teleport it just used:

```csharp
case SmartDestinationType.InternalTeleport:
    agent.Warp(dest.InternalTeleport.Destination.position);
    break;
```

`_lastActiveDestination` still holds the teleport mouth, `pathingTask` still holds the old result, `activeDestination` is unchanged. Next frame `agent.hasPath` is false after the warp, so `agentNeedsPath` is true, so `FollowSmartPath` calls `agent.SetDestination` on **the teleport it just came out of** — and walks back in. `GetResult` is documented to return intermediate link waypoints before the final goal, so this is the normal shape of a multi-hop path, not an edge case.

This has never fired because `InternalTeleports` has nothing behind it in vanilla. **Enable `FireExits` without fixing this and the Gargoyle will ping-pong through the fire exit forever.**

**Fix:** after any successful activation, invalidate and immediately re-request.

```csharp
pathingTask?.Dispose();
pathingTask = null;
activeDestination = null;
_lastActiveDestination = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
```

`TryTeleportNearTarget` already does exactly this after its warp — copy that block. Note the Elevator arm needs different treatment: `CanActivateDestination` returns `IsInsideElevator(agentPosition)` for a *ride* destination, so the Gargoyle has to be standing in the car; it should not re-path until the ride finishes.

Also worth noting: the two teleport arms gate on a hand-rolled `1f + agent.stoppingDistance` radius while the elevator arm calls `dest.CanActivateDestination(transform.position)`. `CanActivateDestination` returns `true` unconditionally for both teleport types, so it is not a substitute — but the inconsistency is worth a comment so the next reader doesn't "fix" it wrongly.

---

## P12 — Warping to a point that may be unreachable

`TryFindTeleportPointNearTarget` picks a random ring position, calls `NavMesh.SamplePosition(candidate, out hit, 2.5f, NavMesh.AllAreas)`, checks no player can see it, and warps there.

**`SamplePosition` only proves a point is *on* the navmesh. It says nothing about whether that patch of navmesh connects to anywhere the Gargoyle needs to go.** The Gargoyle can be dropped on a disconnected island — a sealed room, a ledge, the wrong side of an inside/outside boundary — and then it is stuck for the rest of the round, because nothing in the state machine detects "my destination is unreachable" and re-teleports out.

The cost of this check is also non-trivial: up to 10 attempts, each running a `SamplePosition` plus a `HasLineOfSightToPosition` per living player in the same region.

**Fix, and it is the single cleanest demonstration of the multi-destination API:** collect all 10 candidates first, hand the whole list to one `SmartPathTask`, and pick the nearest candidate that both `PathSucceeded(i)` and passes the visibility test. One off-thread job replaces ten main-thread samples, and reachability is proven rather than assumed. Careful: **`GetPathLength(i)` throws `InvalidOperationException` when the path failed** — always gate it behind `PathSucceeded(i)`.

---

## P13 — The smart agent unregisters and never comes back

```csharp
private void OnDisable() { CleanupSmartPathing(); }   // -> SmartPathfinding.UnregisterSmartAgent(agent); _smartRegistered = false;
```

`RegisterSmartAgent` is called exactly once, in `Start()`. There is no `OnEnable`. Any path that disables and re-enables the GameObject leaves `_smartRegistered` false forever, and the guard in `CleanupSmartPathing` means it will never re-register. From that point the Gargoyle's `SmartPathTask` calls operate on an unregistered agent for the rest of the round — silently, with no log line.

Whether the game ever actually disables an enemy GameObject mid-round is not proven here; it is cheap insurance either way.

**Fix:** add the mirror.

```csharp
private void OnEnable()
{
    if (_smartRegistered || agent == null) return;
    SmartPathfinding.RegisterSmartAgent(agent);
    _smartRegistered = true;
}
```

---

## P14–P16 — Unity-side settings (Mathew, in the Editor)

All on `LethalGargoyleObj.prefab`. **All of these need a `gargoyleassets` rebuild to reach the game** — a C#-only build will not pick them up.

| Setting | Current | Vanilla default | Why |
|---|---|---|---|
| `AIIntervalTime` | `0` | `0.2` | **P2.** Try `0.1` first, not `0.2`. |
| `updatePositionThreshold` | `0` | `1` | **P3.** Change together with the next row. |
| `syncMovementSpeed` | `0` | `0.22` | **P14.** Without this, raising the threshold makes clients see snapping. |
| `m_ObstacleAvoidanceType` | `4` (High) | — | **P15.** Highest-cost local avoidance, paid per Gargoyle per frame; up to 3 are alive (`MaxCount: 3`). Try `2` (Medium) or `3` (Good). |
| `m_Radius` | `1.25` | — | **P15.** Large for a humanoid-type agent (`m_AgentTypeID: 0`). It does not change the baked navmesh, but it does make the agent's own avoidance shove it around in narrow corridors. A plausible contributor to "gets stuck in doorways". |
| `m_AutoBraking` | `0` (off) | — | **P16.** With `stoppingDistance` between 0.1 and 0.3 the agent does not slow down as it arrives, so it overshoots and oscillates around cover points. Turning it on is usually the fix for a creature that jitters when it stops. |

---

## Minor / noted, not worth a batch of their own

* **`LookAtTarget` fights the agent's own steering.** It writes `transform.rotation` directly, every frame, from `HandleAggressivePursuitState` — while the NavMeshAgent is also rotating the transform toward its steering direction. The Gargoyle ends up facing you rather than facing where it is walking, which may well be the intent (it is creepy), but it makes every `agent.angularSpeed` value in the state machine dead tuning.
* **`FollowSmartPath` runs twice per frame in any pursuit state** — once from `Update` ([:574](../../Plugin/src/Enemy/LethalGargoylesAI.cs)) and once from inside `SetSmartDestination`. Harmless, but it doubles the `GetResult(0)` traffic.
* **`GetBufferPositions` calls `System.Enum.GetValues(typeof(RelativeZone))` per call** — allocates an array and boxes eight enum values. A `static readonly RelativeZone[]` fixes it.
* **`ShouldEvaluateHide`'s escape hatch interacts badly with P7.** The hatch exists so a stuck agent re-evaluates, but P7 is a mechanism that *keeps* the agent in the no-path state, so the hatch fires continuously rather than rarely. Fixing P7 makes the hatch behave as designed; do not tune the hatch before fixing P7.
* **`NavMeshLock` / `NavMeshReadLocker` are unused, and that is fine.** They matter for code reading the navmesh from a worker thread. The mod's navmesh reads are all main-thread through the Unity API, which is safe by contract. Recorded so nobody adds locking that isn't needed.

---

## Suggested batch order

Each batch is independently shippable and independently revertable. **Nothing below has been applied.**

| Batch | Contents | Risk | Testable? |
|---|---|---|---|
| **N1** | P1 (search guard), P11 (`NavMeshPath` in loop) | Low — one guard and one buffer swap | **Yes, and obviously.** The Gargoyle starts patrolling. |
| **N2** | P6, P7, P13 (path-request correctness) | Low, but subtle | Only via absence of stuck states. Watch for it *not* freezing. |
| **N3** | P2, P3, P14, P15, P16 (all Unity-side) | Medium — tuning, needs iteration | **Needs a real multiplayer lobby, not a solo host round.** This is the one Mathew cannot self-test. |
| **N4** | P8, P10, then P4 last | Medium-high — new movement capability | Yes: this is what finally makes board Pending step 5 (entrance teleport) meaningful. |
| **N5** | P9 via two-destination `SmartPathTask` | Medium — first real use of the multi-goal API | Hard. Circling direction is subjective; use the existing `LogCat.Movement` trace line. |
| **N6** | P12, then P5 (async multi-goal everywhere) | High — restructures `SetDestinationToHiddenPosition` | Hard. Do last, and only after N1–N5 have been in a real round. |

**N1 first, and on its own.** It is the largest behaviour change per line of diff in the whole document, and it changes what the Gargoyle *does* rather than how fast it does it — so it wants a clean playtest with nothing else moving.

---

## Corrections to `Optimization_Audit.md`

Found while cross-referencing; **the audit doc is stale and a reader will be misled by it.** `c0c0189` ("Batch G") and `a1dac4b` landed after the doc was last updated, and the doc still lists their contents as unapplied.

| Finding | Doc says | Code says |
|---|---|---|
| **G2** (`ChooseClosestNodeToPos` sort-first) | NOT APPLIED | **APPLIED** — `MAX_PATH_CHECKS = 24` with a squared-distance pre-sort, and the docstring describing the fix, are both live. |
| **D1** (static layer never cleared on survival) | NOT APPLIED | **APPLIED** — `OnNetworkDespawn` → `RemoveSelfFromSharedState()` is live. |
| **D2** (`RefreshNodesIfNull` cannot refill an empty list) | NOT APPLIED | **APPLIED** — the condition is now `cachedNodes.Count == 0 \|\| cachedNodes.Any(n => n == null)`. |
| **`ShouldEvaluateHide`** (part of G2's throttle problem) | described as unconditional | **FIXED** — now obeys a short floor instead of no floor. |

**D3 (`LGInstance` gating door closing) is still genuinely open** — verify before acting on any of the above.
