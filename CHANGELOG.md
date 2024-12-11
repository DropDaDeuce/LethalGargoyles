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
