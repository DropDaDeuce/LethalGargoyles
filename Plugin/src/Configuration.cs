using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;

namespace LethalGargoyles.Configuration {
    public class PluginConfig
    {
        // For more info on custom configs, see https://lethal.wiki/dev/intermediate/custom-configs
        public ConfigEntry<int> SpawnWeight;
        public ConfigEntry<int> baseSpeed;
        public ConfigEntry<int> attackRange;
        public ConfigEntry<int> attackDamage;
        public ConfigEntry<int> aggroRange;
        public ConfigEntry<int> idleDistance;

        public PluginConfig(ConfigFile cfg)
        {
            SpawnWeight = cfg.Bind("General", //Main Catagory
                                   "Spawn weight", //SubCatagory
                                   100, //Value
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

            ClearUnusedEntries(cfg);
        }

        private void ClearUnusedEntries(ConfigFile cfg) {
            // Normally, old unused config entries don't get removed, so we do it with this piece of code. Credit to Kittenji.
            PropertyInfo orphanedEntriesProp = cfg.GetType().GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(cfg, null);
            orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
            cfg.Save(); // Save the config file to save these changes
        }
    }
}