# Nexus Mods packaging

The second storefront. `Plugin\Thunderstore\` is the first — this folder is its counterpart,
and the two are **built from the same `bin\Release\` output, at the same time, by the same
`dotnet build -c Release`.**

## What lives here

| File | What it is |
|---|---|
| `pack-nexus.ps1` | Builds the upload archive. Called by the `PackNexus` target in `Plugin\LethalGargoyles.csproj`. |
| `fomod\info.xml` | Mod metadata Vortex reads. Carries an `@VERSION@` token the script substitutes. |
| `fomod\ModuleConfig.xml` | The guided installer. Source paths in it are relative to the zip root. **Its schema URL is a magic string — see below.** |
| `schema\XmlScript5.0.xsd` | Vendored from `Nexus-Mods/fomod-installer`. Not shipped in the zip; `pack-nexus.ps1` validates against it offline. |
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

## The FOMOD schema URL is a magic string, and getting it wrong fails silently

`ModuleConfig.xml` declares `xsi:noNamespaceSchemaLocation="http://qconsulting.ca/fo3/ModConfig5.0.xsd"`.
**That host is dead and nothing ever fetches it.** The string exists so Vortex can decide which
schema version to validate against — and it does that with a **raw text regex over the file**,
not by parsing the XML:

```
xsi:noNamespaceSchemaLocation="[^"]*((XmlScript)|(ModConfig))(.*?).xsd
```

`RegexOptions.Singleline`, and **no `IgnoreCase`**. On no match the version silently defaults to
`1.0`, a schema that predates `moduleImage` and `installSteps` — so a perfectly valid 5.0
installer gets rejected with a content-model error pointing at line 20 and **no mention of
versions anywhere**. Three ways to trip it, all silent:

* lowercase `modconfig` — this is what shipped in b28 and broke a real Vortex install
* a namespace prefix other than `xsi:`
* whitespace around the `=`

**Validating against the 5.0 schema does not catch any of this**, because pointing a validator
at 5.0 by hand skips the detection that is the actual bug. That is why `pack-nexus.ps1` runs
**both** checks: the real regex first to confirm the file resolves to 5.0, then the schema.
Verified against `Nexus-Mods/fomod-installer` → `XmlScriptType.cs`, 2026-08-16.

## The one thing that isn't automated

`Nexus_Description.bbcode` is **hand-written and does not regenerate from `README.md`.** The
README is Markdown with nested `<details>` blocks and a couple of hundred quoted voice lines;
a converter for it would be more fragile than the file it produces. When the README changes
in a way players care about — a new feature, a new voice-line category, a changed install
path — mirror it here. The `ship-release` skill carries this as a step.

## Dependencies are prose here, and NOTHING automates them

Thunderstore has `thunderstore.toml` and `manifest.json`. Nexus has no dependency manifest at
all, and it is worse than it first looks. Everything below was measured on 2026-08-16, not
assumed.

### The list itself

**FOUR required and three optional — get it from `thunderstore.toml`, not from a
BepInEx error message.** Corrected 2026-08-16 (b36) after b33 shipped a list of three:

| Required | Optional (soft deps, guarded) |
|---|---|
| BepInEx 5 · LethalLib · PathfindingLib · **Concentus** | Coroner · Employee Classes · Enhanced Monsters |

**Why three was a plausible-looking wrong answer, and the trap to avoid repeating:** the code
declares only `LethalLib` and `PathfindingLib` as hard `[BepInDependency]`, so those are the
only two BepInEx names-and-shames in the log. **Concentus is declared in `thunderstore.toml`
and `manifest.json` and nowhere in the code** — reading the source, or the error, undercounts
it. NVorbis is not on either list: it ships *inside* the package under `lib\NVorbis\`.

**Bundling the dependencies into this package was considered and REJECTED — don't revisit it
without a reason.** LethalLib and PathfindingLib are MIT, so redistribution is legally fine,
but that is the weakest part of the question. The real objections: a bundled copy **collides
with the one a player already has** from Thunderstore (duplicate plugin GUIDs, mismatched
assembly versions in `BepInEx\core`), it **freezes them on whatever version we shipped** and
breaks other mods needing a newer one, and **HookGenPatcher and MonoDetour live in BepInEx's
own `patchers\` and `core\` folders** — writing there from our installer is not our business.
Ecosystem etiquette matches: in BepInEx-land you *declare* dependencies, you don't ship them.
**`NVorbis` is not a counter-example** — it is a plain NuGet library with no plugin GUID that
lives inside our own folder, which is exactly the kind that IS fine to bundle.

> **Parked, and Mathew's call:** nothing in `Plugin\src` references Concentus — the only hits
> in the whole plugin tree are the two dependency declarations. It is probably vestigial from
> an earlier audio codec. It is listed as **required** because that is what the published
> package declares and because he confirmed it; dropping a declared dependency is a
> player-facing change, not a cleanup.

### Why nothing can automate it

* **None of the three libraries is on Nexus.** LethalLib, PathfindingLib and Concentus are
  Thunderstore-only; verified by enumerating all 181 Lethal Company mods through the Nexus
  API. The Nexus library shelf for this game is mostly stale third-party re-uploads — don't
  plan around one appearing.
* **LethalLib drags in three more packages, and they do NOT install to `plugins`.** Measured
  off the Thunderstore API and the package zips, 2026-08-16: LethalLib needs
  `Evaisa-HookGenPatcher` (the thing people call MMHOOK) and `MonoDetour-MonoDetour_BepInEx_5`,
  which itself needs `MonoDetour-MonoDetour`. **HookGenPatcher unpacks to `BepInEx\patchers\`
  and MonoDetour to `BepInEx\patchers\` and `BepInEx\core\`** — so the obvious instruction,
  "drop the plugin folder into `BepInEx\plugins`", silently breaks all three. b36 shipped
  exactly that wording. **The install text now steers people to a Thunderstore mod manager
  (r2modman or Gale) for the library stack**, which resolves the whole tree and puts each
  piece in the right folder, with a careful manual route as the fallback.
* **Vortex does not read a mod page's Requirements tab.** Not for Nexus-hosted requirements,
  not for off-site ones (Nexus-Mods/Vortex issue 16360). Its real dependency mechanism is
  manual per-user rules, or a Collection. So no Vortex user is ever prompted. **The
  nexusmods.com website is different — it DOES prompt on download**, which is why the tab is
  still worth filling in. Off-site entries go in the editor's *"Other required resources"*
  box (name, URL, notes) and render under an **Off-site requirements** heading.
* **Two automation routes exist and neither fits.** The `.vdeps` file read by the third-party
  *Mod Dependency Fulfiller* extension only resolves dependencies **downloaded through
  Nexus** — ours are not. A **Vortex Collection** can automate an external download (Direct
  Download, or a guided Browse-a-website step), but that is a separate thing to publish and
  maintain, not a property of this package.
* **The plugin cannot warn either.** `evaisa.lethallib` and `Zaggy1024.PathfindingLib` are
  hard `[BepInDependency]` entries, so BepInEx refuses to load the assembly and `Awake` never
  runs. Its own `missing dependencies` log line is the only failure signal, and a player who
  is not reading the log just sees a mod that does nothing.

So the requirements live in **three hand-maintained copies**, and the FOMOD one is the only
one a Vortex user is guaranteed to see:

1. `fomod\ModuleConfig.xml` — the first install step, a read-only notice page
2. `Nexus_Description.bbcode` — the mod page
3. `MANUAL-INSTALL.txt` — the zip's readme

Change a dependency and all three move together, plus `thunderstore.toml` and
`manifest.json`. Nothing checks any of it.

## What Vortex shows for the mod is NOT in your control

`fomod\info.xml` does not set the mod's displayed name or version. Vortex 1.x read `<Name>`
from it; **Vortex 2.x has that extractor commented out** (`installer_fomod_shared/index.ts`,
*"not worth the hassle"*), and no version of it ever read `<Version>`. For a locally
installed zip the name falls through to the **archive filename** and the version is simply
blank, with a "no source assigned" warning — that is the expected local-install state, not a
defect in the package.

For real users it fixes itself: **Mod Manager Download** stamps `source`, `logicalFileName`
and `fileVersion` from the Nexus API, so what actually matters is that **the file's Name and
Version fields on the Nexus upload page are filled in correctly.** `info.xml` is kept because
it is spec-conformant and other managers may read it, not because Vortex does.

**Nexus says so themselves**, in the *About Fomod* page of the Nexus Mods app docs: the
metadata in `info.xml` *"is redundant to what Nexus Mods reports through the api"*, so
*"Vortex at least largely ignores this file."*

> **DO NOT trust Nexus's own installer guide on this point.** The user-facing *How to create
> mod installers* wiki page says `info.xml` and `ModuleConfig.xml` *"both contain information
> Vortex will load during the installation process."* **That sentence is wrong** — the code
> and the *About Fomod* page agree against it. A session reading only that guide would go
> looking for a bug in `info.xml` that does not exist. Two closed Vortex issues (#11776,
> #4420) asked for the behaviour the guide describes.

Two recovery paths exist once the mod IS published, both driven by an MD5 lookup against the
Nexus metadata server, and both worth knowing before anyone re-fights this: **Query Info** on
a download, and **Fix missing IDs** on an installed mod. They can retro-fit real metadata
onto a manually-added copy of a published file. The Source dropdown that clears the warning
is in the **right-hand mod detail pane** — its table column is hidden by default
(`isDefaultVisible: false`), which is why it is not obvious.

**Do not add `vortex_override_instructions.json` to force it.** It would work, and its mere
presence also changes the installer's stop-pattern handling and nulls `pluginPath` — it is
internal Vortex machinery, not a mod-author contract.
