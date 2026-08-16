# Session Board History

Released claim rows moved off [`Docs\Session_Board.md`](../Session_Board.md) so the live board stays short enough to read in one go.

**Rows land here VERBATIM.** Trimming the board is a MOVE, never a delete — these rows carry rulings, measured findings and "why it looks like this" notes that exist nowhere else. Add a dated section header for each groom, newest at the top, and keep the original table shape inside it.

**Nothing here is open work.** If a row here still sounds like something owed, it is a bookkeeping failure, not a task — check the code before acting on it.

---

## Groomed 2026-08-16 (during board b19)

Moved off the board verbatim when *Recently done* passed ten rows.

| Session | Batch | Files | Done |
|---------|-------|-------|------|
| optimize-pass-exec (AI + audio + scrap + config + build) | 8 | Plugin\src\Utility\LGLog.cs (NEW), Plugin\src\Enemy\LethalGargoylesAI.cs, Plugin\src\Utility\AudioManager.cs, Plugin\src\Scrap\GargoyleStatue.cs, Plugin\src\Config\Configuration.cs, Plugin\src\LethalGargoyles.cs, Docs\Plans\Optimization_Audit.md, Docs\Session_Board.md | 2026-08-15 — **executed batches A, E(partial), F, and C/D/G(partial) of the b5 audit autonomously at Mathew's explicit direction while he was away; he accepted the risk after it was raised. FIVE COMMITS, one per batch, so any single batch reverts alone** (`61cc313` A, `0a9ff00` E, `3f92b72` F, `a1dac4b` C/D/G). **EVERY COMMIT IS COMPILE-VERIFIED ONLY — 0 warnings / 0 errors in Debug AND Release — and NOT ONE LINE HAS BEEN RUN.** Release was built deliberately, not as a formality: it is the only configuration where the new `#if DEBUG` blocks are actually excluded. **The two decisions a later session must not silently undo:** (1) **Batch B, the partial-class split, was SKIPPED** — it would have eaten the remaining budget rewriting 2,900 lines for zero behaviour gain, and its whole purpose was to make *later* diffs readable while the later diffs were happening first. It is now a BIGGER job than b5 estimated, since four commits landed on top. (2) **C3, the push/damage owner-authority fix, was deliberately NOT applied** — it is the only top-tier finding still open, it is the one marked PLAUSIBLE rather than CONFIRMED, and it rewrites how damage reaches players. Shipping an unverified rewrite of the damage path with nobody able to test was the worse bet. **Found while fixing E2 and NOT in the original audit:** `GetTargetPosition` picked `targetZone` before the left/right decision, so once the distances actually differed the "left wins" branch returned the RIGHT zone — unreachable only because E2 kept both distances equal, so fixing E2 alone would have swapped one bug for another. Both went in together. Also renamed the new `LogLevel` enum to `LGLevel` (collided with `Unity.Netcode.LogLevel`) and dropped a `using System;` from the AI file (made `Random` ambiguous against `UnityEngine.Random`, which five sites use unqualified). |

---

## Groomed 2026-08-16 (during board b17)

Moved off the board verbatim when *Recently done* passed ten rows.

| Session | Batch | Files | Done |
|---------|-------|-------|------|
| thumbnail (build + packaging) | 6 | icon.png, AssetSources\Images\thumbnail_build.py (NEW), AssetSources\Images\Thumbnails\ (NEW) | 2026-08-15 — **new Thunderstore icon, composited from the real model render rather than generated.** `icon.png` is now the `D_stone` candidate: the gargoyle from `Screenshot_3448-Photoroom.png` (which already carries a clean alpha channel) over a blurred crop of the AI background's corridor *floor*. **That crop box `(456, 400, 1080, 1024)` is load-bearing — it sits below every piece of AI-rendered text in the source image,** including the misspelled "CATION" sign and the "LETHAL COMPANY" wordmark, which is why the result has no AI tell. The old icon's real defect was contrast, not provenance: a dark creature on a dark background with no rim separation, unreadable at the 64-128px that Gale and r2modman actually render. `thumbnail_build.py` regenerates all four candidates plus a `_64x.png` legibility proof for each; tuning knobs (`SC`, `EY`, `ES`, `silhouette=`) are documented in its docstring. **Packaging only — no `.cs` touched, no build run, no `gargoyleassets` rebuild. It reaches players only on the next Release build.** |

