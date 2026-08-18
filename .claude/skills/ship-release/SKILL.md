---
name: ship-release
description: Use when cutting a new version of the mod for Thunderstore or Nexus Mods - bumping the version, building the release packages, writing the changelog entry, updating the Nexus description, or checking that a release is consistent before it goes out. Also use when asked "what's left before I can release" or when a release went out wrong. Do NOT use for ordinary debug builds, which deploy to the local Dev profile and are not releases.
---

# Shipping a release

Releases are rare, so the procedure is re-derived every time and nothing enforces it. That is exactly the shape of task that ships broken.

**The mod ships to TWO storefronts and one `dotnet build -c Release` produces both.** Thunderstore gets `Plugin\Thunderstore\Packages\DropDaDeuce-LethalGargoyles-<ver>.zip` from `tcli`. Nexus gets `Plugin\Nexus\Packages\LethalGargoyles-<ver>.zip` from `pack-nexus.ps1`. They are **different shapes on purpose** — see `Plugin\Nexus\README.md` — so never upload one to the other's site.

## Three version numbers must agree, and NOTHING checks them

1. `Plugin\LethalGargoyles.csproj` → `<Version>`
2. `Plugin\Thunderstore\manifest.json` → `version_number`
3. `CHANGELOG.md` → the top heading

The csproj version is what `tcli` stamps on the package. A mismatch does not fail the build — it ships a package whose own metadata disagrees with itself. **Check all three by hand, every time.**

## Dependencies live in FOUR places and two of them are prose

`Plugin\Thunderstore\thunderstore.toml` is what `tcli` actually publishes. `manifest.json` is only copied into the local dev profile. They disagreed for at least one release — the manifest omitted **PathfindingLib, a hard dependency**. Fixed 2026-08-15; keep them in sync, and treat the toml as authoritative.

**Nexus has no dependency manifest at all.** Its requirements exist only as sentences a player reads, in `Plugin\Nexus\Nexus_Description.bbcode` and `Plugin\Nexus\MANUAL-INSTALL.txt`, plus whatever is listed in the Requirements tab on the mod page. Nothing checks those against the toml, and nothing ever will. **Change a dependency and you change all four by hand.**

Adding or removing a dependency is **player-facing** — it changes what mod managers install for everyone. That is Mathew's call, not a session's.

## Order of operations — the bundle comes FIRST

1. **Does anything in `UnityProject\Assets\` need to ship?** Prefabs, animator, materials, audio assets, the mixer. If yes, **Mathew rebuilds `gargoyleassets` in the Unity Editor before anything else.** Nothing in this repo can build it, and a stale bundle is invisible at build time — the DLL updates, the bundle doesn't, and the change simply isn't there in game.
2. Bump the three version numbers.
3. Write the changelog entry (below).
4. **Does the Nexus description need it?** New feature, new voice-line category, changed install path, changed dependency — mirror it into `Plugin\Nexus\Nexus_Description.bbcode`. **It does not regenerate from `README.md`**, deliberately, so it goes stale silently.
5. `dotnet build -c Release` from `Plugin\`. Release runs BOTH packagers off the same `bin\Release\`: `PackThunderstore` (`dotnet tcli build` against the toml → `Plugin\Thunderstore\Packages\`) and `PackNexus` (`Plugin\Nexus\pack-nexus.ps1` → `Plugin\Nexus\Packages\`).
6. Confirm the netcode patcher ran on the **Release** output — `DropDaDeuce.LethalGargoyles_original.dll` beside the patched DLL. A release DLL that skipped it throws unhelpful reflection errors at runtime for every player. `pack-nexus.ps1` already refuses to package without it, but `tcli` does not check.
7. Inspect **both** zips before uploading. Thunderstore's starts at `plugins\`, Nexus's starts at `BepInEx\plugins\`. Each needs the DLL, `gargoyleassets`, the whole `Voice Lines` tree, `NVorbis.dll` under `lib\NVorbis\`, the Coroner strings XML and `CHANGELOG.md`. The Nexus zip additionally carries `fomod\` and `READ ME FIRST - Manual Install.txt`.

## The changelog entry

**Load the `changelog-writer` skill and follow it** — it carries the voice, the group headers, the punctuation bans and the "say it when it's true" list. The release-specific parts are only these two:

* The heading gets a themed release name: `## v0.7.0 - The Gargoyle Is Back!`.
* If an `## Unreleased` section is sitting at the top, that is the accumulated work. **Rename it to the new version heading** rather than writing a fresh one, then re-read the whole thing as one document — bullets written weeks apart in separate batches often repeat each other.

## Uploading to Nexus — Mathew's steps, and they differ from Thunderstore's

