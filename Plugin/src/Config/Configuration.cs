using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using LethalGargoyles.src.Utility;

namespace LethalGargoyles.src.Config
{
    public class PluginConfig(ConfigFile cfg)
    {
        // For more info on custom configs, see https://lethal.wiki/dev/intermediate/custom-configs
        public ConfigEntry<int> SpawnWeight = cfg.Bind("General", //Main Catagory
                                   "Spawn weight", //SubCatagory
                                   50, //Value
                                   "The spawn chance weight for the Gargoyle, relative to other existing enemies.\n" + //Description
                                   "Goes up from 0, lower is more rare, 100 and up is very common.");
        public ConfigEntry<int> baseSpeed = cfg.Bind("General",
                                   "Base Speed",
                                   4,
                                   "The base speed that the Gargoyle travels.");
        public ConfigEntry<int> attackRange = cfg.Bind("General",
                                   "Attack Range",
                                   2,
                                   "The range that that the Gargoyle can attack.");
        public ConfigEntry<int> attackDamage = cfg.Bind("General",
                                   "Attack Damage",
                                   20,
                                   "The base damage that the Gargoyle deals.");
        public ConfigEntry<int> aggroRange = cfg.Bind("General",
                                   "Aggro Range",
                                   4,
                                   "The range in which the Gargoyle will agro.");
        public ConfigEntry<int> idleDistance = cfg.Bind("General",
                                   "Idle Distance",
                                   20,
                                   "The distance in which the Gargoyle will stay idle if not seen.");
        // THESE TWO ARE THE "make him talk less" KNOB. Raise BOTH - after every line the Gargoyle
        // rolls a fresh random wait between them, so lifting only Max barely moves the average.
        //
        // The old descriptions here claimed "other types of taunts will be half this number".
        // That was never true of the code: general, enemy-warning and aggro taunts all roll the
        // FULL Min..Max range (MarkTaunted, OtherTaunt and EnemyTaunt in LethalGargoylesAI all
        // call Random.Range(minTaunt, maxTaunt) unmodified). Corrected 2026-08-16 (b14).
        public ConfigEntry<int> minTaunt = cfg.Bind("General",
                                   "Min Taunt",
                                   30,
                                   "The SHORTEST the Gargoyle will stay quiet between taunts, in seconds. After every line he picks a new random wait between this and Max Taunt, so to make him less chatty raise BOTH numbers.\n" +
                                   "Covers his general taunts, his enemy warnings and his aggro taunts. Two things it deliberately does NOT cover: the bark the moment he decides to charge you (always immediate, so you get the warning), and his personal SteamID lines, which have their own setting below.");
        public ConfigEntry<int> maxTaunt = cfg.Bind("General",
                                   "Max Taunt",
                                   60,
                                   "The LONGEST the Gargoyle will stay quiet between taunts, in seconds. See Min Taunt - both are used together as a range.\n" +
                                   "The wait is drawn up to but not including this number, so 60 means he waits at most 59 seconds. Must be greater than Min Taunt.");
        // Was a bare 90f literal in LethalGargoylesAI.TryPlayPlayerSpecificTaunt, the only taunt
        // timer with no way to tune it. Default 90 is exactly the old behaviour.
        public ConfigEntry<float> steamIDTauntCooldown = cfg.Bind("General",
                                   "SteamID Taunt Cooldown",
                                   90f,
                                   "The shortest gap, in seconds, between the Gargoyle's personal lines - the ones recorded for a specific player's Steam ID.\n" +
                                   "These only have about a 2.5% chance of coming up in the first place; this is a floor underneath that so a player does not hear their own name twice in a row. 90 is the value the mod has always used.");
        public ConfigEntry<int> distWarn = cfg.Bind("General",
                                   "Enemy Distance Warning",
                                   45,
                                   "The distance at which a gargoyle will warn players about enemies with his taunts. This taunt is set to a 20% chance to play if an enemy is near the gargoyle, and not all enemy warnings are real!");
        public ConfigEntry<int> bufferDist = cfg.Bind("General",
                                   "Buffer Distance",
                                   12,
                                   "How much buffer the Gargoyle will try to keep between him and the target player.");
        public ConfigEntry<int> awareDist = cfg.Bind("General",
                                 "Awareness",
                                 40,
                                 "How far away is the gargoyle aware of other players. This will affect performance, as this setting is used to calculate pathing.");
        public ConfigEntry<bool> enablePush = cfg.Bind("General",
                                 "Pushy Gargoyle",
                                 true,
                                 "Enable/Disable the push state for the gargoyle.");
        public ConfigEntry<bool> enableScrap = cfg.Bind("Scrap",
                                 "Gargoyle Statue Scrap Enabled",
                                 true,
                                 "Enable/Disable the Gargoyle Statue from spawning as scrap.");
        public ConfigEntry<int> scrapWeight = cfg.Bind("Scrap",
                                 "Gargoyle Statue Spawn Weight",
                                 10,
                                 "Sets the weight for the statue to spawn. 1 being very rare, and 100 being very common.");
        public ConfigEntry<bool> dogHear = cfg.Bind("Scrap",
                                 "Dog Can Hear Scrap/Dog Taunt",
                                 true,
                                 "Can the dog hear the scrap when it talks? This will also enable/disable the dog taunt.");
        public ConfigEntry<float> dogCooldown = cfg.Bind("Scrap",
                                 "Cooldown For Dog Taunt",
                                 300f,
                                 "Sets the cooldown between dog taunts in seconds. Default is 300(5 minutes).");
        // Split out of "Enemy Distance Warning" - the statue reused that setting for a completely
        // different mechanic (how close the dog must be to hear the statue), so one slider was
        // silently tuning two things at once. Default matches the old shared value.
        public ConfigEntry<int> dogHearDist = cfg.Bind("Scrap",
                                 "Dog Hearing Distance",
                                 45,
                                 "How close an Eyeless Dog must be to hear the Gargoyle Statue talk. Previously this shared the General > Enemy Distance Warning setting; it is now separate so the two can be tuned independently.");

