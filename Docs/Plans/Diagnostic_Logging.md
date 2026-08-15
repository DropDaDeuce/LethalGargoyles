# Diagnostic logging — design

**Status: LIVE, not implemented.** Written 2026-08-15 by board batch **b7**. This is **Batch A** of the improve/optimize pass in [`Optimization_Audit.md`](Optimization_Audit.md) — it ships first, before the partial-class split, because everything after it is easier to verify once this exists.

**The goal in one sentence:** make it possible to turn on detailed logging *from the config file, in a shipped build, without recompiling*, scoped to one subsystem at a time, so that a bug found during the refactor can be identified from a pasted `LogOutput.log` instead of a repro session.

---

## Why the current setup can't do this

There are exactly two logging levels in the mod today and neither is what's needed.

**`Plugin.LogIfDebugBuild` (and the AI's and AudioManager's local copies) are `[Conditional("DEBUG")]`.** The compiler deletes the *call* in a Release build. That is efficient, but it means:

- The only way to get detail is to ship Mathew a DEBUG build. Fine for him, useless for a player reporting a bug.
- It's all-or-nothing. A DEBUG build logs *everything* — pathing, taunts, audio, state — so finding the one line that matters means scrolling past thousands that don't.
- **It has already leaked twice.** `[Conditional]` strips the call, not the surrounding code. The audit found `Stopwatch.StartNew()` plus six `float t0 = (float)sw.Elapsed.TotalMilliseconds;` locals surviving into Release in `Update` (audit G3), and ~15 interpolated strings built into locals in `DoAIInterval` (audit G4) — including a `agent.remainingDistance` read that forces the agent to walk its corner list, purely to format a string that then gets thrown away.

