## v0.8.0 - The Wandering Gargoyle

**AI Improvements:**

- Fixed the gargoyle loitering near its spawn instead of roaming while searching.
- Multiple gargoyles now spread their targets across the crew.
- Fixed the gargoyle hiding in places its target could plainly see.
- The gargoyle now backs away and looks again when it cannot find cover.
- Widened the search for hiding places to 160 candidate spots.
- Fixed the gargoyle always circling to the same side.
- Improved movement smoothing and reduced network traffic in multiplayer.
- Added auto braking so the gargoyle stops on arrival instead of overshooting.
- Fixed the gargoyle interrupting its own route calculations.
- Reduced the delay before the gargoyle reacts to being spotted, from 3 seconds to none.

**New Features:**

- **Diagnostics:**
    - Added a Diagnostics config section with a log level per area of the mod.
    - Diagnostics work in the normal release build, so no special build is needed.
    - Default Level acts as a floor on every area's log level.
- **Configuration:**
    - Added SteamID Taunt Cooldown, the shortest gap between a player's personal lines (default 90).
    - Added Cornered Aggression Delay, the seconds before the gargoyle gives up hiding and charges (default 5).
    - Added Follow Players Through Exits, letting the gargoyle use fire exits and the main entrance (default off).
    - Added Use Mineshaft Elevator, letting the gargoyle call and ride the elevator (default off).
    - Added Agent Radius, how much room the gargoyle's steering needs (default 0.5).

**Changes:**

- **Performance:**
    - Significantly improved performance when multiple gargoyles are active.
    - Optimized the search for hiding places to sort by distance before checking line of sight.
    - Removed a per-frame rebuild of the AI node list while the gargoyle is searching.
- **Gargoyle AI:**
    - Increased the default taunt timers from 15 and 45 seconds to 30 and 60.
- **Config File:**
    - Moved the per-clip voice line toggles to the bottom of the config file.
    - Renamed the Audio config sections to Voice Lines. Disabled clips stay disabled.

**Bug Fixes:**

- Fixed the gargoyle's push doing nothing for anyone except the host.
- Fixed footstep volume never changing with the gargoyle's state for anyone except the host.
- Fixed the gargoyle being unable to find cover near stairs or between floors.
- Fixed several ways the gargoyle could freeze on the spot, one for up to half a minute.
- Fixed the gargoyle freezing when a player stood at almost exactly the Idle Distance.
- Fixed the gargoyle forgetting and re-noticing a player at the edge of its Awareness range.
- Fixed the gargoyle standing in plain view at a stale hiding place and then charging.
- Fixed stereo voice lines being cut off halfway through for clients.
- Fixed the gargoyle statue scrap being silent for anyone who is not the host.
- Fixed every gargoyle statue in a player's inventory talking at once.
- Fixed one gargoyle dying stopping the survivors from opening and closing doors.
- Fixed a player becoming permanently un-shovable for the rest of the session.

**Game Parity:**

- Updated entrance teleport handling to match the current version of Lethal Company.

**Documentation:**

- Corrected the Min Taunt and Max Taunt descriptions, which claimed other taunts use half the range.

**Notes:**

- Existing config files keep their old taunt timers. Delete Min Taunt and Max Taunt to pick up the new defaults.
- Multiple gargoyles now spread across the crew instead of all chasing one player. This is intended.
- The config file reorganizes itself once, on the first launch after updating.

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
