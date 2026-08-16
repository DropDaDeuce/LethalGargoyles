---
name: changelog-writer
description: Use ANY time a line is going into CHANGELOG.md - after finishing a batch that changes player-visible behaviour, adding a new config setting, fixing a bug, or writing up a release. Also use when asked to rewrite, tidy or re-voice an existing changelog entry. Carries the house voice (dev intern explaining the change to a casual player, technical detail kept but placed second) and the punctuation rules that keep it from reading like AI output.
---

# Writing the changelog

`CHANGELOG.md` is read by players in a mod manager, mid-scroll, deciding whether to hit update. Most of them do not code. Some of them do, and those are the ones who file the useful bug reports, so the detail stays in. Both audiences get served by the same bullet: **plain sentence first, the technical reason second.**

This is not a commit log and it is not marketing. Write it the way a dev intern would explain their week to the players: they know exactly what they changed, they are not showing off about it, and they are not hiding it either.

## Where the entry goes

* **`## Unreleased` at the top of the file is the accumulator.** Add to it. Do not mint a new version heading - `ship-release` renames `Unreleased` when the version actually ships.
* **Reuse an existing bold group header before inventing one.** The live set is `**AI Improvements:**`, `**Bug Fixes:**`, `**New Features:**`, `**Changes:**`, `**Tuning:**`, `**New Settings:**`, `**Config File Layout:**`, `**Game Compatibility:**`, `**Documentation:**`. A one-bullet section nobody has seen before is usually a bullet that belonged in an existing section.
* **Not everything belongs here.** A refactor a player cannot observe, a doc fix, a board entry, a build-system change: none of those go in `CHANGELOG.md`. They go in `Docs\Archive\Done\Done_YYYY-MM.md`. The test is literal: **could a player notice this without reading the source?** If no, it is not a changelog line.
* `CHANGELOG.md` is a single-writer shared doc under the board protocol. Claim it before editing it.

## The shape of a bullet

1. **Sentence one says what the player will see, hear, or be able to do.** No jargon, no class names, no method names.
2. **Sentence two (optional) says why it was happening.** This is where the technical reader gets paid. Keep it to the mechanism, not the fix's implementation.
3. **Sentence three (optional) is the consequence they should expect.** "Expect it to feel more present." "Your existing config keeps the old value."

Bold the whole first sentence only when the bullet is the headline of its section. Two or three bolded leads in a release is right; bolding every bullet means nothing is emphasised.

```
- **The Gargoyle actually roams again while it is searching for you.** Its search was
  restarting itself every frame, which meant it never got far enough to pick somewhere
  to go, so it tended to loiter near where it spawned. Expect it to feel considerably
  more present.
```

That is the target: a player understands the first sentence, a modder understands the second, and nobody had to read code.

## Punctuation and phrasing rules

These exist because the default register of a language model is instantly recognisable, and a changelog that reads like it was generated gets trusted less than one that reads like a person wrote it.

**Banned outright:**

* **Em dashes and en dashes as sentence punctuation.** Recast instead: split into two sentences, use a comma, or put the aside in parentheses. A hyphen inside a range or a compound word is fine.
* **Curly quotes and curly apostrophes.** Straight only.
* **Emoji**, decorative bullets, and arrows. The one arrow that is allowed is the config convention `Section > Setting Name`.
* **The chiasmus.** "It's not a performance problem, it's a correctness problem." "Less a fix, more a rewrite." Say the one true thing.
* **The rhetorical triad.** "Faster, quieter, and far less likely to get stuck." Pick the one that matters.
* **LLM filler vocabulary:** seamlessly, robust, leverage, utilize, delve, comprehensive, streamlined, enhanced experience, under the hood as a section header, "it's worth noting", "importantly", "significantly improved" with nothing measured behind it.
* **Announcing the writing.** "This update brings a number of improvements." Just list them.

**Use sparingly:**

* **Semicolons.** At most one per bullet, and only when the two halves are genuinely one thought. Two semicolons in a bullet is a paragraph pretending to be a list item.
* **Bold inside a sentence.** For a config path or a hard warning, not for emphasis-by-default.

**Always:**

* **Present tense for behaviour, past tense for the bug.** "The Gargoyle no longer gets stuck. It was asking for a route it never requested."
* **Second person for the player.** "you", "your config", "your existing values". Not "the user".
* **Name the setting exactly as it appears in the cfg**, with its default in backticks: `` `General > Min Taunt` (default `30`) ``.

## Before and after

| Reads like AI | Reads like a person |
|---|---|
| Significantly enhanced the pathfinding subsystem for a more robust navigation experience. | The Gargoyle gets stuck on corners and doorways a lot less. |
| Refactored `HandleStealthyPursuitState` to invalidate `pathDelayTimer` on the rising edge of `isSeen`. | It reacts to being spotted straight away instead of after up to three seconds. |
| Fixed a bug where the audio decode path did not correctly handle multi-channel input - resulting in truncated playback. | Fixed stereo voice lines being cut off halfway through. The decoder was reading them as if they were mono, so it stopped at the halfway mark. |
| Improved config organization for better usability. | The eight hundred per-clip voice-line toggles moved to the bottom of the config file. They used to be first, which pushed every setting you might actually want past line 900. |

## Things that must be said when they are true

Leaving these out is how a changelog becomes untrustworthy:

* **Unverified in game.** Every session in this project can build and none can play. If a change compiled but nobody has run it, say so plainly: "Not yet verified in a live round."
* **Existing config files do not pick up new defaults.** BepInEx keeps what is on disk. If a default moved, say that the player has to delete the line or set it themselves.
* **A behaviour change that will look like a bug.** If the Gargoyle now spreads across the crew instead of dogpiling one player, say it, or the first report will be "the new version broke targeting".
* **Off by default.** New settings that change how the enemy behaves ship off. Say `(**off by default**)` right in the bullet.
* **Who to thank.** Assets, renders, ideas from other people get a name.

## Before you call the entry done

* Read it out loud. If you would not say it to someone in voice chat, rewrite it.
* Search the new text for `—`, `–`, `’`, and `“`. There should be zero hits.
* Check that every bullet passes the player-notices test. Delete the ones that do not and move them to `Done_YYYY-MM.md`.
* Check the group headers against the existing file. New header means you probably miscategorised.