**`Plugin.Logger.LogInfo` fires in Release for every player.** So it's reserved for things that matter to a bug report — which means almost nothing uses it, which is why the mod's worst bug class (a clip name that doesn't resolve on a client) produces *no log output at all*.

The gap is a middle tier: **detailed, off by default, switchable at runtime, and free when off.**

---

## The design

### Levels

```
Off  →  Error  →  Warn  →  Info  →  Debug  →  Trace
```

Six values, ordered. A message is emitted when its level is at or below the configured level for its category. Default is **`Warn`** for everything — quiet in a normal player's log, but the things that indicate a real defect still show up.

- **Error** — something is broken and a feature will not work. A clip over the 500 KB cap. An asset that failed to load.
- **Warn** — something unexpected that the mod recovered from. A clip name that didn't resolve on this client. A client that timed out during audio transfer.
- **Info** — significant one-off events. Enemy registered, N clips loaded, gargoyle spawned/died.
- **Debug** — state transitions, target changes, taunt decisions. **This is the level Mathew runs while refactoring.**
- **Trace** — per-tick detail. Path requests, zone evaluations, per-frame perception. A firehose; used to chase one specific thing.

### Categories

A `[Flags]` enum so several can be enabled at once, mapping to the AI file's own banner sections plus the non-AI subsystems:

```
None, Lifecycle, StateMachine, Targeting, Movement, Perception,
Combat, Taunt, Audio, Netcode, Config, Scrap, Performance, All
```

Each gets **its own level** in the config, so `Audio = Trace` while `Movement = Warn` is a normal thing to ask for. That combination is exactly what "why is this one client silent" needs, and today it's impossible.

### Config

New `Diagnostics` section in the BepInEx cfg:

```ini
[Diagnostics]
## Master switch. Off disables all diagnostic logging regardless of the per-category levels below.
Enabled = true
## Default level for any category not overridden below.
DefaultLevel = Warn
## Per-category overrides.
StateMachine = Warn
Targeting = Warn
Movement = Warn
Audio = Warn
Netcode = Warn
...
## Prefix every line with the gargoyle's instance tag and whether this machine is host or client.
IncludeContext = true
## Suppress a repeating message after this many occurrences (0 = never suppress).
RepeatLimit = 20
```

**This ships enabled in Release.** That is the point of the whole design — a player who reports "the gargoyle went quiet" can be told to set `Audio = Debug` and send the log. The cost when a category is at `Warn` is one integer comparison per call site, which is nothing.

### The call-site idiom, and the one rule that matters

**Never build the message before the level check.** This is the exact mistake that leaked `Stopwatch` and fifteen strings into Release. C# 10 interpolated-string handlers would solve it automatically, but they need a .NET 6 BCL type and **the target here is `netstandard2.1`, so they are not available** — the guard has to be explicit.

For anything on a per-frame or per-tick path:

```csharp
if (LGLog.On(Cat.Movement, Lvl.Trace))
    LGLog.Trace(Cat.Movement, $"{GargoyleTag} repath -> {dest} (dist {d:0.00})");
```

`On()` is a static method that compares two ints and returns a bool. When the category is off, **the interpolated string is never constructed** — no allocation, no `ToString`, no property reads. This is verbose at the call site and that is a deliberate trade: it is the only form that is genuinely free when disabled.

For cold paths — startup, round transitions, error handlers, anything that runs a handful of times — call directly and don't bother with the guard:

```csharp
LGLog.Warn(Cat.Audio, $"Taunt '{clipName}' (type '{clipType}') not found locally — list has {clipList.Count} clips.");
```

**`LogIfSlow` and the `DoAIInterval` diagnostic block move behind `#if DEBUG` rather than into this system.** They read live agent state (`remainingDistance` forces a corner-list walk) and time things with a `Stopwatch`; that cost is real even when the log line is discarded, so a runtime flag is the wrong tool. Those two stay compile-time-stripped. Audit items G3 and G4 are the fix.

### Context tagging

Every line gets a prefix so a multi-gargoyle, multi-machine log is readable:

```
[LethalGargoyles][H][G3][StateMachine] SwitchState AggressivePursuit -> Idle (reason: target null)
```

- `H` / `C` — host or client. **Half the audit's findings are "works on host, not on client", so this single character is worth more than it looks.**
- `G3` — the gargoyle instance. `GargoyleTag` already exists on the AI and is already used in log lines; reuse it, don't invent a second scheme.
- Category name, so a reader can filter with a text search.

### Repeat suppression

Several of the audited bugs run *every frame* (`EnemyTaunt` scanning all enemies, `Taunt()` re-entering because a timer wasn't advanced, `ChooseClosestNodeToPos` when the hide throttle is bypassed). If those log unsuppressed at `Debug`, they produce thousands of lines a minute and bury everything else.

`LGLog` keeps a small dictionary keyed by call site + message shape. After `RepeatLimit` occurrences it emits one `… (suppressing further occurrences)` line and goes quiet for that key until the round ends. Two helpers on top:

- **`LGLog.Once(cat, key, msg)`** — emits the first time only. For "this should be impossible" conditions.
- **`LGLog.Invariant(cond, cat, msg)`** — if `cond` is false, logs once per instance at `Error`. See below.

### Invariant checks — the part aimed squarely at this refactor

The nine broken features found in the audit share a shape: **a value that was supposed to be kept in sync silently wasn't.** `gargoyleTargets[myID]` never written. `lastSteamIDTauntTime` never updated. Both zone distances reset every call. A push flag never cleared.

None of those throw. None log. They just quietly do nothing, which is why they survived to 0.7.0.

`LGLog.Invariant` is a cheap assertion that fires once per instance per violation:

```csharp
LGLog.Invariant(gargoyleTargets[myID] == targetPlayer, Cat.Targeting,
    $"{GargoyleTag} target desync: map={gargoyleTargets[myID]?.playerUsername ?? "null"} field={targetPlayer?.playerUsername ?? "null"}");
```

The initial set, one per audit finding they'd have caught:

| Invariant | Catches |
|---|---|
| `gargoyleTargets[myID] == targetPlayer` after every target write | E1 — dead target balancing |
| `leftPathDist != rightPathDist` after a `CheckZonePath` pair, or both != 1000f | E2 — the reset-clobber |
| `closestPlayer != null \|\| distanceToClosestPlayerSqr == float.MaxValue` | E3 — the null-is-distance-zero trap |
| At most one `true` per player in `playerPushStates` | D1, D5 — stale push flags |
| `activeGargoyles` contains no destroyed instances | D2, A3 — leaked corpses |
| `LGInstance != null` when any gargoyle is alive | D3 — door closing dying with one gargoyle |
| Host and client clip-list counts match per category | F1–F5 — every audio transfer failure |

That last one needs a small addition: the host sends its per-category counts once after transfer completes, and each client compares against its own. **That single check catches the entire audio failure class in one line of log**, instead of the current situation where four different bugs all present as "the gargoyle isn't talking."

### Session banner

One `Info` block at load, so a pasted log is immediately useful without a follow-up question:

```
[LethalGargoyles] v0.7.0 | host | game 2022.3.62f2 | bundle 2022.3.62f3
[LethalGargoyles] Soft deps: Coroner=yes EmployeeClasses=no EnhancedMonsters=no
[LethalGargoyles] Clips: General 34, Aggro 5, PriorDeath 18(+61 Coroner), Activity 21, ... total 170 / 10.6 MB
[LethalGargoyles] Config: aggroRange 30 sightRange 10.95 push=on scrap=on
[LethalGargoyles] Diagnostics: Audio=Debug, others=Warn
```

`AudioManager.LogClipCounts` already produces most of the third line — fold it in rather than duplicating it. **The bundle-vs-game Unity version mismatch goes on line one deliberately**, since `CLAUDE.md` names it as the first thing to suspect when assets don't load.

### State-transition reasons

`SwitchState(State)` gains an optional reason string used only for logging:

```csharp
SwitchState(State.SearchingForPlayer, "target null");
SwitchState(State.Idle, "out of aggro range");
```

A `Debug`-level line per transition with the reason attached is, on its own, most of what's needed to diagnose a stuck state machine — and audit finding E3 (`AggressivePursuit` with no exit when `closestPlayer` is null) would have been obvious from a log that simply stopped producing transitions while the chase animation kept playing.

---

## What this costs

- **When a category is off:** one `int` comparison per guarded call site. Nothing else runs.
- **When on:** the string is built and written through BepInEx's logger, same as today's `LogIfDebugBuild`.
- **Code size:** one new file, `Plugin\src\Utility\LGLog.cs`, roughly 200 lines, plus a `Diagnostics` block in `Configuration.cs`.
- **Migration:** existing `LogIfDebugBuild` calls get a category and a level as they're touched. **They do not all need converting at once** — the old helper keeps working, and a call site that hasn't been migrated behaves exactly as it does today. Convert opportunistically during the batches that follow.

## What it does not do

- **No in-game overlay.** That needs Unity assets and a prefab change, which is a Mathew step and a `gargoyleassets` rebuild. Not worth it for this.
- **No separate log file.** BepInEx's `LogOutput.log` is where people already look, and a second file is a second thing to ask a bug reporter for.
- **No live config reload.** Levels are read at load. Changing one means restarting the game, which is acceptable and avoids a whole class of thread-safety problems.

---

## Ordering

This ships **first**, as Batch A, before the partial-class split. Two reasons: the split's smoke test is more meaningful with transition logging on, and every batch after it gets a cheaper playtest. It absorbs the old A1 (the missing `TauntClientRpc` warning) — that finding is simply the first consumer of the new `Warn` path.

The rest of the old Batch A (crash guards, null checks, `Awake` try/catch) ships in the **same** commit, since it's all `safe` and all in service of the same goal.

**Playtest for this batch:** run a round with defaults and confirm the log is quiet apart from the session banner — no new noise for a normal player. Then set `[Diagnostics] StateMachine = Debug`, run again, and confirm a readable transition trail with reasons. If any `Invariant` line appears, that is a real pre-existing bug being surfaced for the first time, not a regression — send it over.