        // The answer to "he has nowhere to hide, so he just paces". In a wide-open room the hide
        // search genuinely has no good answer, and every candidate is equally bad, so he oscillates
        // between them. This gives that situation an exit.
        public ConfigEntry<float> corneredAggroDelay = cfg.Bind("General",
                                 "Cornered Aggression Delay",
                                 5f,
                                 "How many seconds the Gargoyle will tolerate being stared at while failing to find anywhere to hide, before he gives up on hiding and comes for you instead.\n" +
                                 "The timer only runs while you can actually see him, and it resets the moment he breaks line of sight - so in a normal interior he will usually vanish long before it fires. It is meant for open rooms and dead ends where there is genuinely nowhere to go. Set to 0 to switch this off and let him pace.");

        // --- Pathfinding -----------------------------------------------------------------
        // Both DEFAULT OFF. They switch on PathfindingLib smart-links that the Gargoyle has never
        // had, so turning one on is new movement behaviour, not a tuning change. See
        // Docs\Plans\Pathfinding_Audit.md -> P4 for why they were unreachable until now.
        public ConfigEntry<bool> allowExitFacility = cfg.Bind("Pathfinding",
                                 "Follow Players Through Exits",
                                 false,
                                 "Let the Gargoyle walk through fire exits and the main entrance, so it can follow players between the facility and the surface.\n" +
                                 "OFF by default: until now it could never do this, so it is a real change to how the Gargoyle behaves rather than a tuning knob. It can still appear near you outdoors either way - that is a separate mechanic.");
        public ConfigEntry<bool> allowElevators = cfg.Bind("Pathfinding",
                                 "Use Mineshaft Elevator",
                                 false,
                                 "Let the Gargoyle call and ride the mineshaft elevator, so it can change floors on mineshaft interiors instead of being stuck on the one it spawned on.\n" +
                                 "OFF by default for the same reason as the setting above - it is untested behaviour, not a tuning knob.");

