---
name: playtest
description: Use when a change is built and needs to be tried in game, when writing the numbered steps for Mathew to run a test, or when reading a LogOutput.log after he has played. Covers what to tell him to watch or listen for, which log lines are load-bearing, and why a Release build hides most of the mod's tracing. Use it any time you are about to say "please test this".
---

# Playtesting — the only real feedback loop

**No session can run the game.** Every behavioural claim this project makes is either measured from a log or reported by Mathew. That makes the quality of the test instruction the limiting factor, not the quality of the code.

## Before you ask for a test

1. **Build first.** A debug build IS the deploy to the Gale `Dev` profile — there is no separate copy step. Confirm three things in the output rather than assuming: the build is green, `DropDaDeuce.LethalGargoyles_original.dll` sits beside the patched DLL (proof the netcode patcher ran), and the copy into the `-DEV` profile happened.
2. **Ask whether the asset bundle matters.** A C#-only change never needs a rebuild. A prefab, animator, material, audio-asset or mixer change does — and **a stale bundle is invisible at build time and only shows up in game.** If the change needs one, that is a Unity Editor step for Mathew and it goes FIRST in his list.
3. **Decide whether one machine is enough.** Anything touching audio transfer, RPCs, or state that the host owns needs **a second player**, because the host path and the client path are different code. Say so explicitly — a host-only test that passes proves very little about the client half.

## Writing the instruction

Give a clean numbered list **in chat**, plain language, only what HE does, one or two sentences each. Never point him at a doc as his instructions.

**State what "wrong" looks like, not just what to do.** "Check the teleport works" is a bad instruction; "it should come out on the *far* side of the entrance it walked into — same side, or standing still at the door, means the pairing is backwards" is one he can actually act on. He can only report a difference he knows to look for.

Cover, in this order:
* Anything he must do in Unity/Blender/Audacity first.
* How to get into the situation (which moon, does he need a second player, does he need to be alone/hurt/inside).
* **What to watch or listen for**, and what wrong looks like.
* Whether to send the log afterwards, and from which machine.

## Reading `LogOutput.log`

It is in the Gale profile's `BepInEx\` folder — for the dev profile, beside the `DropDaDeuce.LethalGargoyles-DEV` plugin folder. **Ask which machine's log it is** before drawing conclusions; host and client logs mean different things.

Load-bearing lines that fire in **any** build:

| Line | Means |
|---|---|
| `Plugin ... is loaded!` | the plugin itself started — its absence is a load failure, not a behaviour bug |
| `Checking Soft Dependencies:` | which of Coroner / EmployeeClasses / EnhancedMonsters are live this run |
| `Sent Clip(<name>) to ClientID(<n>)` | host side, one clip transferred |
| `Clip Loaded: <name>` | client side, one clip decoded and ready |
| `Sending Clip(...) failed. Max clip size...` | the 500 KB cap — **and the rest of that category was skipped** |
| `Gargoyle Statue scrap is registered.` | the scrap registered |
| `Failed to load custom assets.` | the asset bundle did not load — everything else is moot |

**Most of the mod's tracing is `[Conditional("DEBUG")]`** — `LogIfDebugBuild`, the per-tick AI state lines, `LogIfSlow`, `LogClipCounts`. In a Release build they do not exist at all. If you need that detail, say plainly that he has to run a debug build, rather than reasoning around the gap.

## After the test

* **It worked** → remove the item from `ToDo.md` (do not tick it), and record what was actually verified in the month's `Done_YYYY-MM.md`. "Compiled" and "verified in a round" are different claims — keep them distinct.
* **It didn't** → if the cause is obvious, fix it. If not, hand the symptom **and the evidence gathered so far** to the `deep-debug` agent rather than guessing on the main thread.
* **He couldn't tell** → that is a defect in the instruction, not in his testing. Rewrite it with a sharper observable.
