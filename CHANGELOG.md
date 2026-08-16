## v0.8.0 - The Wandering Gargoyle

**AI Improvements:**
- **The Gargoyle actually roams while he is searching for you.** His search was restarting itself every frame, so he never got far enough to pick anywhere to go and tended to loiter near where he spawned, only noticing you if you walked close to him. Expect him to feel considerably more present.
- **Several Gargoyles now split up across the crew instead of all stalking the same person.** The code that shared targets out never recorded who had picked whom, so the balancing it was supposed to do could not run. If you are used to the whole pack converging on one player, this will feel like a different mod. It is the intended behaviour finally working.
- He can break line of sight again. When further than the Idle Distance from you, his move to get out of sight never once checked whether you could see the place he was heading, only whether he could walk there. He now refuses spots you can currently see, and refuses spots he is already standing on.
- When he cannot find anywhere to hide, he backs away and looks again rather than walking at you. His old answer to finding no cover was, genuinely, to path towards the person he was hiding from, arrive, and stand there in the open. He now retreats away from you and searches again from further out, repeating as needed. He gives up and charges only if you keep him in sight for five seconds while he fails to cover eight metres of ground, which catches him being properly stuck, jammed on a corner, or pacing between two equally exposed spots. A real run for cover clears eight metres in about a second, so escaping properly never trips it. Set `General > Cornered Aggression Delay` to `0` if you would rather he never charged. Not yet verified in a live round.
- He searches a much wider area for somewhere to hide. He used to properly consider only the couple of dozen spots nearest to you, which are the spots most likely to be in your view, so in an open area he often found nothing at all. He now checks up to a hundred and sixty places for whether you can see them, then works out routes only to the first handful that are genuinely hidden, so the wider search does not cost him any more time. He still prefers the closest hidden spot, so he stays uncomfortably near.
- He circles both ways again. He compares a route to your left against a route to your right, and both measurements were being reset before the comparison, so the two always tied and he took the same side every time.
- Smoother movement and much lighter network traffic in multiplayer. He was thinking every frame instead of five times a second, and telling every other player his position every frame instead of every metre. He also brakes as he arrives somewhere now, instead of overshooting and shuffling.
- He no longer gets stuck asking for a route he never actually requested, and no longer interrupts his own route calculation before it can finish. Both showed up as him freezing or drifting toward somewhere you had already left.
- He reacts to being spotted straight away instead of after up to three seconds.

**Performance:**
- Large frame-rate improvement, most noticeable with more than one Gargoyle alive. While sneaking or hiding, each of them was running a full route calculation plus line-of-sight checks against every navigation point in the interior, every single frame. With four alive that is well over a hundred route solves per frame. They now sort by plain distance first and only pay for the expensive checks on a handful of candidates.
- Another large improvement while he is searching. The same bug that stopped him roaming was also making him rebuild a list of every navigation point on the map, sixty times a second, per Gargoyle.

**Bug Fixes:**
- **Fixed the shove doing nothing at all to anyone except the host.** He was moving you and dealing the shove damage on the host's machine rather than on yours, and Lethal Company only lets your own machine push you around or hurt you, so for everyone else the shove landed nowhere. If you play with friends, expect to start getting thrown at railings and off ledges. Confirmed working in a live round.
- Fixed his footsteps playing at one flat volume for everyone except the host. How loud his steps are is supposed to depend on what he is doing, and none of that ever reached the other players. You will notice it most on the shove, which is meant to be nearly silent, and you also get the quiet while he stands watching you. The same fix makes him louder for those players while he is searching or chasing, because that is the volume he was always meant to have and they were never hearing it. Not yet verified in a live round.
- Fixed him being unable to hide anywhere near stairs or between floors. His search for cover only looked within about a metre of your own height, so on a staircase he found nothing at all, and his response to finding nothing was to walk straight at you. He now looks about ten metres up and down.
- Fixed him getting stuck shuffling back and forth in one spot. Two separate causes: when he could not find anywhere good to go he was told to walk to where he was already standing, which he then recalculated to the same answer forever, and his roaming search was left running when he switched to stalking you, so two parts of the AI were steering him at once.
- **Fixed him freezing in place while hunting for you, sometimes for half a minute at a time.** Two parts of the mod were steering him at once whenever he lost track of you: the roaming search, and the leftover route to wherever he had been heading before he lost you. Each one kept cancelling the other's route, so he recalculated constantly and never actually followed one. He now hands over cleanly, and only one thing steers him at a time. Not yet verified in a live round.
- **Fixed him freezing on the spot whenever you stood at almost exactly `General > Idle Distance` from him.** He is supposed to stop moving at that range, but the switch was a single hard line, so one step of yours flipped him between standing still and stalking, over and over. In a recorded round he held the same position for thirteen straight AI updates while a player wandered around him from ten metres out to twenty. He now settles at the Idle Distance and does not get moving again until you have opened up about a fifth further, so drifting across the line no longer makes him twitch. Not yet verified in a live round.
- Fixed the same problem at `General > Awareness`. A player sitting right on the edge of what he can notice was being forgotten and re-noticed on alternate updates, and each round trip reset his state and sent him back to searching. He now notices you at the Awareness distance and does not lose track of you until you are roughly fifteen percent past it.
- Fixed him walking to a hiding place, standing there in plain view, and then charging you. He works out which spots you cannot see and reuses that list for a while, so if you stood still and simply turned around, every spot on it was still marked hidden while being in full view. He would arrive at one, re-pick that same spot because it was still the nearest, and stand there until the cornered timer gave up and sent him at you. He now re-checks a hiding place before committing to it and discards the ones that have gone stale.
- Fixed stereo voice lines being cut off halfway through for everyone except the host. Clients decode the audio themselves and were reading stereo clips as if they were mono, so playback stopped at the halfway mark. Only one shipped line is stereo, but this also affects any custom line you add.
- Fixed the Gargoyle Statue scrap being silent for anyone who is not the host. Activating it was gated behind a host-only check, so a client holding one heard nothing at all.
- Fixed every Gargoyle Statue in your inventory talking at once. Only the one in your hand should be making noise. The game runs an update on every item you are carrying, not just the held one, and the statue was not checking which it was.
- Fixed one Gargoyle dying stopping all the survivors from opening and closing doors. The door behaviour was tied to a single shared marker rather than to each Gargoyle.
- Fixed a player becoming permanently un-shovable for the rest of the session. When a Gargoyle despawned it left its half-finished push behind in shared state, and nothing ever cleaned it up.

