---
name: ship-release
description: Use when cutting a new version of the mod for Thunderstore - bumping the version, building the release package, writing the changelog entry, or checking that a release is consistent before it goes out. Also use when asked "what's left before I can release" or when a release went out wrong. Do NOT use for ordinary debug builds, which deploy to the local Dev profile and are not releases.
---

# Shipping a release

Releases are rare, so the procedure is re-derived every time and nothing enforces it. That is exactly the shape of task that ships broken.

## Three version numbers must agree, and NOTHING checks them

1. `Plugin\LethalGargoyles.csproj` → `<Version>`
2. `Plugin\Thunderstore\manifest.json` → `version_number`
3. `CHANGELOG.md` → the top heading

The csproj version is what `tcli` stamps on the package. A mismatch does not fail the build — it ships a package whose own metadata disagrees with itself. **Check all three by hand, every time.**

## Dependencies live in TWO files that have already drifted once

`Plugin\Thunderstore\thunderstore.toml` is what `tcli` actually publishes. `manifest.json` is only copied into the local dev profile. They disagreed for at least one release — the manifest omitted **PathfindingLib, a hard dependency**. Fixed 2026-08-15; keep them in sync, and treat the toml as authoritative.

Adding or removing a dependency is **player-facing** — it changes what mod managers install for everyone. That is Mathew's call, not a session's.

## Order of operations — the bundle comes FIRST

1. **Does anything in `UnityProject\Assets\` need to ship?** Prefabs, animator, materials, audio assets, the mixer. If yes, **Mathew rebuilds `gargoyleassets` in the Unity Editor before anything else.** Nothing in this repo can build it, and a stale bundle is invisible at build time — the DLL updates, the bundle doesn't, and the change simply isn't there in game.
2. Bump the three version numbers.
3. Write the changelog entry (below).
4. `dotnet build -c Release` from `Plugin\`. Release additionally runs `PackThunderstore` → `dotnet tcli build` against the toml, output to `Plugin\Thunderstore\Packages\`. It packages from `bin\Release\`.
5. Confirm the netcode patcher ran on the **Release** output — `DropDaDeuce.LethalGargoyles_original.dll` beside the patched DLL. A release DLL that skipped it throws unhelpful reflection errors at runtime for every player.
6. Inspect the produced zip before uploading: the DLL, `gargoyleassets`, the whole `Voice Lines` tree, `NVorbis.dll` under `lib\NVorbis\`, the Coroner strings XML, and `CHANGELOG.md`.

## The changelog entry

Match the existing voice — a themed release name (`## v0.7.0 - The Gargoyle Is Back!`), then bolded groupings (**New Features:**, **Changes:**, **Bug Fixes:**, **Game Parity:**). It is **player-facing**: describe what a player will notice, not what changed in the code. Engineering detail belongs in `Docs\Archive\Done\Done_YYYY-MM.md`.

If an `## Unreleased` section is sitting at the top, that is the accumulated work — rename it to the new version heading rather than writing a fresh one.

## Before you call it done

* **Has it been played?** A green Release build proves nothing about behaviour. Check `ToDo.md` for open in-game verification items — shipping with one outstanding is a decision, not an oversight, and it should be a conscious one.
* **Does the README need it?** Player-facing features, new voice-line categories and compatibility notes live there. It is allowed to lag the code, but not across a release.
* **Game version parity.** If the release is a response to a game update, say which game version it targets in the changelog — players read that first.

Committing and uploading are Mathew's steps. Never publish on his behalf.
