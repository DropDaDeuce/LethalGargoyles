# LethalGargoyles ToDo

**OPEN ITEMS ONLY.** When something ships, it comes out of here and goes into the current month's `Docs\Archive\Done\Done_YYYY-MM.md` **and** into `CHANGELOG.md` under the release it lands in. Do not keep a done item here with a tick next to it — a stale open item is how a session gets sent to fix something that was fixed weeks ago.

**Seeded 2026-08-15** from `Gargoyle To Do.txt` and the To Do section of `README.md`. Both of those are now **superseded** — add new items here, not there. (`README.md`'s list contained a bullet reading literally `- Add `, truncated with its content lost; if Mathew remembers what it was, it belongs here.)

---

## Next up

- [ ] **One more taunt for each employee class.** Content work — needs a recorded line per class in `Taunt - EmployeeClass`, named to match the existing convention. No code change; the loader picks up new files in an existing category automatically.
- [ ] **Item-stealing mechanic.** The gargoyle takes an item from a player. Needs a config toggle to enable/disable it. Design open: what it steals, whether it drops or hides it, and whether it is server-authoritative through the existing state machine (it must be).
- [ ] **Gargoyle scrap that taunts.** The `GargoyleStatue` already talks — this item is about a *lower* volume than the monster so it reads as a different thing. Check `dogHear`/`dogCooldown` interaction before changing volume, since the Eyeless Dog hears it.
- [ ] **Activity taunt for a boombox playing near the gargoyle.** Fits the existing Activity taunt path (`AIHelperPatches` tracks the trigger, the AI fires `TauntClientRpc(..., "activity")`). Needs the taunt file plus a detection hook.

- [ ] **Work the pathfinding audit's batches N1–N6.** Full findings and the batch plan are in [`Docs\Plans\Pathfinding_Audit.md`](Docs/Plans/Pathfinding_Audit.md) (b10, 2026-08-16) — **read-only, nothing applied.** The three that matter most: **N1** the search state restarts itself every frame so the Gargoyle cannot roam (one guard fixes it, and it is also the biggest per-frame cost in the mod); **N3** the prefab has `AIIntervalTime`, `updatePositionThreshold` and `syncMovementSpeed` all at `0` where vanilla uses `0.2`/`1`/`0.22`, which means the AI ticks every frame and fires a position RPC to every client every frame — **Unity-side, needs a `gargoyleassets` rebuild, and cannot be seen while testing as host**; **N4** three of PathfindingLib's four smart-link flags are switched off by a hardcoded constant, so the Gargoyle has never been able to use a fire exit, the main entrance or the mineshaft elevator.
- [ ] **Decide whether the Gargoyle should be able to follow players outdoors through a fire exit.** This is a gameplay call, not a bug, and it gates batch N4 above. Today it structurally cannot: `GetAllowedLinks()` returns `InternalTeleports` only. It already targets outside players and already has a cheat-warp (`TryTeleportNearTarget`), so this is about whether it walks the route legitimately.

## Maybe / undecided

- [ ] **Mirage integration** — the gargoyle "repeats" voice lines back in a mocking manner. Explicitly conditional on difficulty and time in `README.md`; not committed to.
- [ ] **More ways to be mischievous without being overbearing.** Open-ended design item, kept because it is the mod's stated design goal and it is the lens for judging any new mechanic above.

## Needs an in-game check (code is compile-verified only)

- [ ] **Verify the Gargoyle still teleports correctly through fire exits / the main entrance.** The game deleted `EntranceTeleport.exitPoint` and b3 rewrote the one call site to `FindExitPoint()` + `exitScript.entrancePoint` (`LethalGargoylesAI`, section *6) Movement*, the `SmartDestinationType.EntranceTeleport` arm). It compiles and the API shape is confirmed by reflection, but **nothing has watched a Gargoyle actually use a teleport since the change.** What to watch: it should come out at the *far* side of the entrance it walked into — inside the facility when it enters from outside, and vice versa. Coming out on the same side, or not moving at all, means `exitScript` is the wrong end of the pairing.
  **BLOCKED, found 2026-08-16 (b10): this test cannot pass as written, and it is not b3's fault.** The `SmartDestinationType.EntranceTeleport` arm is **unreachable dead code** — `GetAllowedLinks()` returns `SmartPathfindingLinkFlags.InternalTeleports` only, so PathfindingLib will never hand the Gargoyle an entrance-teleport destination. Batch **N4** of [`Docs\Plans\Pathfinding_Audit.md`](Docs/Plans/Pathfinding_Audit.md) has to land first (and N4 must include P10, or the Gargoyle ping-pongs through the fire exit forever). Do not spend a playtest on this until then.
