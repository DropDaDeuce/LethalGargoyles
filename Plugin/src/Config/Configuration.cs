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
        public ConfigEntry<int> minTaunt = cfg.Bind("General",
                                   "Min Taunt",
                                   15,
                                   "The minimum amount of time in seconds to wait between taunts. Other types of taunts will be half this number.");
        public ConfigEntry<int> maxTaunt = cfg.Bind("General",
                                   "Max Taunt",
                                   45,
                                   "The maximum amount of time in seconds to wait between general taunts. Other types of taunts will be half this number.");
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
                                 60,
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

        // --- Diagnostics -----------------------------------------------------------------
        // Runtime-gated logging. Live in RELEASE on purpose: a player chasing a bug can turn one
        // category up and send a log, without anyone shipping them a DEBUG build.
        public ConfigEntry<bool> diagEnabled = cfg.Bind("Diagnostics",
                                 "Enabled",
                                 true,
                                 "Master switch for diagnostic logging. When false, nothing below has any effect.");
        public ConfigEntry<LogLevel> diagDefaultLevel = cfg.Bind("Diagnostics",
                                 "Default Level",
                                 LogLevel.Warn,
                                 "Log level used for any category not overridden below.\nOff < Error < Warn < Info < Debug < Trace. Debug is the useful level when chasing a bug; Trace is a firehose.");
        public ConfigEntry<bool> diagIncludeContext = cfg.Bind("Diagnostics",
                                 "Include Context",
                                 true,
                                 "Prefix each line with [H] or [C] for host/client and the category name. Strongly recommended - a lot of this mod's bugs only happen on one side.");
        public ConfigEntry<int> diagRepeatLimit = cfg.Bind("Diagnostics",
                                 "Repeat Limit",
                                 20,
                                 "Suppress an identical message after this many occurrences in a round, so a per-frame problem cannot bury the rest of the log. 0 disables suppression.");

        public readonly Dictionary<LogCat, ConfigEntry<LogLevel>> diagCategoryLevels = [];

        /// <summary>
        /// Binds one level entry per category and pushes the whole set into <see cref="LGLog"/>.
        /// Called from Plugin.Awake before anything that might want to log.
        /// </summary>
        public void InitializeDiagnostics()
        {
            var levels = new Dictionary<LogCat, LogLevel>();
            foreach (LogCat cat in Enum.GetValues(typeof(LogCat)))
            {
                if (cat == LogCat.None) continue;
                var entry = cfg.Bind("Diagnostics",
                                     cat.ToString(),
                                     LogLevel.Warn,
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

        public void InitializeAudioClipConfigs(Dictionary<string, List<string>> audioClipFilePaths)
        {
            foreach (var cat in audioClipFilePaths)
            {
                string category = cat.Key;
                List<string> fileNames = cat.Value;

                foreach (string fileName in fileNames)
                {
                    string clipName = Path.GetFileNameWithoutExtension(fileName);

                    // Store the ConfigEntry<bool> in the dictionary
                    AudioClipEnableConfig[clipName] = Plugin.Instance.Config.Bind(
                        "Audio." + category,
                        $"Enable {clipName}",
                        true,
                        $"Enable the audio clip: {clipName}"
                    );
                }
            }
            ClearUnusedEntries(cfg);
        }

        public void ClearUnusedEntries(ConfigFile cfg)
        {
            // Normally, old unused config entries don't get removed, so we do it with this piece of code. Credit to Kittenji.
            //
            // This reflects on a PRIVATE, undocumented BepInEx property. GetProperty returns null
            // rather than throwing if it is ever renamed, and the old code then called GetValue on
            // that null - an NRE that escaped Awake and skipped everything after it, including
            // scrap registration and the footstep cache. The symptom ("statue never spawns") was
            // nowhere near the cause. Degrade to "pruning skipped" instead.
            try
            {
                PropertyInfo? orphanedEntriesProp = cfg.GetType().GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
                if (orphanedEntriesProp == null)
                {
                    LGLog.Warn(LogCat.Config, "BepInEx's private OrphanedEntries property was not found - stale config entries will not be pruned. Harmless, but it means BepInEx changed under the mod.");
                    return;
                }

                if (orphanedEntriesProp.GetValue(cfg, null) is Dictionary<ConfigDefinition, string> orphanedEntries)
                {
                    orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
                }
                cfg.Save(); // Save the config file to save these changes
            }
            catch (Exception ex)
            {
                LGLog.Warn(LogCat.Config, $"Config pruning skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}