- Fixed `Diagnostics > Default Level` doing nothing at all. It was described as the level used by any category you had not set yourself, but every category is always written into the config file, so there was never a category for it to apply to. It is now a floor: raise it and everything logs at least that much detail, which is what you want when someone asks you for a log. A category you have set higher keeps its own setting.

**Tuning:**
- He talks less by default. `General > Min Taunt` is now `30` and `General > Max Taunt` is now `60`, up from `15` and `45`. If you already have a config file your existing values are kept. Delete those two lines, or set them yourself, to pick up the new defaults.

**Config File Layout:**
- The per-clip voice-line toggles have moved to the bottom of the config file. They were the first thing in it and there are over eight hundred of them, which pushed every setting you might actually want to change past line 900, `Min Taunt` included. The section is now named `Voice Lines.<Category>` instead of `Audio.<Category>`, purely so it sorts last. Any clip you had switched off stays switched off. The move happens once, the first time you launch after updating.

**New Settings:**
- `General > SteamID Taunt Cooldown` (default `90`). The shortest gap between his personal lines, the ones recorded for a specific player's Steam ID. This was fixed at 90 seconds with no way to change it. The default is the old behaviour, so nothing changes unless you touch it.
- `General > Cornered Aggression Delay` (default `5`). How many seconds you have to keep him in sight, while he fails to cover eight metres of ground, before he gives up on hiding and comes for you instead. Set it to `0` to switch this off and let him keep looking.
- `Pathfinding > Follow Players Through Exits` (**off by default**). Lets him use fire exits and the main entrance, so he can follow you between the facility and the surface. He has never been able to do this, so it stays off until you choose it.
- `Pathfinding > Use Mineshaft Elevator` (**off by default**). Lets him call and ride the mineshaft elevator instead of being stuck on the floor he spawned on.
- `Pathfinding > Agent Radius` (default `1.25`, which is the width he already used). How much room his steering thinks he needs. Lower it to `0.5` if he jams walking on the spot at corners and doorways, or refuses to squeeze past railings.
- A new `Diagnostics` section, for when you need to report a bug. Set `Enabled` to `true`, then turn up whichever part you care about: `Lifecycle`, `StateMachine`, `Targeting`, `Movement`, `Perception`, `Combat`, `Taunt`, `Audio`, `Netcode`, `Config` or `Scrap`. Each takes `Off`, `Error`, `Warn`, `Info`, `Debug` or `Trace` and starts at `Warn`. This works in the normal release build, so you no longer need a special version of the mod to produce a useful log.

**Documentation:**
- Corrected the descriptions on `General > Min Taunt` and `General > Max Taunt`. Both claimed "other types of taunts will be half this number", which was never true. General, enemy-warning and aggro taunts all use the full range. They now also say what they do not cover: the bark the moment he charges you, which is always immediate on purpose.

**Game Compatibility:**
- Fixed the mod failing to build against the current version of Lethal Company. The game removed the property he used to find the far side of an entrance when teleporting, so he now resolves the paired entrance the way the game itself does. **Not yet verified in a live round.**