        // Live-tunable so the corner-jam theory can be tested WITHOUT a Unity rebuild - NavMeshAgent
        // .radius is writable at runtime, unlike the prefab value it comes from. Default 1.25 is
        // exactly what the prefab already ships, so leaving this alone changes nothing.
        public ConfigEntry<float> agentRadius = cfg.Bind("Pathfinding",
                                 "Agent Radius",
                                 0.5f,
                                 "How much room the Gargoyle's steering thinks it needs. This does NOT change where it is allowed to walk - the level's navigation data is fixed - it changes how far it tries to stay from walls and from other Gargoyles.\n" +
                                 "0.5 is the shipped value and is small; most vanilla enemies are nearer 0.5. If the Gargoyle jams walking-on-the-spot at corners or doorways, or refuses to pass railings, try 0.5 and compare.");

        // --- Diagnostics -----------------------------------------------------------------
        // Runtime-gated logging. Live in RELEASE on purpose: a player chasing a bug can turn one
        // category up and send a log, without anyone shipping them a DEBUG build.
        public ConfigEntry<bool> diagEnabled = cfg.Bind("Diagnostics",
                                 "Enabled",
                                 true,
                                 "Master switch for diagnostic logging. When false, nothing below has any effect.");
        public ConfigEntry<LGLevel> diagDefaultLevel = cfg.Bind("Diagnostics",
                                 "Default Level",
                                 LGLevel.Warn,
                                 "The MINIMUM detail every category below will log. Raising this turns everything up at once, which is what you want when someone asks you for a log; raise a single category instead when you already know which part you care about.\n" +
                                 "Off < Error < Warn < Info < Debug < Trace. Debug is the useful level when chasing a bug; Trace is a firehose. A category set higher than this keeps its own setting.");
        public ConfigEntry<bool> diagIncludeContext = cfg.Bind("Diagnostics",
                                 "Include Context",
                                 true,
                                 "Prefix each line with [H] or [C] for host/client and the category name. Strongly recommended - a lot of this mod's bugs only happen on one side.");
        public ConfigEntry<int> diagRepeatLimit = cfg.Bind("Diagnostics",
                                 "Repeat Limit",
                                 20,
                                 "Suppress an identical message after this many occurrences in a round, so a per-frame problem cannot bury the rest of the log. 0 disables suppression.");

        public readonly Dictionary<LogCat, ConfigEntry<LGLevel>> diagCategoryLevels = [];

        /// <summary>
        /// Binds one level entry per category and pushes the whole set into <see cref="LGLog"/>.
        /// Called from Plugin.Awake before anything that might want to log.
        /// </summary>
        public void InitializeDiagnostics()
        {
            var levels = new Dictionary<LogCat, LGLevel>();
            foreach (LogCat cat in Enum.GetValues(typeof(LogCat)))
            {
                if (cat == LogCat.None) continue;
                var entry = cfg.Bind("Diagnostics",
                                     cat.ToString(),
                                     LGLevel.Warn,
                                     $"Log level for the {cat} subsystem.");
                diagCategoryLevels[cat] = entry;
                levels[cat] = entry.Value;
            }

            LGLog.Configure(
                diagEnabled.Value,
                diagDefaultLevel.Value,
                levels,
                diagIncludeContext.Value,
                diagRepeatLimit.Value);
        }

        public static Dictionary<string, ConfigEntry<bool>> AudioClipEnableConfig { get; set; } = [];

        // BepInEx writes the .cfg with its sections in ALPHABETICAL order - ConfigFile.Save sorts
        // by section name, and so does the in-game config manager. There are ~830 per-clip toggles,
        // so under the old "Audio.<Category>" name they sorted above EVERYTHING and filled lines
        // 4-848 of a 1050-line file; [General] did not start until line 939. That is not cosmetic:
        // it is why Min Taunt could not be found by scrolling. Measured 2026-08-16 (b14).
        //
        // "Voice Lines" is chosen purely because V sorts after every other section this mod binds
        // (Diagnostics < General < Pathfinding < Scrap < Voice Lines). IF YOU ADD A SECTION
        // STARTING W-Z IT WILL LAND BELOW THE CLIP TOGGLES AND BE JUST AS BURIED. Rename it, or
        // move to explicit numeric prefixes on every section instead.
        private const string AudioSectionPrefix = "Voice Lines.";

