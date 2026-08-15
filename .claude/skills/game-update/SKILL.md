---
name: game-update
description: Use when the build suddenly fails on a GAME type or member (CS1061 "does not contain a definition for", CS0246 "type or namespace not found" on a vanilla class), when Lethal Company has patched and the mod needs re-checking, or when a mod feature stops working after a game update with no code change on our side. Reads the real API off the installed game assembly instead of guessing at it. Do NOT use for compile errors in our own code, or for a defect that reproduces on an unchanged game version.
---

# Game update / API drift

Lethal Company is a moving target. `Assembly-CSharp.dll` changes under the mod without warning, and the first symptom is almost always a build that was green last month and isn't now.

**A build that fails on a vanilla type is a GAME UPDATE, not a regression in whatever you just edited.** Establish that before you touch anything.

## 1. Get a clean baseline first

Build **before** making any change, so a failure is attributable:

```
cd Plugin && dotnet build -v minimal
```

Count the errors. **C# reports every CS1061/CS0246 in one pass**, so the error count is a real measure of how much API surface moved — one error means exactly one call site broke, and the rest of the mod still matches the game.

If the failure is instead `$(ManagedDirectory)` resolving to nothing, the game isn't installed — check `<game>\Lethal Company_Data\Managed\` exists before anything else. Steam leaves the mod-created folders (`Lethal Gargoyles\`, `doorstop_config.ini`) behind on uninstall, so the game *folder* existing proves nothing.

## 2. Read the real API — never guess, and never grep

**Grepping the DLL for a member name is actively misleading.** `exitPoint` still appears in the binary's string heap after the field was deleted, because `exitPointDoesntExist` shares the prefix. A grep "confirms" a member that is gone.

Use `reflect.cs` in this skill folder. It loads the installed assembly with a `MetadataLoadContext` — nothing executes, the game doesn't need to run, and it lists **private members too**, so the publicizer can never confuse the answer:

```
dotnet run reflect.cs EntranceTeleport
```

Edit the `managed` path at the top if the game moved. Pass any vanilla type name. It prints fields with their real types, plus methods.

## 3. Work out the replacement from evidence, not from the name

Removed members usually mean the game restructured, not renamed. Ask:

* **What is the new shape?** A `Transform` field replaced by a reference to a paired object plus a resolver method is the common pattern (`EntranceTeleport.exitPoint` → `exitScript` + `entrancePoint` + `bool FindExitPoint()`).
* **What does the ecosystem use?** `grep -oaE "memberName" Plugin/dlls/PathfindingLib.dll` — the soft-dep and library DLLs in `Plugin\dlls\` are built against recent game versions, so what they reference is a strong signal about what is current.
* **Does the new API report failure?** If a resolver returns `bool`, call it and honour the result. That is usually a free null guard the old field access never had.

## 4. Fix, rebuild, and be honest about what you verified

A green build proves the **shapes** match. It does not prove behaviour. Say that plainly, and park an in-game check in `ToDo.md` describing **what "wrong" would look like**, not just "please test" — see the `playtest` skill.

Also check, because these drift silently on a game update:

* **The netcode patcher's Unity version** — `netcode-patch -uv 2022.3.62` in the csproj's `NetcodePatch` target, against the game's actual version (`(Get-Item "<game>\Lethal Company.exe").VersionInfo.ProductVersion`).
* **The asset bundle's Unity version** vs the game's. Read both from their file headers. They are allowed to differ by a patch letter — they already do — but this is the first suspect if models, animations or audio stop loading while the DLL clearly loaded.

Record the finding in `CLAUDE.md` → *Current State* and the month's `Done_YYYY-MM.md`, including **which call sites broke**, so the next update starts from a known surface.