---

## Groomed 2026-08-16 (during board b16)

Moved off the board verbatim when *Recently done* passed ten rows.

| Session | Batch | Files | Done |
|---------|-------|-------|------|
| optimization-audit (docs / planning) | 5 | Docs\Plans\Optimization_Audit.md (NEW), Docs\INDEX.md, Docs\Session_Board.md | 2026-08-15 — **phase 1 of the improve/optimize pass: a read-only audit of all of `Plugin\src\**`, ~70 findings, NOTHING applied.** Full record in [`Optimization_Audit.md`](Plans/Optimization_Audit.md) — read its *headline* table first. **The finding that reframes the whole pass: this is not primarily a performance problem — NINE FEATURES DO NOT WORK, or work only for the host.** Target balancing is dead (`gargoyleTargets[myID]` is never written on acquisition, so every gargoyle stalks the same player and `ChangeTarget` is unreachable dead code); `CheckZonePath` resets *both* distances at the top of every call, so left/right circling has never worked; the statue's `ItemActivate` gates a `RequireOwnership = false` ServerRpc behind `IsServer`, muting it for non-hosts; push damage is applied server-side to remote players' copies (PLAUSIBLE — **invisible to Mathew because he tests as host**); `LGInstance` gating means one gargoyle's death stops ALL survivors closing doors; stereo OGGs decode to half length and one shipped clip is stereo; and static state is never cleared when the crew *survives*, leaving a player permanently un-pushable for the session. **Two findings were corroborated by independent agents (the statue `IsServer` bug, the `randomIndex++` overflow) — the safest bets in the doc.** Settled two open `ToDo.md` questions with evidence: **Concentus is confirmed unused** (zero hits across `Plugin\`), and the seven-place category table is currently in sync. **Verified clean, recorded so nobody re-audits it:** zero soft-dep leakage, zero Harmony prefixes, no patch-target drift (all eight checked by reflection against the installed assembly, parameter names included), all eleven static clip lists correctly cleared. **Board-relevant: the static layer has NO true data race** — every writer is main-thread, so the `ConcurrentDictionary`s and `PlayerPushStatesLock` are currently decorative; its bugs are lifecycle and staleness, not concurrency. Corrected `Docs\INDEX.md`: the root `.gitignore` and `.gitattributes` are **untracked**, contradicting rule 12 below. |

---

## Groomed 2026-08-16 (during board b15)

Moved off the board verbatim when *Recently done* passed ten rows.

| Session | Batch | Files | Done |
|---------|-------|-------|------|
| skills-and-subagents (docs / planning) | 4 | .claude\skills\voice-line\, .claude\skills\game-update\ (+ reflect.cs), .claude\skills\ship-release\, .claude\skills\playtest\, CLAUDE.md, Docs\INDEX.md, Docs\Session_Board.md, Docs\Archive\Done\Done_2026-08.md | 2026-08-15 — **four procedure skills + subagents as standing policy.** The new skills carry STEPS (the two old ones only move the model dial). **A verification sweep by `Explore` found `CLAUDE.md`'s "adding a voice-line category is a six-place change" was WRONG — it is seven, and the missed one is `Scrap\GargoyleStatue.cs`, which holds its own parallel `clipType` switch.** Miss it and the monster taunts while the scrap is silent. `game-update` ships a working `reflect.cs` that reads any vanilla type off the installed assembly. Board-relevant: **a subagent must never claim a row, never run a build, and defaults to read-only** — the parent session owns its claim end to end. Full record in [`Done_2026-08.md`](Archive/Done/Done_2026-08.md). |

---

## Groomed 2026-08-16 (during board b14)

Moved off the board verbatim when *Recently done* passed ten rows.

