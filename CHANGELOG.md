## 0.0.1

- Initial release

## 0.0.2

- Fixed Gargoyle not wandering
- Added voice line sync between clients
- Increased idle range
- Added a more dynamic approach to animations. This should hopefully stop the walking in place.
- Added more logic to finding a hiding spot to hopefully prevent him from getting permanently angry.

## 0.0.3

- Another fix for him getting angry permanently.
- Seperated enemy taunts from the general list and made it a seperate chance to play a false enemy taunt. They were playing too frequently.
- Decreased the chance of a true enemy taunt.
- Animation dysnc fix
- Fix for small chance of taunt desyncs.
- Increased aggro taunt timer.
- Hopefully fixed the swing attack.
- Fixed gargoyles talking at the same time.

## 0.0.4

- Fixed Read Me name spellings

## 0.0.5

- Some pathing and code optimizations.
- Fixed a lot of pathing issues.

## 0.1.0

- Added soft dependancy on Coroner 
    - For custom cause of death for gargoyle.
    - Allows access to more causes of deaths in prior death taunts.
- Mod now tracks prior round deaths
    -  Adding 78 more taunts relating to cause of death. (18 from vanilla, 60 from Coroner.)
    - Gargoyle will now taunt it's target with how they died the last round.
- Added 3 aggro and 2 general taunts.
- 1 Gargoyle Attack Voice Line and 1 Gargoyle Hit Voice Line
- A few tweaks to the settings.
- Accidently set Gargoyles health to 0 at some point. Set back to 6 as intended.
- Adjusted Gargoyles aggro state to be harder to counter play.
- Fixed Gargoyles attacks being more than 1 per second.
- Reduced max distance of Gargoyle voice to 3* normal instead of 4*
- Fixed enemy clips being pulled into general taunts.
- Default Idle config reduced from 30 to 20. That way Gargoyles should follow their target a bit more closely.
    - You will need to update your config yourself if you installed this before this update and you want to see this change.