Thunderstore takes a package and reads the metadata out of it. **Nexus takes a file and asks a human for everything else**, so a session's job is to hand him the exact text, not a link to the README.

1. Upload `Plugin\Nexus\Packages\LethalGargoyles-<ver>.zip` as the main file. **Set the file's NAME field to `Lethal Gargoyles` and its VERSION field to the release version, and get both right** — measured 2026-08-16: those two Nexus fields are what populate the Name and Version columns in every user's Vortex. **`fomod\info.xml` does NOT do it.** Vortex used to read `<Name>` from it and the call is commented out in Vortex 2.x (`installer_fomod_shared/index.ts`, *"not worth the hassle"*), and nothing has ever read `<Version>` from it. Sloppy fields here show up as a mod called `LethalGargoyles-0.7.0.zip` with a blank version.
2. Paste `Plugin\Nexus\Nexus_Description.bbcode` into the description box **as BBCode**. Nexus does not render Markdown, so pasting the README gives a wall of asterisks and hashes.
3. Fill the **Requirements** tab — it is called **"Requirements and mirrors"** in the mod page editor, and has three boxes. **LethalLib, PathfindingLib AND Concentus** go in the third, **"Other required resources"** — name, URL, notes — pointing at Thunderstore. They render to players under an **Off-site requirements** heading (`Mod name` / `Notes`).
   * **Take the dependency list from `thunderstore.toml`, never from a BepInEx error or from the source.** There are **four** required, and only two of them are `[BepInDependency]` attributes in the code — **Concentus is declared in the toml and manifest and appears nowhere in `Plugin\src`**, so both the log and the source undercount it. b33 shipped a list of three off exactly that mistake. NVorbis is on neither list: it ships inside the package.
   * The three **optional** mods (Coroner, Employee Classes, Enhanced Monsters) are soft deps and belong in the description, not the requirements boxes. **Neither is on Nexus** (verified 2026-08-16 by enumerating all 181 Lethal Company mods through the Nexus API), so the "Required mods on the Nexus" search will not find them and that is expected, not a mistake.
   * **Do fill it in: the nexusmods.com WEBSITE prompts people to grab listed requirements when they click download.** That prompt is real and it is the main reason the tab is worth the two minutes.
   * **Vortex, however, ignores the tab entirely** — for Nexus-hosted and off-site requirements alike (Nexus-Mods/Vortex issue 16360). Nobody installing through Vortex is ever prompted. **So the requirements text in the description and in the FOMOD's first install page is what a Vortex user actually sees**, and those are the copies that must stay correct.
   * **Checked and rejected: the `.vdeps` file** used by the third-party *Mod Dependency Fulfiller* extension. It only resolves dependencies that are themselves **downloaded through Nexus**, which is exactly what ours are not. A **Vortex Collection** is the only route that automates a non-Nexus dependency (as a Direct Download or a guided Browse-a-website step), and that is a separate product to maintain, not a release step.
4. Paste the release's `CHANGELOG.md` section into the Changelog tab.
5. Tick **"This mod contains AI generated content"** if Nexus asks. The README carries that disclosure and it should not quietly stop being true on the other storefront.

**A FOMOD that fails validation fails SILENTLY at authoring time and loudly on a player's machine.** b28 shipped one; b31 fixed it. `pack-nexus.ps1` now runs the two checks that catch it — Vortex's own version-detection regex, then schema validation — and **the first one is the one that matters**, because validating against the 5.0 schema by hand skips the detection that is usually the real bug. If the pack ever throws `Vortex would read this FOMOD as XmlScript 1.0`, the fix is in the `xsi:noNamespaceSchemaLocation` string, not in the installer logic. `Plugin\Nexus\README.md` has the detail.

**The FOMOD is otherwise untestable from here.** No session can run Vortex, so "the installer works" is never something to claim on a session's word. The archive is built so that **a plain drag of the root `BepInEx` folder is a complete install** even if the FOMOD is ignored entirely — that fallback is what makes a bad installer a cosmetic problem rather than a broken release. If Mathew wants it verified, he installs the zip through Vortex once and checks that `BepInEx\plugins\LethalGargoyles\` exists in the game folder with the `Voice Lines` tree under it.

## Before you call it done

* **Has it been played?** A green Release build proves nothing about behaviour. Check `ToDo.md` for open in-game verification items — shipping with one outstanding is a decision, not an oversight, and it should be a conscious one.
* **Does the README need it?** Player-facing features, new voice-line categories and compatibility notes live there. It is allowed to lag the code, but not across a release. **The Nexus description is a second copy of that same information** — if you touched one, check the other.
* **Game version parity.** If the release is a response to a game update, say which game version it targets in the changelog — players read that first.

Committing and uploading are Mathew's steps. Never publish on his behalf.