| Session | Batch | Files | Done |
|---------|-------|-------|------|
| csproj-portable-paths + game-compat (build + packaging, AI / state machine) | 3 | Plugin\LethalGargoyles.csproj, Plugin\src\Enemy\LethalGargoylesAI.cs, CHANGELOG.md, CLAUDE.md, ToDo.md, Docs\Session_Board.md, Docs\Archive\Done\Done_2026-08.md | 2026-08-15 — **the build is GREEN again (0 errors, 0 warnings) but it did not start that way, and the reason predates this session: THE GAME UPDATED UNDER v0.7.0.** A baseline build run *before* any edit failed on `EntranceTeleport.exitPoint`, which the game **deleted**. Settled by reflecting over the installed `Assembly-CSharp.dll` with a `MetadataLoadContext` — grep is actively misleading here, since `exitPointDoesntExist` leaves the string in the binary. Fixed via `FindExitPoint()` + `exitScript.entrancePoint`, which also adds a null guard the old code lacked. **Compile-verified only — an in-game teleport check is parked in `ToDo.md`.** csproj: `$(NuGetPackageRoot)` + a shared `$(NVorbisVersion)` replace the hardcoded nuget path; `Unity.Collections`/`Unity.Mathematics` move to `$(ManagedDirectory)` with `Private="false"`, which also stopped two game assemblies being copied into `bin\` every build. Full record in [`Done_2026-08.md`](Archive/Done/Done_2026-08.md). |

---

## Groomed 2026-08-16 (during board b20)

Moved off the board verbatim when *Recently done* passed ten rows.

| Session | Batch | Files | Done |
|---------|-------|-------|------|
| concentus-correction (build + packaging) | 9 | Docs\Plans\Optimization_Audit.md, ToDo.md, Docs\Session_Board.md | 2026-08-15 — **REVERSES b5/b7's ruling that Concentus is an unused dependency. IT IS LOAD-BEARING — do not drop it.** Mathew caught it from the Thunderstore listing, which shows the package bundles four BCL shims beside the codec. **`NVorbis 0.10.5`'s netstandard2.0 build requires `System.Memory`** (declared in its nuspec; `CopyNVorbis` copies that exact build). The game ships no `System.Memory.dll` — 167 assemblies in `Managed\` and the only near-match is the unrelated `System.Numerics.dll` — and this mod's output contains only `NVorbis.dll`/`NVorbis.xml`. **The single copy on disk is `BepInEx\plugins\qwbarch-Concentus\System.Memory.dll`.** Dropping the dependency would leave every client unable to decode any voice line, presenting exactly like the silent-audio failures in A1/F1–F5. `System.ValueTuple`, NVorbis's other declared dependency, is covered by Unity's `mscorlib` and is not at risk. **The lesson, and the reason a whole audit agent got this wrong: it searched the tree for "Concentus", correctly found zero hits, and concluded dead — but a package's value can be the transitive assemblies it bundles, and searching for a dependency by its own name can never find that.** Real remaining defect, filed in `ToDo.md` as non-urgent: the reliance is implicit and undocumented, so a repackage upstream would break the mod with no code change here; the clean fix is to ship `System.Memory.dll` ourselves via a `CopyNVorbis`-style target and only then drop Concentus. |

---

## Groomed 2026-08-16 (during board b18)

Moved off the board verbatim when *Recently done* passed ten rows.

| Session | Batch | Files | Done |
|---------|-------|-------|------|
| skills-and-subagents (docs / planning) | 4 | .claude\skills\voice-line\, .claude\skills\game-update\ (+ reflect.cs), .claude\skills\ship-release\, .claude\skills\playtest\, CLAUDE.md, Docs\INDEX.md, Docs\Session_Board.md, Docs\Archive\Done\Done_2026-08.md | 2026-08-15 — **four procedure skills + subagents as standing policy.** The new skills carry STEPS (the two old ones only move the model dial). **A verification sweep by `Explore` found `CLAUDE.md`'s "adding a voice-line category is a six-place change" was WRONG — it is seven, and the missed one is `Scrap\GargoyleStatue.cs`, which holds its own parallel `clipType` switch.** Miss it and the monster taunts while the scrap is silent. `game-update` ships a working `reflect.cs` that reads any vanilla type off the installed assembly. Board-relevant: **a subagent must never claim a row, never run a build, and defaults to read-only** — the parent session owns its claim end to end. Full record in [`Done_2026-08.md`](Archive/Done/Done_2026-08.md). |
| diagnostic-logging (docs / planning) | 7 | Docs\Plans\Diagnostic_Logging.md (NEW), Docs\Plans\Optimization_Audit.md, Docs\INDEX.md, Docs\Session_Board.md | 2026-08-15 — **docs only, no source touched.** Designed the runtime-gated diagnostic logging layer in [`Diagnostic_Logging.md`](Plans/Diagnostic_Logging.md); it becomes **Batch A** of the b5 plan and absorbs b5's old A1. **The design point that matters: `[Conditional("DEBUG")]` structurally cannot do this job** — it strips the *call*, not the surrounding code (which is how a `Stopwatch` and ~15 interpolated strings leaked into Release, audit G3/G4), and it is all-or-nothing, so getting detail means shipping Mathew a DEBUG build and it is useless for a player bug report. Replacement is levels × per-subsystem categories, read from the BepInEx cfg and **live in Release**, guarded by an explicit `if (LGLog.On(cat, lvl))` because **C# 10 interpolated-string handlers need a .NET 6 BCL type and the target is `netstandard2.1`** — the guard cannot be automatic here. Carries `LGLog.Invariant`, aimed at the exact silent-desync shape the audit hit nine times, with a starting set of seven checks mapped to specific findings; the host↔client clip-count check alone collapses the whole audio failure class into one log line. **Deliberately NOT included: an in-game overlay (needs a `gargoyleassets` rebuild), a second log file, and live config reload.** Also recorded **all six of Mathew's rulings** on b5's open questions in `Optimization_Audit.md` → *Rulings* — that section is now authoritative over the individual finding rows. Two of them are player-facing and must ride a release with a CHANGELOG line, not a code batch: **dropping Concentus** and **the new per-clip toggles for custom voice lines**. |

---

## Groomed 2026-08-16 (during board b12)

Moved off the board verbatim when *Recently done* passed ten rows.

| Session | Batch | Files | Done |
|---------|-------|-------|------|
| doc-corrections + git-structure (docs / planning + build) | 2 | .gitignore (NEW), .gitattributes (NEW), README.md, Plugin\Thunderstore\manifest.json, CLAUDE.md, ToDo.md, Docs\Session_Board.md, Docs\Archive\Done\Done_2026-08.md | 2026-08-15 — **two b1 findings fixed, one parked, and the reason it's parked outranks all three: THE GAME IS UNINSTALLED, so nothing here can be built.** `Lethal Company_Data\` has no `Managed\` folder; what's left in the game directory is mod debris Steam doesn't remove. The README's custom voice-line path was wrong and **the live install proved it** — `<game root>\Lethal Gargoyles\Custom Voice Lines\` is on disk with real user OGGs in it. `manifest.json` now matches `thunderstore.toml` (it had omitted PathfindingLib, a *hard* dependency). The csproj path fix was deliberately NOT shipped: build-affecting and unverifiable. Git: root `.gitignore` + `.gitattributes` added, a tracked `.pyc` untracked, and an `Icon.png`/`icon.png` case bug fixed that `core.ignorecase=true` had been hiding. **Index changes are STAGED, not committed.** Full record in [`Done_2026-08.md`](Archive/Done/Done_2026-08.md). |

---

## Groomed 2026-08-16 (during board b11)

Moved off the board verbatim when *Recently done* passed ten rows.

| Session | Batch | Files | Done |
|---------|-------|-------|------|
| docs-scaffold (docs / planning) | 1 | CLAUDE.md (NEW), ToDo.md (NEW), Docs\INDEX.md (NEW), Docs\Session_Board.md (NEW), Docs\Archive\Done\Done_2026-08.md (NEW), Docs\Archive\Session_Board_History.md (NEW), Docs\Plans\README.md (NEW), .claude\agents\* (NEW), .claude\skills\* (NEW) | 2026-08-15 — **the instruction/coordination structure ported from ShippingHelper and adapted to a C#/Unity mod.** Docs only; no code touched, nothing to build. **The adaptation that matters: ShippingHelper's collision surface is one Mathew + one .accdb funnelling through a serial hand-apply step, so its board guards *unapplied staged changes*. Here Claude edits and builds directly, so the board guards the SHARED BUILD OUTPUT and the dev profile instead — hence rule 5, which has no ShippingHelper equivalent.** Three doc/code disagreements were measured while writing it and are filed in `ToDo.md` → Housekeeping rather than silently fixed: the README's custom voice-line path omits the `Lethal Gargoyles` folder level, `manifest.json` and `thunderstore.toml` declare different dependencies, and the csproj carries two machine-specific paths. |

---

*(Empty as of 2026-08-15 — board b1 created the board itself. The first groom will land its section above this line.)*
