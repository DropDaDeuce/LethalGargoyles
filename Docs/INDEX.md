# Docs Index

**Purpose: tell a session, in one read, which docs are LIVE work and which are closed reference — before it spends context opening the wrong one.** Created 2026-08-15 (board b1).

**Maintenance rule:** when a doc's status changes, update its row here in the same batch. A row that lies is worse than no row.

**When a plan doc is done, does it MOVE?** Usually **no — reclassify it here instead of relocating it.** Other docs, Done entries and board-history rows point at plans by path, and moving one breaks those pointers — which is the exact failure class this index exists to kill. Move a plan to `Docs\Plans\Archive\` only when it is genuinely DEAD: it tells you nothing you cannot get from the code, the Done entry, or `CLAUDE.md`. A closed design that is still consulted is not dead — mark it CLOSED and leave it where it is. If you do move one, sweep its inbound references in the same batch.

---

## Start here

| File | What it is |
|---|---|
| `CLAUDE.md` (repo root) | **Law.** Read first, every session. Architecture, hard rules, do-not-relearn traps. |
| `Docs\Session_Board.md` | **The multi-session rulebook AND the live lock table.** Read before your first file read/edit. |
| `ToDo.md` (repo root) | The live backlog — **OPEN ITEMS ONLY**. |
| `CHANGELOG.md` (repo root) | Per-release player-facing record. Newest release at the top. |
| `README.md` (repo root) | **Player-facing** — features, the full voice-line list, install and custom-voice-line instructions, credits. Not architecture; allowed to lag the code, and known to in two places (see `CLAUDE.md`). |

## History (nothing here is open work)

| File | Covers |
|---|---|
| `Docs\Archive\Done\Done_YYYY-MM.md` | The dated Done log, one file per month, 2026-08 onward. **New entries append to the current month's file, at the top.** |
| `Docs\Archive\Session_Board_History.md` | Released claim rows moved off the board verbatim, so the board stays under one read. |

## Plans

| File | Status |
|---|---|
| `Docs\Plans\README.md` | What belongs in this folder. |

*(No plan docs yet. When you write one, add its row here in the same batch, with a status of LIVE, CLOSED, or DEAD.)*

## Superseded sources — do not add to these

| File | Why |
|---|---|
| `Gargoyle To Do.txt` (repo root) | Folded into `ToDo.md` 2026-08-15. Kept only until Mathew rules on deleting it. |
| `README.md` → **To Do** section | Folded into `ToDo.md` 2026-08-15, and **re-synced from it by b2** — the truncated `- Add ` bullet is gone and the list now matches the backlog's player-facing items. It is a mirror for players, not a source: change `ToDo.md`, then update it here if players would care. |

## Not docs, but you will look for them

| Path | What |
|---|---|
| `AssetSources\` | Audacity `.aup3` voice-line sources, Blender files, images, `VoiceLines.xlsx` (the script of record for what each line says), and the Audacity pipe export scripts. |
| `Plugin\Thunderstore\Voice Lines\` | The default OGG clips that ship in the package, in their category folders. |
| `UnityProject\AssetBundles\StandaloneWindows\gargoyleassets` | The committed asset bundle. Built by hand in the Unity Editor — nothing in this repo builds it. |
| `.claude\skills\` | **Four procedure skills** — `voice-line`, `game-update`, `ship-release`, `playtest` — plus the two model-routing lanes `deep-work` and `light-touch`. The procedure skills carry checklists that would otherwise be re-derived; the routing lanes only move the model dial. `CLAUDE.md` → *Procedure Skills* says when each fires. |
| `.claude\skills\game-update\reflect.cs` | Runnable: `dotnet run reflect.cs <VanillaTypeName>` prints the real fields/methods of any Lethal Company type off the installed assembly. Use it instead of guessing when the game changes an API. |
| `.claude\agents\` | `Explore` (cheap wide searches) and `deep-debug` (unknown-cause defects). Standing authorization to use them is in `CLAUDE.md` → *Subagents*. |
| `.gitignore` × 4, `.gitattributes` × 3 | Layered, all tracked: root (added 2026-08-15) plus `Plugin\`, `UnityProject\`, `AssetSources\`. The subtree files are upstream templates and stay authoritative for their own trees. Rules and traps are in `CLAUDE.md` → **Git**. |
