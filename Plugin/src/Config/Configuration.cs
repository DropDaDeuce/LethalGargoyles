using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace LethalGargoyles.src.Config
{
    public class PluginConfig
    {
        // For more info on custom configs, see https://lethal.wiki/dev/intermediate/custom-configs
        public ConfigEntry<int> SpawnWeight;
        public ConfigEntry<int> baseSpeed;
        public ConfigEntry<int> attackRange;
        public ConfigEntry<int> attackDamage;
        public ConfigEntry<int> aggroRange;
        public ConfigEntry<int> idleDistance;
        public ConfigEntry<int> minTaunt;
        public ConfigEntry<int> maxTaunt;
        public ConfigEntry<int> distWarn;
        public ConfigEntry<int> bufferDist;
        public ConfigEntry<int> awareDist;
        public Dictionary<string, ConfigEntry<bool>> AudioClipEnableConfig { get; set; } = [];

        public PluginConfig(ConfigFile cfg)
        {

            AudioClipEnableConfig = [];

            SpawnWeight = cfg.Bind("General", //Main Catagory
                                   "Spawn weight", //SubCatagory
                                   50, //Value
                                   "The spawn chance weight for the Gargoyle, relative to other existing enemies.\n" + //Description
                                   "Goes up from 0, lower is more rare, 100 and up is very common.");

            baseSpeed = cfg.Bind("General",
                                   "Base Speed",
                                   4,
                                   "The base speed that the Gargoyle travels.");

            attackRange = cfg.Bind("General",
                                   "Attack Range",
                                   2,
                                   "The range that that the Gargoyle can attack.");

            attackDamage = cfg.Bind("General",
                                   "Attack Damage",
                                   20,
                                   "The base damage that the Gargoyle deals.");

            aggroRange = cfg.Bind("General",
                                   "Aggro Range",
                                   6,
                                   "The range in which the Gargoyle will agro.");
            idleDistance = cfg.Bind("General",
                                   "Idle Distance",
                                   20,
                                   "The distance in which the Gargoyle will stay idle if not seen.");
            minTaunt = cfg.Bind("General",
                                   "Min Taunt",
                                   15,
                                   "The minimum amount of time in seconds to wait between taunts. Other types of taunts will be half this number.");
            maxTaunt = cfg.Bind("General",
                                   "Max Taunt",
                                   45,
                                   "The maximum amount of time in seconds to wait between general taunts. Other types of taunts will be half this number.");
            distWarn = cfg.Bind("General",
                                   "Enemy Distance Warning",
                                   45,
                                   "The distance at which a gargoyle will warn players about enemies with his taunts. This taunt is set to a 20% chance to play if an enemy is near the gargoyle, and not all enemy warnings are real!");
            bufferDist = cfg.Bind("General",
                                   "Buffer Distance",
                                   12,
                                   "How much buffer the Gargoyle will try to keep between him and the target player.");
            awareDist = cfg.Bind("General",
                                 "Awareness",
                                 60,
                                 "How far away is the gargoyle aware of other players. This will affect performance, as this setting is used to calculate pathing.");

            /*foreach (AudioClip clip in ModAssets.LoadAllAssets<AudioClip>())
            {
                string clipName = clip.name;
                AudioClipEnableConfig[clipName] = cfg.Bind(
                    "Audio",
                    $"Enable{clipName}",
                    true,
                    $"Enable the audio clip: {clipName}"
                );
            }*/

            ClearUnusedEntries(cfg);
        }

        private void ClearUnusedEntries(ConfigFile cfg)
        {
            // Normally, old unused config entries don't get removed, so we do it with this piece of code. Credit to Kittenji.
            PropertyInfo orphanedEntriesProp = cfg.GetType().GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(cfg, null);
            orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
            cfg.Save(); // Save the config file to save these changes
        }
    }
}