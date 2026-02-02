# Lethal Gargoyles!

This mischievous gargoyle's goal is to follow you around and annoy you with his witty taunts. But be wary, he's not so passive if you don't respect his space!

> **I want to be transparent. Some content in "Lethal Gargoyles" was created using AI-assisted tools. I understand a lot of people don't wish to support anything that was made using AI, so this disclamer is here in respect of those views.**

## Check Out The Mod Showcase Here:
[![Lethal Gargoyles Mod Showcase](https://markdown-videos-api.jorgenkh.no/url?url=https%3A%2F%2Fyoutu.be%2FBGAoOVgEUk0)](https://youtu.be/BGAoOVgEUk0)

## Features:

* **Voiced Gargoyle:**  Features the voice of the modder, with 160+ unique voice lines (and more to come!).
* **Dynamic Behavior:** The gargoyle tries to stay close while remaining hidden, but will become aggressive if you get too close.
* **Varied Taunts:**  Includes a wide range of taunts for different situations, including general taunts, aggro responses, death reactions, and more.
* **Customizable Voice Lines:**  Add or replace voice lines with your own custom OGG files. (Details on how are below)
* **Scrap Item:**  Adds a Gargoyle Statue as scrap that can be interacted with to hear the Gargoyle's voice lines. Be careful, the Eyeless Dog can hear it too!
* **Configuration Options:** Disable specific voice lines or adjust other settings through the mod's configuration file.

<details>
<summary>Gargoyle Behavior (Spoilers)</summary>

- The gargoyle's primary goal is to annoy the player. It will try to stay close while remaining hidden, but its stealth skills are still under development.
- If the player enters its aggro range, it will chase and attack them. Be careful, as it might occasionally push you into walls (a bug that's being worked on).
- If the target player is walking on a catwalk, the gargoyle will try to push the player, potentially off the catwalk!

</details>

## Feedback and Requests

Any feedback or voice line requests can be submitted through the following channels:

* **GitHub Repo:** https://github.com/DropDaDeuce/LethalGargoyles
* **Lethal Company Modding Discord:** https://discord.com/channels/1168655651455639582/1312527029455032394

This is my first experience with Unity, C#, models, and animations. Any suggestions for improvement are welcome!

## Add/Replace Voice Lines
<details>

**1. Audio Format and Location**

- All custom voice lines **must be in OGG format**. Other formats (MP3, WAV, etc.) are not supported.
- Place your custom OGG files in the **"Custom Voice Lines"** folder located within your Lethal Company game directory (e.g., `C:\Program Files (x86)\Steam\steamapps\common\Lethal Company\Custom Voice Lines`).
- **Do not** place custom voice lines in the "Voice Lines" folder.
- There is a maximum size of **500KB** on the OGG files due to Steam networking limitations on network messages.

**2.  Voice Line Categories**

Voice lines are organized into the following categories:

- Combat Dialog
- Taunt - Activity
- Taunt - Aggro
- Taunt - Enemy
- Taunt - Gargoyle Death
- Taunt - General
- Taunt - Player Death
- Taunt - Prior Death
- Taunt - EmployeeClass (Requires the Employee Classes mod)
- Taunt - SteamIDs

**3.  Naming Conventions**

- **Hardcoded Categories:** For the categories "Taunt - Activity," "Taunt - Enemy," "Taunt - Prior Death," and "Taunt - EmployeeClass," you can add multiple variations of the same taunt by using the original file name as a base and adding a suffix.
    - For example, to add variations of the "taunt_priordeath_Abandoned" voice line, you can name your custom OGG files "taunt_priordeath_Abandoned2.ogg", "taunt_priordeath_Abandoned_Custom.ogg", etc. The mod will randomly choose between all variations with the same base name.
- **Other Categories:**  For the remaining categories, you can use any file name for your custom OGG files. Simply place them in the corresponding folder within "Custom Voice Lines."

**4. Disabling Voice Lines**

- If you want to disable a specific voice line, you can do so through the mod's configuration file.

**5. Steam ID Taunts**

- If you want to add a custom taunt for a specific Steam ID, you can do so by naming the OGG file "[SteamID][optional text].ogg" (e.g., "76561198012345678_ConorTaunt.ogg" or "76561198012345678.ogg"). The Gargoyle will play this custom taunt when the player with the specified Steam ID is the target.
- There is a 2.5% chance that the Gargoyle will play a Steam ID taunt. This is to prevent these taunts from being overused.
    - If the player has multiple Steam ID taunts, the Gargoyle will randomly choose one to play.

</details>

## Current Voice Lines

The gargoyle's taunts are categorized as follows:

* General
* Aggro
* Gargoyle Death
* Player Death
* Enemy Proximity
* Prior Death Vanilla
* Prior Death Coroner
* Employee Classes
* Activity
* SteamIDs

<details>
<summary>General (General taunts that play randomly during gameplay)</summary>
<pre>"Ach, I can smell your awful breath... all the way from here."</pre>
<pre>"I'd ask you to try and hit me... but ya'd probably poke yer eye out instead."</pre>
<pre>"I might be made of stone, but at least I ain't stone blind."</pre>
<pre>"Hey! You're that mighty employee, aren't ya? Well, I've got scrap for ya: KISS MY STONEY ARSE!"</pre>
<pre>"I bet your aim's as bad as your body odour!"</pre>
<pre>"I heard there was a prophecy about ya... yeah... something about THE WORST EMPLOYEE OF ALL TIME! Yeah, that was it! Hahaha!"</pre>
<pre>"You must be the sorriest excuse of an Employee I ever saw, and I've seen plenty."</pre>
<pre>"Ye cannae hit me. Ye cannae hit me! Haha!"</pre>
<pre>"Hi, my name's Barn Door. Bet ye cannae hit meeee!"</pre>
<pre>"You're more of a crack-pot than a crack-shot! Hahahaha!"</pre>
<pre>"Don't you try and ignore me, you pink-bellied numpty!"</pre>
<pre>"Is this what I've been sittin' here waiting for all these centuries? You?! Bahahaha!"</pre>
<pre>"I can tell by yer glazed over eyes ye cannae hit straight!"</pre>
<pre>"Hey! Employee! Do you have any tzp or flashlights? Hahaha, you don't have any skill, that's as plain as day!"</pre>
<pre>"Ach, away with ye, ye scavenger dog-monkey."</pre>
<pre>"Hey! Look at me when I'm insulting ya! You walking sack of compost!"</pre>
<pre>"You think you're smart? I've seen more brains in a slop-bucket!"</pre>
<pre>"Let's see your aim... If ya've got one!"</pre>
<pre>"You couldnae hit a castle wi'a ball of dung!"</pre>
<pre>"Ach, ya blunderin' goon, you couldnae hit me in a million years!"</pre>
<pre>"Tell me this, how does a rubbish employee like you stay alive? You must be a right jammy bugger!"</pre>
<pre>"What are you gonna do, huh? Hit me? Ooh, I'm shiverin', mummy, help!"</pre>
<pre>"Tell me this, is it true you don't know which way to hold a shovel? Hahaha!"'</pre>
<pre>"Oh, look! My face is all exposed! Betchya cannae hit me!"</pre>
<pre>"Which one ya cannae hit me with? Yer shovel, or yer sneeze?"</pre>
<pre>"Hey, what you are doing with all those weapons? You might as well use a wee toy slingshot, ye big baby!"</pre>
<pre>"Hey, come and have a go at me, if you think you're tough enough!"</pre>
<pre>"I can paint a target on me arse and you'll still be pointing at ye shoe!"</pre>
<pre>"You're so incompetent, you couldnae hit a spring head!"</pre>
<pre>"I've seen more enthusiasm from a Bracken!"</pre>
<pre>"What do you think of that, Mr. Pajama-Wearing, Basket-Face, Slipper-Wielding, Clipe-Dreep-Bauchle, Gether-Uping-Blate-Maw, Bleathering, Gomeril, Jessie, Oaf-Looking, Stauner, Nyaff, Plookie, Shan, Milk-Drinking, Soy-Faced Shilpit, Mim-Moothed, Sniveling, Worm-Eyed, Hotten-Blaugh, Vile-Stoochie, Callie-Breek-Tattie?" - Submitted by ThePatienceToad</pre>
<pre>"You must have drawn the ugly gene in the family."</pre>
<pre>"Its employees like you that make me happy humans are mortal!" - Submitted by ThePatienceToad</pre>
<pre>"If yer as slow on foot as ye are in the head, ye cannae hope to hit me!" - Submitted by Plague</pre>
</details>

<details>
<summary>Aggro (When you aggro him)</summary>
<pre>"That's It! I'll kick yer arse!"</pre>
<pre>"You think you're tough!? I got some scrap for ya!"</pre>
<pre>"I'll bloody make yer face even more ugly! Wait… I dinnae think that's possible." - Submitted by Electric</pre>
<pre>"Oi! You lookin' at me?! I'll give ye somethin' to look at!"</pre>
<pre>"Step back, ya numpty! Unless ye want a face full of stone!"</pre>
</details>

<details>
<summary>Gargoyle Death (When he dies)</summary>
<pre>"These are my final words. I hate you. Hahahaha!"</pre>
<pre>"At least... at least I'm not... not you..."</pre>
<pre>"At least I didn't trip over my own feet and fall into a pit… you imbecile… *coughs*"</pre>
</details>

<details>
<summary>Player Death (When a player dies to a Gargoyle)</summary>
<pre>"Hahahaha! You died!"</pre>
<pre>"Another employee down! Hahahaha!"</pre>
<pre>"Hahahaha! You suck!"</pre>
</details>

<details>
<summary>Enemy Proximity(Small chance to trigger when an enemy is near the gargoyle, or a false trigger)</summary>
<pre>"Hello, sir Bracken! He's over there! Hahahaha"</pre>
<pre>"Sit still and don't turn around. I want to watch that Spring Head maul you!"</pre>
<pre>"What a cute little Thumper! They're over there boy, go get em! Good boy!"</pre>
<pre>"Looks like you're being haunted! I hope you get possesed you useless pile of trash!"</pre>
<pre>"I hope this centipede eats your face!"</pre>
<pre>"The itsy bitsy spider ate the employees face. Hahahaha!"</pre>
<pre>"Hey look! Another employee. Go give him a hug!"</pre>
<pre>"Yippee! Hahahaha!"</pre>
<pre>"This Jester is hilarious! Go tell that joke to that employee over there! Hahahaha!"</pre>
<pre>"Hey, you! I found some nuts for you to crack. They're over there!"</pre>
<pre>"Hey! Employee! Ever been dissolved by a slime?"</pre>
<pre>"That's one scary butler! I'm glad I'm not you! Hahahaha!"</pre>
<pre>"This one eats employees! I like it already!"</pre>
<pre>"Another Gargoyle joins the employee hunt! Luckily this employee sucks, should be an easy kill!"</pre>
<pre>"Aww, what a cute little doggy! Look at all those teeth, just waiting to rip you to shreds. Oh… Was I supposed to be quiet? Hahahaha"</pre>
</details>

<details>
<summary>Prior Death Vanilla (He will taunt his target based on how/if they died last round)</summary>
<pre>"Left behind, eh?  Even your friends didn't like you!"</pre>
<pre>"I heard tough guys don't look at explosions, which is probably why ye died." - Submitted by ThePatienceToad</pre>
<pre>"They say 'fight fire with fire'. Well, ye fought fists with... yer face! HAHAHA!"</pre>
<pre>"Next time you get roasted, I'll bring some marshmallows."</pre>
<pre>"Flat as a pancake, ye were! Were you always that thin?</pre>
<pre>"You couldnae hold your breath longer than 10 seconds. Hahahaha!" - Submitted by ThePatienceToad</pre>
<pre>"Next time you get electrocuted, try not to pee yourself!"</pre>
<pre>"You got blown away, literally!"</pre>
<pre>"What went up, came down... and splat! Just like you!"</pre>
<pre>"My favorite part about the last moon. Bang! You were full of holes! What a surprise…"</pre>
<pre>"Next time ye want to go head first into something hard, I'll give ya a good smack!"</pre>
<pre>"Kicked ya right into the goal last round ye were! Hahahaha!" - Submitted by ThePatienceToad</pre>
<pre>"Torn limb from limb?  Served you right for getting out of bed that morning!"</pre>
<pre>"How much glue did they need to put ye back together?" - Submitted by ThePatienceToad</pre>
<pre>"Next time you see a knife, try running away from it instead of towards it! Hahahaha!"</pre>
<pre>"Gack! Couldn't breathe? Maybe ye should've tried breathing through your ears!"</pre>
<pre>"Looks like someone needed a breath of fresh air...permanently!"</pre>
<pre>"I can't believe it! You actually died of embarrassment! Hahahaha!" - Submitted by ThePatienceToad</pre>
</details>

<details>
<summary>Prior Death Coroner (He will taunt his target based on how/if they died last round)</summary>
<pre>"Couldnae outsmart a bunch of birds with hairy bums, could ye?"</pre>
<pre>"He's a sneaky one, that Barber! Appears out of thin air, then snip snip! Should've seen yer face... oh wait, he cut it in half! Hahahaha!"</pre>
<pre>"Bracken snuck up on ye. Should've seen the look on yer face when ye felt those bony fingers on yer neck! Then crack like a twig!"</pre>
<pre>"Caught in a wee web, were ye? Should've seen yer face when she came crawlin' out! Like a hairy, eight-legged beastie!"</pre>
<pre>"He went out with a bang, didn't he? Took ye right wit him. Hahahaha!"</pre>
<pre>"Heard ye were lookin' for a close shave. He gave ye one, didn't he? A bit too close for comfort, I'd say!"</pre>
<pre>"Heard those bees gave ye quite the shock! Should've seen yer hair standin' on end! Hahahaha!"</pre>
<pre>"If you had the brains to turn around a half second sooner, you might have survived!" - Submitted by ThePatienceToad</pre>
<pre>"Swallowed whole, were ye? He's got an appetite for careless employees, that worm! Should've seen yer face when he popped up! HAHAHA!"</pre>
<pre>"The pup heard ye sneakin' about! Should've seen him come flyin' through the air! Like a furry, toothy missile!"</pre>
<pre>"Heard ye screamin' all the way from here! Did ye think that would scare him off? He can't even hear ye! Hahahaha!"</pre>
<pre>"He's got a big appetite, that one! Try tried standin' still next time. Maybe he'll think yer a tree!"</pre>
<pre>"Couldn't handle a bit of a haunting, could ye? Yer head just popped like a ripe melon! Messy!"</pre>
<pre>"Couldn't keep yer hands off his shiny bits, could ye? He gave ye a good polishin', though, didn't he?"</pre>
<pre>"Slow and steady wins the race, eh? Except when it's a giant puddle of acid chasin' ye! Hahahaha!"</pre>
<pre>"That Jester's got quite the spring in his step, eh? Should've seen yer face when he popped out! Hahahaha!"</pre>
<pre>"Thought ye could outsmart one of our own, did ye? Yer body sure did make a bloody good chair!"</pre>
<pre>"Yer parenting skills are worse than yer survival skills... I dinnae know that was even possible!" - Submitted by Sniker</pre>
<pre>"A guy with a mask threw up on you and you fell over dead? Maybe that possessed clone has more braincells than you!" - Submitted by Sniker</pre>
<pre>"'Beware of gift bearing Greeks!' Or, in yer case, masks bearin' doom! Should've seen yer friends runnin'! Like wee bairns from a bogeyman!"</pre>
<pre>"Thought ye were done with him, did ye? Next time, try bringin' a fly swatter! Hahahaha!"</pre>
<pre>"He kicked ye so hard, ye flew higher than a hawk! Should've seen ye spinnin' through the air! Did ye land on yer head? Hahahaha!"</pre>
<pre>"He cracked ye good, didn't he? Should've seen ye dancin'! One step forward, two steps back... right into his shotgun blast!"</pre>
<pre>"He ran ye over like a wee speed bump, didn't he? Should've seen ye go flyin'!"</pre>
<pre>"Heard ye were lookin' for a quick trip to the moon. He granted yer wish, didn't he? One-way ticket, though, I'm afraid!"</pre>
<pre>"He's got quite the footwork, that one! Should've seen him tap-dancin' on yer head!"</pre>
<pre>"Ach, he roasted ye like a wee marshmallow, didn't he? Should've seen ye glowin'! Nice and crispy on the outside, I bet!"</pre>
<pre>"Did that wee beastie steal yer breath away? Should've seen ye flailin' about! Like a fish outta water!"</pre>
<pre>"Did ye trip over yer own feet tryin' to get away? Or did ye faint from the smell of his... perfume? HAHAHA! Either way, it's pathetic!"</pre>
<pre>"Ach, couldn't outrun a deaf beastie, could ye? Should've seen ye trippin' over yer own feet!"</pre>
<pre>"Should've seen ye flailin' about with those wee snakes on yer head! Too bad ye broke the fall with ye face!"</pre>
<pre>"Thought ye could make a deal with the devil, did ye? He took yer scrap... and yer soul! HAHAHA!"</pre>
<pre>"Heard ye were expectin' a package. Well, ye got one! Right on top of yer head!"</pre>
<pre>"You know what they say, watch where ye step. Oh, ye must have missed that one."</pre>
<pre>"They say 'lightning never strikes twice'. Well, it only needs to strike once to turn ye into a pile of ash! Hahahaha!"</pre>
<pre>"Heard ye were stargazin'. Well, ye got a closer look than ye planned!"</pre>
<pre>"They say 'curiosity killed the cat'. Well, it also killed the employee who wandered too far! Hahahaha!"</pre>
<pre>"How'd ye miss the big metal plate with spikes on it? Well it sure didn't miss you when it poked holes in ya! Hahahaha!"</pre>
<pre>"They say 'don't poke the bear'. Well, ye shouldn't poke the turret either! Hahahaha!"</pre>
<pre>"Remember that time ye missed that jump and died? Ha, Great times."</pre>
<pre>"Next time ye want to go for a fall, try bringin' a parachute!"</pre>
<pre>"Heard ye were tryin' out for the diving team. Well, ye certainly took the plunge!"</pre>
<pre>"Watch your step! There's a pit there! Oh wait, too late. Hahahaha!"</pre>
<pre>"Ye took the express elevator to the bottom, didn't ye? Did ye make a wish on the way down?"</pre>
<pre>"I heard ye took a bit of a tumble. Can you do it again? I wasn't looking last time."</pre>
<pre>"Thought ye were a master driver, did ye? Did you get your license out of a cereal box?"</pre>
<pre>"My favorite look on ye. Exploded to bits!"</pre>
<pre>"Thought ye could trust yer driver, did ye? Should've called a taxi!"</pre>
<pre>"Next time, try wearin' a traffic cone as a hat! Might make ye a wee bit more visible…"</pre>
<pre>"They say 'don't put all yer eggs in one basket'. Well, ye put all yer faith in that one egg... and it blew up in yer face! Hahahaha!"</pre>
<pre>"Yer jetpack had a wee bit of a temper tantrum, didn't it? Should've seen the fireworks! And the confetti... made of employee bits! Hahahaha!"</pre>
<pre>"Ye flew a bit too close to the sun? Should've seen ye splatter! Like a wee bug on a windshield!"</pre>
<pre>"That ladder had a bone to pick with ye, didn't it? Came down right on top of ye! Should've seen the dent it made! Maybe ye should try wearin' a helmet next time!"</pre>
<pre>"Thought ye could trust yer teammates, did ye? Turns out, they're sharper than they look!"</pre>
<pre>"Heard ye were tryin' out for the skeet shooting competition. Well, ye were the skeet!"</pre>
<pre>"Yer teammate gave ye a good whack, didn't they? Should've seen ye do a jig! One step forward, two steps back... right into the ground!"</pre>
<pre>"Your teammate gave ye a sign, didn't they? A stop sign... right to the head!"</pre>
<pre>"Your teammate gave ye a lesson in road safety, didn't they?"</pre>
<pre>"They say 'the ground can swallow ye whole'. Well, it did! Should've worn yer floaties! Hahahaha!"</pre>
<pre>"That last death sure was stunning!"</pre>
<pre>"Thought ye were safe on that catwalk? Not with him around! Hahahaha"</pre>
</details>

<details>
<summary>Employee Classes (If you have the EmployeeClasses mod)</summary>
<pre>"A Scout? More like a... lout! Probably trip over yer own feet tryin' to escape!"</pre>
<pre>"Go on, then, Brute! Hit me! I could use a massage... if ye can even reach me!"</pre>
<pre>"So ye think yer a smart researcher, eh? I've seen smarter rocks! And they're less squishy!"</pre>
<pre>"Go on, then, Maintenance! Try to repair yerself... after I'm done with ye!"</pre>
<pre>"Just a regular Employee, eh? Nothin' special... just like yer face! Hahahaha!"</pre>
</details>

<details>
<summary>Activity (Taunts the target based on several hardcoded actions)</summary>
<pre>"You think that's a key to the facility!? That's a key to your death! Hahahaha!"</pre>
<pre>"Can't find yer way out, can ye? Don't worry, I'll be here to watch ye die! Hahahaha!"</pre>
<pre>"Need to find the way out, do ye? Just keep wanderin', maybe ye'll stumble upon a friendly monster... or a bottomless pit! Hahahaha!"</pre>
<pre>"Ye killed another Gargoyle, did ye? He must have been deathly ill to die to a weakling like you!"</pre>
<pre>"So ye managed to snap his neck, eh? I could've done it with my little finger! Amateur."</pre>
<pre>"Ye killed a Butler? Ye must feel pretty high and mighty killing a harmless old man!"</pre>
<pre>"Think yer tough because ye squashed a bug? Yer more pathetic than the employee who sits at the ship and watches everyone die!"</pre>
<pre>"What kind of person kills a little girl? You digust me."</pre>
<pre>"Poor bug just wanted some shiny scrap, and ye killed em! I knew yer the worst employee, but yer also a terrible person!"</pre>
<pre>"Ye killed a harmless toy box, and you think yer strong? Bah!"</pre>
<pre>"Ye call us monsters, but then ye go and kill a BABY!? What a digusting creature you are."</pre>
<pre>"Ye see a sick employee, and instead of trying to cure em, you kill em? You really ARE the worst!"</pre>
<pre>"Oh, look at me! I'm an employee, I'm so strong that I kill toys! Hahahaha!"</pre>
<pre>"Ye must be a special kind of wimp to feel threatened by jello!"</pre>
<pre>"Yer so ugly, I think that spider died just from lookin at ya. Hahahaha!"</pre>
<pre>"Cowardly of ye to kill something that can't even move if ye look at it."</pre>
<pre>"That Thumper just wanted some cuddles, and you killed it! Yer even worse than I thought!"</pre>
<pre>"Oi, put that back! My hotub can't run without power!"</pre>
<pre>"Put that mask on yer face! Might make ye more attractive!"</pre>
<pre>"Would be a tragedy if you didn't put that mask on and kill all yer friends. Hahahaha!"</pre>
<pre>"You're such a bad parent, even that maneater baby wants to kill you." - Submitted by Sniker</pre>
</details>

<details>
<summary>SteamIDs (I added one for my own Steam ID so that the mod would actually load the clip array)</summary>
<pre>"If it isn't my creator! There's something I've always wanted to say to you, YOU SUCK! Hahahaha!"</pre>
</details>

## Compatibility
- Enhanced Monsters
    - Sets default values for Enhanced Monsters
- Coroner
    - Specific voice lines for coroner cause of death
    - Adds custom cause of death message for gargoyle
- Employee Classes
    - Adds taunts for each class.

## To Do
- 1 more taunt for each employee class.
- Add 
- **MAYBE** add Mirage integration. Gargoyle "Repeats" voice lines in a mocking manner. (Depends on difficulty and time to implement)
- Thinking of new ways to make the Gargoyle michievous without it being overbearing.

### Credits:
 - **Evaisa** for creating LethalLib.
 - **Hamunii** for Example Enemy.
 - **Xu Xiaolan** for the youtube tutorial.
 - **qwbarch** for uploading Concentus
 - **NVorbis:** This mod uses the NVorbis library for decoding OGG files. You can find the library and its source code here: [https://github.com/NVorbis/NVorbis](https://github.com/NVorbis/NVorbis)
 - **RichAudio**, **Syiacka**, and **Nightare** for testing with me.

<details>
<summary>Gargoyle Model Credit/Copyright - Original model by Lionhead Studios - Extracted by CharlieVet on cults3d.com - Modified by DropDaDeuce & Syiacka</summary>
<pre>The Gargoyle model was extracted from the game Fable II and prepared for printing including smoothing by CharlieVet on cults3d.com

This model is available for use under https://creativecommons.org/licenses/by-nc/4.0/

    - Attribution: CharlieVet https://cults3d.com/en/3d-model/game/gargoyle-on-edge 
    - Modified by: DropDaDeuce & Syiacka
    - License: Creative Commons Attribution-NonCommercial 4.0 International
        - You are free to:
            - Share: copy and redistribute the material in any medium or format
            - Adapt: remix, transform, and build upon the material
            - The licensor cannot revoke these freedoms as long as you follow the license terms.
                - Under the following terms:
                    - Attribution: You must give appropriate credit , provide a link to the license, and indicate if changes were made . You may do so in any reasonable manner, but not in any way that suggests the licensor endorses you or your use.
                    - NonCommercial: You may not use the material for commercial purposes .
                    - No additional restrictions: You may not apply legal terms or technological measures that legally restrict others from doing anything the license permits.
</pre>
</details>