        // The name these toggles lived under up to and including v0.7.0.
        private const string LegacyAudioSectionPrefix = "Audio.";

        public void InitializeAudioClipConfigs(Dictionary<string, List<string>> audioClipFilePaths)
        {
            // Renaming a section ORPHANS every entry under the old name, and ClearUnusedEntries
            // below then deletes the orphans outright. Without this migration the rename would
            // silently switch every clip a player had turned off back ON, with nothing in the log
            // to explain it. Read the old values out of the orphan pile before that happens.
            Dictionary<ConfigDefinition, string>? orphans = TryGetOrphanedEntries(cfg);
            int migrated = 0;

            foreach (var cat in audioClipFilePaths)
            {
                string category = cat.Key;
                List<string> fileNames = cat.Value;

                foreach (string fileName in fileNames)
                {
                    string clipName = Path.GetFileNameWithoutExtension(fileName);
                    string key = $"Enable {clipName}";

                    ConfigEntry<bool> entry = cfg.Bind(
                        AudioSectionPrefix + category,
                        key,
                        true,
                        $"Enable the audio clip: {clipName}"
                    );

                    // Only a deliberate "off" is worth carrying across - everything else is already
                    // at the default the new entry just bound.
                    if (orphans != null &&
                        orphans.TryGetValue(new ConfigDefinition(LegacyAudioSectionPrefix + category, key), out string? previous) &&
                        bool.TryParse(previous, out bool wasEnabled) &&
                        !wasEnabled)
                    {
                        entry.Value = false;
                        migrated++;
                    }

                    // Store the ConfigEntry<bool> in the dictionary
                    AudioClipEnableConfig[clipName] = entry;
                }
            }

            if (migrated > 0)
            {
                LGLog.Info(LogCat.Config, $"Moved {migrated} disabled voice-line toggle(s) from the old [Audio.*] sections to [Voice Lines.*]. This happens once.");
            }

            ClearUnusedEntries(cfg);
        }

        /// <summary>
        /// Reads BepInEx's PRIVATE, undocumented <c>OrphanedEntries</c> - the entries that were in
        /// the .cfg on disk but that nothing bound this run. Returns the live dictionary, so a
        /// caller may read it AND clear it. Null means the reflection failed and the caller must
        /// degrade rather than throw.
        /// </summary>
        private static Dictionary<ConfigDefinition, string>? TryGetOrphanedEntries(ConfigFile cfg)
        {
            // GetProperty returns null rather than throwing if BepInEx ever renames this, and the
            // old code then called GetValue on that null - an NRE that escaped Awake and skipped
            // everything after it, including scrap registration and the footstep cache. The
            // symptom ("statue never spawns") was nowhere near the cause.
            try
            {
                PropertyInfo? orphanedEntriesProp = cfg.GetType().GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
                if (orphanedEntriesProp == null)
                {
                    LGLog.Warn(LogCat.Config, "BepInEx's private OrphanedEntries property was not found - stale config entries will not be pruned, and voice-line toggles saved under the old [Audio.*] section names will reset to enabled. Harmless, but it means BepInEx changed under the mod.");
                    return null;
                }

                return orphanedEntriesProp.GetValue(cfg, null) as Dictionary<ConfigDefinition, string>;
            }
            catch (Exception ex)
            {
                LGLog.Warn(LogCat.Config, $"Could not read BepInEx's orphaned config entries: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        public void ClearUnusedEntries(ConfigFile cfg)
        {
            // Normally, old unused config entries don't get removed, so we do it with this piece of code. Credit to Kittenji.
            try
            {
                TryGetOrphanedEntries(cfg)?.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
                cfg.Save(); // Save the config file to save these changes
            }
            catch (Exception ex)
            {
                LGLog.Warn(LogCat.Config, $"Config pruning skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}