## v0.7.0 - The Gargoyle Is Back!

**AI Improvements:**
- Integrated PathfindingLib for improved pathfinding and navigation.
- Improved the Gargoyle's navigation so it can reach players more reliably and gets stuck less often.
- Tweaked behavior logic (searching, sneaking, chasing, idling, and pushing) to make the AI feel more consistent and responsive.
- General performance improvements when multiple Gargoyles are active (less repeated heavy logic per tick).

**Game Parity:**
- Updated the asset bundle to Unity 2022.3.62f3.
- Updated netcode to be compatible with v73.

**Icon**
- Thank you Purple for the Gargoyle render used in the icon!

### Documentation
- Added/updated the changelog format and entries.

## v0.6.1 - Compatibility & Fixes

**New Features:**

- **Enhanced Monsters Compatibility:**
    - Added compatibility with the Enhanced Monsters mod. WIP.

**Changes:**

**Bug Fixes:**
- Modified the animator controller for the die animation of the gargoyle.
- Fixed an issue with the gargoyle statue scrap not being activated properly on servers.

**Notes:**

- Enhanced Monsters compatibility is currently under development by the mod author.

## v0.6.0 - The Gargoyle's Treasure

**New Features:**

- **Gargoyle Statue Scrap:**
    - Added a new gargoyle statue scrap item that can be found in the game.
    - The Eyeless Dog can hear the gargoyle statue scrap.
    - The gargoyle statue scrap has its own voice lines!
- **Sound Effects:**
    - The gargoyle now has footstep sound effects.

**Changes:**

- Updated various assets for improved visuals and sound effects.

## v0.5.0 - The Talkative Gargoyle

**New Features:**

- **Activity-Based Taunts:**
    - The gargoyle now reacts to various player activities with unique taunts.
    - Added 21 new voice lines for activity-based taunts.
    - Added 1 new voice line for nearby gargoyle detection.
    - Added 1 new voice line for SteamID taunts. (This is just to get the taunt type loaded. Additions will need to be added by the host in the Custom Voice Lines folder.)
- **Custom Steam ID Taunts:**
    - You can now add custom taunts for specific players by placing OGG files in the "Taunt - SteamIDs" folder, using the player's Steam ID as the file name prefix.

**Changes:**

- **Audio System:**
    - Refactored taunt logic for better performance and maintainability.

**Bug Fixes:**

- Fixed minor bugs related to gargoyle behavior and AI.
- Updated enemy names in taunt clips to align with internal names.

## v0.4.2

**Changes:**

- **Gargoyle AI:**
    - Improved gargoyle AI responsiveness.
    - Optimized the process of finding nearby railings.
    - Refined targeting logic for improved efficiency.
- **Performance:**
    - Significantly improved gargoyle performance through various optimizations, including:
        - Replacing LINQ methods with more efficient loops.
        - Utilizing optimized distance calculations.
        - Caching frequently accessed values.
        - Reducing redundant function calls.

These changes enhance the gargoyle's performance and responsiveness, leading to smoother gameplay.

## v0.4.1 - Hot Fix

- **Bug Fixes:**
    - Fixed one of the new methods erroring out if Coroner was not installed.

## v0.4.0 - The Pushy Gargoyle

**New Features:**

- **Push Attack:**
    - Added a new "PushTarget" attack pattern where the gargoyle attempts to push players off edges.
    - Implemented a new cause of death in Coroner for deaths caused by gargoyle pushes.
- **New Voice Lines:**
    - Added a voice line for the gargoyle push death.
- **Configuration:**
    - Added a config option to enable or disable the "PushTarget" attack.
    - Nerfed default value for Aggro Range from 6 to 4.

**Changes:**

- **Gargoyle AI:**
    - Adjusted pathfinding logic to support the new `PushTarget` state.
    - Added a 45-second cooldown to the `PushTarget` state after a successful push.
    - Prevented line of sight from breaking the `PushTarget` state within aggro range.
    - Improved target selection to distribute targets more evenly among players.
    - Gargoyles now attempt to spread their targets across all players in the same area.
    - Improved base pathfinding to utilize positions near AI nodes instead of directly on them, increasing pathing options and preventing gargoyle stacking.
    - Decreased speed in the aggressive state.
    - Introduced caching for AI nodes to avoid redundant lookups.
- **CoronerClass:**
    - Changed the registry name of the gargoyle push death to avoid conflicts.
- **Performance:**
    - Optimized `LethalGargoylesAI` for improved performance and readability.
- **Attack Changes:**
    - Removed the fear level component from the gargoyle's attack.

**Documentation:**