- [ ] **Confirm the mod still behaves on the current game version generally.** The build break proved the game updated under v0.7.0. One API broke and is fixed, but a compile only proves the *shapes* match — behaviour changes in vanilla AI, entrances or player code wouldn't show up at compile time.

## Housekeeping (unowned)

- [ ] **Cut a release for the game-compatibility fix.** `CHANGELOG.md` has an `Unreleased` section describing it; when the version is cut, rename that heading and make the three version numbers agree — csproj `<Version>`, `manifest.json` `version_number`, and the changelog heading. Nothing enforces that agreement.
- [ ] **Consider rebuilding `gargoyleassets` on the game's exact Unity version.** The bundle is `2022.3.62f3`, the game runs `2022.3.62f2`. It has shipped this way since v0.7.0 and appears fine, so this is hygiene, not a bug — but it is the first thing to suspect if the model, animations or audio ever fail to load while the DLL clearly loads.
- [x] ~~**Is Concentus actually a dependency?**~~ **ANSWERED 2026-08-15 — YES, KEEP IT. Do not remove it from `thunderstore.toml` or `manifest.json`.** Nothing under `Plugin\` references *Concentus*, which is what made it look dead — but the package also ships `System.Memory.dll`, `System.Buffers.dll`, `System.Numerics.Vectors.dll` and `System.Runtime.CompilerServices.Unsafe.dll`, and **`NVorbis 0.10.5`'s netstandard2.0 build requires `System.Memory`** (its nuspec declares it; `CopyNVorbis` copies that exact build). The game ships no `System.Memory.dll` and neither does this mod — **the only copy on disk is `BepInEx\plugins\qwbarch-Concentus\System.Memory.dll`.** Drop it and NVorbis throws on first decode, so every client silently gets no voice lines. Evidence and the optional clean fix (ship `System.Memory.dll` ourselves, *then* drop Concentus) are in `Docs\Plans\Optimization_Audit.md` → *Rulings* item 2.

## Worth doing sometime (not urgent)

- [ ] **Make the `System.Memory` dependency explicit instead of relying on Concentus to supply it.** Right now the mod depends on a third-party package for a transitive BCL assembly that package does not advertise; if qwbarch ever repackages Concentus without it, the mod breaks with no code change on our side. `System.Memory` is already in the NuGet cache as an NVorbis dependency, so a `CopyNVorbis`-style target could ship it beside `NVorbis.dll` — after which Concentus could genuinely be dropped. Packaging change, needs a real clean-install test, so it is a deliberate decision rather than a chore.
- [ ] **Decide the fate of `Gargoyle To Do.txt`.** Its content is fully carried above. Leaving it in place is a second backlog that will drift; deleting it is Mathew's call.
- [ ] **Consider Git LFS for `AssetSources\`.** The repo is ~720 MB and the largest tracked files are Audacity sources (up to 9.3 MB each) plus `Gargoyle.blend` at 6.4 MB — exactly what LFS is for. **But migrating existing history is a rewrite that breaks every existing clone**, so this is a decision to make deliberately, not a cleanup to slip into another batch. Doing it for *new* assets only is the cheaper half and doesn't rewrite anything.
