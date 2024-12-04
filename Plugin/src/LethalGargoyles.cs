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
using System.Diagnostics;

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
        public static List<AudioClip> enemyClips = [];
        public static List<AudioClip> playerDeathClips = [];
        public static List<AudioClip> deathClips = [];
        public static List<AudioClip> priorDeathClips = [];
        public static List<AudioClip> activityClips = [];
        public static List<AudioClip> attackClips = [];
        public static List<AudioClip> hitClips = [];

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

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
#if DEBUG
            LogIfDebugBuild("Loaded gargoyle general taunt clips count: " + tauntClips.Count);
            LogIfDebugBuild("Loaded gargoyle aggro taunt clips count: " + aggroClips.Count);
            LogIfDebugBuild("Loaded gargoyle enemy taunt clips count: " + enemyClips.Count);
            LogIfDebugBuild("Loaded gargoyle player death taunt clips count: " + playerDeathClips.Count);
            LogIfDebugBuild("Loaded gargoyle gargoyle death taunt clips count: " + deathClips.Count);
            LogIfDebugBuild("Loaded gargoyle prior death taunt clips count: " + priorDeathClips.Count);
            LogIfDebugBuild("Loaded gargoyle activity taunt clips count: " + activityClips.Count);
            LogIfDebugBuild("Loaded gargoyle voice attack clips count: " + attackClips.Count);
            LogIfDebugBuild("Loaded gargoyle voice hit taunt clips count: " + hitClips.Count);
#endif
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
                int generalIndex = 0;
                int aggroIndex = 0;
                int enemyIndex = 0;
                int playerDeathIndex = 0;
                int deathIndex = 0;
                int priorDeathIndex = 0;
                int activityIndex = 0;
                int attackIndex = 0;
                int hitIndex = 0;

                foreach (AudioClip clip in allClips)
                {
                    if (clip.name.StartsWith("taunt_general"))
                    {
                        tauntClips.Add(clip);
                        LogIfDebugBuild("Loaded general taunt clip: " + tauntClips[generalIndex].name + " | Index Number: " + generalIndex);
                        generalIndex++;
                    }
                    else if (clip.name.StartsWith("taunt_aggro"))
                    {
                        aggroClips.Add(clip);
                        LogIfDebugBuild("Loaded aggro taunt clip: " + aggroClips[aggroIndex].name + " | Index Number: " + aggroIndex);
                        aggroIndex++;
                    }
                    else if (clip.name.StartsWith("taunt_enemy"))
                    {
                        enemyClips.Add(clip);
                        LogIfDebugBuild("Loaded enemy taunt clip: " + enemyClips[enemyIndex].name + " | Index Number: " + enemyIndex);
                        enemyIndex++;
                    }
                    else if (clip.name.StartsWith("taunt_playerdeath"))
                    {
                        playerDeathClips.Add(clip);
                        LogIfDebugBuild("Loaded player death taunt clip: " + playerDeathClips[playerDeathIndex].name + " | Index Number: " + playerDeathIndex);
                        playerDeathIndex++;
                    }
                    else if (clip.name.StartsWith("taunt_death"))
                    {
                        deathClips.Add(clip);
                        LogIfDebugBuild("Loaded gargoyle death taunt clip: " + deathClips[deathIndex].name + " | Index Number: " + deathIndex);
                        deathIndex++;
                    }
                    else if (clip.name.StartsWith("taunt_priordeath"))
                    {
                        priorDeathClips.Add(clip);
                        LogIfDebugBuild("Loaded gargoyle prior death taunt clip: " + priorDeathClips[priorDeathIndex].name + " | Index Number: " + priorDeathIndex);
                        priorDeathIndex++;
                    }
                    else if (clip.name.StartsWith("taunt_activity"))
                    {
                        activityClips.Add(clip);
                        LogIfDebugBuild("Loaded gargoyle activity taunt clip: " + activityClips[activityIndex].name + " | Index Number: " + activityIndex);
                        activityIndex++;
                    }
                    else if (clip.name.StartsWith("voice_attack"))
                    {
                        attackClips.Add(clip);
                        LogIfDebugBuild("Loaded gargoyle voice attack: " + attackClips[attackIndex].name + " | Index Number: " + attackIndex);
                        attackIndex++;
                    }
                    else if (clip.name.StartsWith("voice_hit"))
                    {
                        hitClips.Add(clip);
                        LogIfDebugBuild("Loaded gargoyle voice hit clip: " + hitClips[hitIndex].name + " | Index Number: " + hitIndex);
                        hitIndex++;
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