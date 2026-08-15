---
name: voice-line
description: Use for anything about the Gargoyle's voice lines - adding or replacing clips, adding a whole new taunt category, why a line isn't playing, why it plays on the host but not a client, clip size or format limits, SteamID taunts, the per-clip enable toggles, or the Custom Voice Lines folder. Covers both the shipped Voice Lines folder and player-added custom lines.
---

# Voice lines

The audio system is the most surprising subsystem in the mod, and almost all of its failure modes are **silent**. Read the relevant half below before changing anything.

## The three rules that cause most "it isn't talking" reports

1. **OGG only.** `GetMP3Files` is a misleading name — it returns whatever files it finds — but only Vorbis survives the pipeline, because clients decode with `NVorbis.VorbisReader`.
2. **500 KB (512000 bytes) hard cap per clip.** Over it, the host logs an error, skips the send, **and `break`s the loop — so one oversized file silently costs the rest of its category.** This is the single most damaging mistake available here.
3. **Clips are addressed BY NAME across the wire.** `TauntClientRpc(clipName, clipType)` sends a string; the client looks it up in its own list and, if it isn't there, **does nothing at all** with no log outside a DEBUG build. A client that missed a transfer looks exactly like "the gargoyle just isn't talking."

## Adding a clip to an EXISTING category

No code change. Drop the OGG in the right folder and it is picked up at load:

* **Shipped defaults:** `Plugin\Thunderstore\Voice Lines\<category folder>\`
* **Player-added:** `<game root>\Lethal Gargoyles\Custom Voice Lines\<category folder>\` — note the `Lethal Gargoyles` level, which the README got wrong for years.

Naming: for **Activity, Enemy, Prior Death and EmployeeClass** the filename maps to a specific game event, so a variant must keep the base name and add a suffix (`taunt_priordeath_Abandoned2.ogg`) — the mod picks randomly among all files sharing the base. Other categories accept any filename. **SteamID taunts** are `<SteamID>[anything].ogg` in `Taunt - SteamIDs` and fire at a deliberate 2.5% chance.

A per-clip `Enable <clipname>` toggle is minted automatically under `Audio.<Category>` — `InitializeAudioClipConfigs` is generic, so it needs no edit ever.

**No asset-bundle rebuild is needed for any voice-line work.** Clips are files on disk, not bundle assets.

## Adding a NEW category — SEVEN places, and missing one fails quietly

Verified against the source 2026-08-15. **Navigate by method name, not line number.** The first six are required; the seventh is a judgment call.

| # | File | Where | What to add |
|---|---|---|---|
| 1 | `LethalGargoyles.cs` | `GetDefaultAudioClipFilePaths` | `{ "YourCat", [] }` in the dictionary initialiser |
| 2 | `LethalGargoyles.cs` | `Awake` | a `Directory.CreateDirectory` line for its custom-lines subfolder |
| 3 | `AudioManager.cs` | `GetMP3Files` | a `case` mapping the category name to its folder name |
| 4 | `AudioManager.cs` | `GetClipListByCategory` | a `case` mapping the category to its list |
| 5 | `AudioManager.cs` | fields + `OnNetworkDespawn` + `LogClipCounts` | the `static List<AudioClip>`, **its `.Clear()`**, and its count line |
| 6 | `LethalGargoylesAI.cs` | `TauntClientRpc` | a `clipType` `case` routing to the list |
| 7 | **`Scrap\GargoyleStatue.cs`** | its **own** `TauntClientRpc` | **the same `clipType` switch, duplicated** |

**#7 is the one people miss.** The Gargoyle Statue scrap carries a second, parallel copy of the `clipType` switch. Miss it and the monster taunts correctly while the scrap silently plays nothing — a difference nobody would think to test.

**Optional:** `LethalGargoylesAI.OtherTaunt` has a third switch covering only `general`/`aggro`/`death`/`enemy`. Add a case there **only** if the new category should be reachable through that generic path.

**Skip #5's `.Clear()` and the lists double on the next round** — they are static and survive a lobby.

Soft-dep categories are conditional: `Class` is only added when EmployeeClasses is loaded, `Coroner` clips only when Coroner is. Follow that pattern rather than adding an unconditional entry.

## Diagnosing a silent gargoyle

Ask **on which machine** first — that splits the problem in half.

* **Silent on one client, fine on the host** → the transfer, not the AI. Check that client's `LogOutput.log` for `Clip Loaded:` lines and the host's for `Sent Clip(...)`. A missing name is a clip that never arrived. Look for the 500 KB `LogError`.
* **Silent everywhere** → the AI never fired the taunt, or the category list is empty. `LogClipCounts` prints per-category counts, but it is `[Conditional("DEBUG")]` — you need a debug build to see it.
* **Was fine, now silent after a lobby change** → check the `OnNetworkDespawn` clears.

The transfer itself: host loads every enabled clip, pushes raw bytes per client over `CustomMessagingManager` (named message `SendLGAudioClip`, `ReliableFragmentedSequenced`), one at a time behind a ready-handshake with a 5s timeout — after which **it sends anyway**, so a slow client can miss clips without any error.

Use the `Explore` agent for "where is this category referenced" sweeps rather than reading `LethalGargoylesAI.cs` whole.
