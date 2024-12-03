using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using LethalLib.Modules;
using System.IO;
using System.Reflection;
using UnityEngine;
using LethalGargoyles.src.Config;
using System.Collections.Generic;
using LethalLib;

namespace LethalGargoyles.src
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    [BepInDependency("com.elitemastereric.coroner", BepInDependency.DependencyFlags.SoftDependency)]

    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger = null!;
        public static Plugin Instance { get; private set; } = null!;
        internal static readonly Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        internal static PluginConfig BoundConfig { get; private set; } = null!;

        public bool IsCoronerLoaded { get; private set; }

        public static AssetBundle? ModAssets;
        public static List<AudioClip> tauntClips = [];
        public static List<AudioClip> aggroClips = [];
        public static List<AudioClip> hideClips = [];
        public static List<AudioClip> seenClips = [];
        public static List<AudioClip> enemyClips = [];
        public static List<AudioClip> playerDeathClips = [];
        public static List<AudioClip> deathClips = [];
        public static List<AudioClip> priorDeathClips = [];
        public static List<AudioClip> activityClips = [];

#pragma warning disable IDE0051 // Remove unused private members
        private void Awake()
#pragma warning restore IDE0051 // Remove unused private members
        {
            Logger = base.Logger;
            BoundConfig = new PluginConfig(base.Config);
            InitializeNetworkBehaviours();
            var bundleName = "gargoyleassets";
            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), bundleName));
            if (ModAssets == null)
            {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }
            var LethalGargoyle = ModAssets.LoadAsset<EnemyType>("LethalGargoyle");
            var LethalGargoyleTN = ModAssets.LoadAsset<TerminalNode>("LethalGargoyleTN");
            var LethalGargoyleTK = ModAssets.LoadAsset<TerminalKeyword>("LethalGargoyleTK");

            Logger.LogInfo($"Loading audio clips");
            LoadClips();
            NetworkPrefabs.RegisterNetworkPrefab(LethalGargoyle.enemyPrefab);
            Enemies.RegisterEnemy(LethalGargoyle, BoundConfig.SpawnWeight.Value, Levels.LevelTypes.All, LethalGargoyleTN, LethalGargoyleTK);
            harmony.PatchAll();
            Instance = this;

            IsCoronerLoaded = DepIsLoaded("com.elitemastereric.coroner");
            Plugin.Logger.LogInfo($"Coroner Is Loaded? " + IsCoronerLoaded);

            if (IsCoronerLoaded)
            {
                SoftDepends.CoronerClass.Init();
            }

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        private static void InitializeNetworkBehaviours()
        {
            // See https://github.com/EvaisaDev/UnityNetcodePatcher?tab=readme-ov-file#preparing-mods-for-patching
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }

        void LoadClips()
        {
            if (ModAssets != null)
            {
                AudioClip[] allClips = ModAssets.LoadAllAssets<AudioClip>();

                foreach (AudioClip clip in allClips)
                {
                    if (clip.name.StartsWith("taunt_general"))
                    {
                        tauntClips.Add(clip);
                    }
                    else if (clip.name.StartsWith("taunt_aggro"))
                    {
                        aggroClips.Add(clip);
                    }
                    else if (clip.name.StartsWith("taunt_seen"))
                    {
                        seenClips.Add(clip);
                    }
                    else if (clip.name.StartsWith("taunt_hide"))
                    {
                        hideClips.Add(clip);
                    }
                    else if (clip.name.StartsWith("taunt_enemy"))
                    {
                        tauntClips.Add(clip);
                    }
                    else if (clip.name.StartsWith("taunt_playerdeath"))
                    {
                        playerDeathClips.Add(clip);
                    }
                    else if (clip.name.StartsWith("taunt_death"))
                    {
                        deathClips.Add(clip);
                    }
                    else if (clip.name.StartsWith("taunt_priordeath"))
                    {
                        priorDeathClips.Add(clip);
                    }
                    else if (clip.name.StartsWith("taunt_activity"))
                    {
                        activityClips.Add(clip);
                    }
                }
            }
        }

        private bool DepIsLoaded(string pGUID)
        {
            try
            {
                return BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(pGUID);
            }
            catch
            {
                return false;
            }
        }
    }
}