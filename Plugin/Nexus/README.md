# Nexus Mods packaging

The second storefront. `Plugin\Thunderstore\` is the first — this folder is its counterpart,
and the two are **built from the same `bin\Release\` output, at the same time, by the same
`dotnet build -c Release`.**

## What lives here

| File | What it is |
|---|---|
| `pack-nexus.ps1` | Builds the upload archive. Called by the `PackNexus` target in `Plugin\LethalGargoyles.csproj`. |
| `fomod\info.xml` | Mod metadata Vortex reads. Carries an `@VERSION@` token the script substitutes. |
| `fomod\ModuleConfig.xml` | The guided installer. Source paths in it are relative to the zip root. |
| `MANUAL-INSTALL.txt` | Staged into the zip root as `READ ME FIRST - Manual Install.txt`. |
| `Nexus_Description.bbcode` | The mod page description. Nexus takes BBCode, not Markdown — paste this into the description box. |
| `Packages\` | Output. Git-ignored, same as Thunderstore's. |
| `obj\stage\` | Scratch. Wiped and rebuilt on every pack. Git-ignored. |

## Why the layout differs from Thunderstore

Thunderstore packages start at `plugins\` because r2modman and Gale know to graft that onto
BepInEx. **Nexus has no such convention** — Vortex and a player with a mouse both expect the
archive to mirror the game folder. So the Nexus zip's root holds `BepInEx\plugins\LethalGargoyles\`,
and dragging that one folder onto the game directory is a complete manual install.

`fomod\` sits beside it for Vortex and Mod Organizer 2. Managers that ignore it fall back to
the root layout and still land correctly — **that redundancy is deliberate, don't "tidy" the
plugin tree into a `Core\` folder** or the manual path breaks.

## The one thing that isn't automated

`Nexus_Description.bbcode` is **hand-written and does not regenerate from `README.md`.** The
README is Markdown with nested `<details>` blocks and a couple of hundred quoted voice lines;
a converter for it would be more fragile than the file it produces. When the README changes
in a way players care about — a new feature, a new voice-line category, a changed install
path — mirror it here. The `ship-release` skill carries this as a step.

## Dependencies are declared in a third place

Thunderstore has `thunderstore.toml` and `manifest.json`. Nexus has no dependency manifest at
all: the requirements are **prose**, in `Nexus_Description.bbcode` and `MANUAL-INSTALL.txt`.
Adding or dropping a dependency means editing both of those by hand, and nothing will warn
you if you forget.