- Updated the README with the latest changes and improvements.
- Separated vanilla Prior Death and Coroner Prior Death voice lines in the "Current Voice Lines" section.
- Added Employee Classes voice lines to the "Current Voice Lines" section.
- Updated the `Strings_en-us_gargoyle.xml` localization file with new death messages for the gargoyle push death.

## v0.3.0 Employee Classes Update

**New Features:**

- **Employee Classes Integration:**
    - Added soft dependency on the Employee Classes mod.
    - The gargoyle now taunts players based on their chosen class if the Employee Classes mod is installed.
    - Added a "Taunt - EmployeeClass" folder for custom voice lines related to employee classes.
    - Added new voice lines for each employee class (Scout, Brute, Maintenance, Researcher, Employee).
- **Taunt Variations:**
    - Implemented logic to randomly select from multiple audio clips with the same base name for Enemy, PriorDeath and Class taunts.
- **Coroner Taunts:**
    - Moved Coroner PriorDeath taunts into a subfolder to prevent loading if the Coroner mod is not installed.
- **Guaranteed Taunt Variation:**
    - Added logic to ensure the gargoyle will perform a taunt from a different category (Enemy, PriorDeath, or EmployeeClasses) after a set number of consecutive general taunts.
    - The number of consecutive general taunts allowed is dynamically determined based on installed mods and player states.

**Changes:**

- **Audio System:**
    - Optimized audio clip loading by adjusting the timing of the `WaitForClientReady` call.
- **File Structure:**
    - Reorganized the file structure in the source directory.
- **Gargoyle AI:**
    - Optimized gargoyle AI for improved performance and behavior.
    - Optimized animation and state transitions for smoother movement.
    - Added the ability for the gargoyle to target players even when outside.
    - Introduced the ability for the gargoyle to close doors behind it.
    - Updated acceleration, stopping distance, auto braking, and angular speed to improve target following during chases.

**Bug Fixes:**

- Fixed an issue where the gargoyle would get stuck in the `GetOutOfSight` state when spawned in front of a player.
- Attmepted to improve the gargoyle AI for smoother animation transitions.

**Documentation:**

- Updated the README with the latest changes and improvements.

## v0.2.0 Custom Voices Update

- **Audio System Rework:**
    - Voice lines are now loaded from folders within the plugin's directory instead of the asset bundle.
    - The host loads audio lists at the start of a game.
    - Clients receive audio data from the host upon joining.
    - Added a "Custom Voice Lines" folder for replacing or adding custom voice lines (see `CustomVoiceLines.txt` for details).

- **Bug Fixes:**
    - Fixed enemy warning taunts.

**Dependency Changes:**
- Added NVorbis library for OGG decoding.
- Added Concentus dependency for required system libraries:
    - `System.Memory`
    - `System.Buffers`
    - `System.Numeric.Vectors`
    - `System.Runtime.CompilerServices.Unsafe`

**Documentation:**

- Updated the README with the latest changes and improvements.
- Reformatted the changelog to display the latest version at the top.

## v0.1.0 

**New Features:**

- **Coroner Integration:**
    - Added soft dependency on the Coroner mod for custom causes of death.
    - Expanded prior death taunts to include causes of death from the base game and the Coroner mod (78 new taunts).
    - The gargoyle now taunts its target based on how they died in the previous round.
- **New Voice Lines:**
    - Added 3 new aggro taunts.
    - Added 2 new general taunts.
    - Added 2 gargoyle attack voice lines.
    - Added 2 gargoyle hit voice lines.
- **Gameplay Adjustments:**
    - Adjusted gargoyle aggro state to be more challenging.
    - Fixed gargoyle attack rate to prevent exceeding 1 attack per second.
    - Reduced the maximum distance of gargoyle voice lines.
    - Fixed enemy clips being incorrectly included in general taunts.
    - Reduced the default idle range from 30 to 20 to improve target following.

**Bug Fixes:**

- Fixed an issue where the gargoyle's health was accidentally set to 0.
- Fixed enemy clips being pulled into general taunts.

## v0.0.5

**Changes:**

- Optimized pathfinding and code for improved performance.
- Fixed various pathfinding issues.

## v0.0.4

- Fixed spelling errors in the README.

## v0.0.3

- Fixed an issue where the gargoyle could become permanently angry.
- Separated enemy taunts from general taunts and reduced their frequency.
- Fixed animation desynchronization issues.
- Fixed potential taunt desynchronization issues.
- Increased the aggro taunt timer.
- Fixed the swing attack.
- Fixed gargoyles talking at the same time.

## v0.0.2

- Fixed an issue where the gargoyle would not wander.
- Added voice line synchronization between clients.
- Increased the idle range.
- Improved animation handling to prevent walking in place.
- Added logic to prevent the gargoyle from getting permanently angry.

## v0.0.1

- Initial release.
