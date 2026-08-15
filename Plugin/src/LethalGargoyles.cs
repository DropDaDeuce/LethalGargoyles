using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using LethalLib.Modules;
using System.IO;
using System.Reflection;
using UnityEngine;
using LethalGargoyles.src.Config;
using LethalLib;
using System.Diagnostics;
using System;
using LethalGargoyles.src.Utility;
using System.Collections.Generic;
using LethalGargoyles.src.Scrap;
using LethalGargoyles.src.SoftDepends;

namespace LethalGargoyles.src
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    [BepInDependency(PathfindingLib.PathfindingLibPlugin.PluginGUID)]
    [BepInDependency("com.elitemastereric.coroner", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Jade.EmployeeClasses", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.velddev.enhancedmonsters", BepInDependency.DependencyFlags.SoftDependency)]

    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger = null!;
        public static Plugin Instance { get; private set; } = null!;
        internal static readonly Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        internal static PluginConfig BoundConfig { get; private set; } = null!;
        public bool IsCoronerLoaded { get; private set; }
        public bool IsEmployeeClassesLoaded { get; private set; }
        public bool IsEnhancedMonstersLoaded { get; private set; }
        public static AssetBundle? ModAssets;
        public static string? CustomAudioFolderPath { get; private set; }
        public AudioClip? stepSound;
        public static Dictionary<string, List<string>> defaultAudioClipFilePaths = [];

        [Conditional("DEBUG")]
        public void LogIfDebugBuild(string text)
        {
            Logger.LogInfo(text);
        }

#pragma warning disable IDE0051 // Remove unused private members
        private void Awake()
#pragma warning restore IDE0051 // Remove unused private members
        {
            Logger = base.Logger;
            BoundConfig = new PluginConfig(base.Config);
            // Diagnostics first, so everything after this point can log through LGLog.
            BoundConfig.InitializeDiagnostics();

            IsCoronerLoaded = DepIsLoaded("com.elitemastereric.coroner");
            IsEmployeeClassesLoaded = DepIsLoaded("Jade.EmployeeClasses");
            IsEnhancedMonstersLoaded = DepIsLoaded("com.velddev.enhancedmonsters");
            Instance = this;

            InitializeNetworkBehaviours();

            var bundleName = "gargoyleassets";
            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), bundleName));
            if (ModAssets == null)
            {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }

            CustomAudioFolderPath = Path.Combine(Path.GetDirectoryName(Paths.ExecutablePath),"Lethal Gargoyles","Custom Voice Lines");
            LogIfDebugBuild(CustomAudioFolderPath);

            // Custom voice line support is OPTIONAL. Enemy registration and harmony.PatchAll()
            // below are not. The old code let any IO failure here (a permission denial on a
            // OneDrive-synced game folder is the common one) escape Awake or return early, which
            // silently skipped both - so the gargoyle never spawned and the log showed a bare
            // IOException with nothing tying it to voice line folders. Degrade, don't abort.
            try
            {
                Directory.CreateDirectory(CustomAudioFolderPath);
                Directory.CreateDirectory(Path.Combine(CustomAudioFolderPath, "Combat Dialog", "Attack"));
                Directory.CreateDirectory(Path.Combine(CustomAudioFolderPath, "Combat Dialog", "Hit"));
                Directory.CreateDirectory(Path.Combine(CustomAudioFolderPath, "Taunt - Activity"));
                Directory.CreateDirectory(Path.Combine(CustomAudioFolderPath, "Taunt - Aggro"));
                Directory.CreateDirectory(Path.Combine(CustomAudioFolderPath, "Taunt - Enemy"));
                Directory.CreateDirectory(Path.Combine(CustomAudioFolderPath, "Taunt - Gargoyle Death"));
                Directory.CreateDirectory(Path.Combine(CustomAudioFolderPath, "Taunt - General"));
                Directory.CreateDirectory(Path.Combine(CustomAudioFolderPath, "Taunt - Player Death"));
                Directory.CreateDirectory(Path.Combine(CustomAudioFolderPath, "Taunt - Prior Death", "Coroner"));
                Directory.CreateDirectory(Path.Combine(CustomAudioFolderPath, "Taunt - EmployeeClass"));
                Directory.CreateDirectory(Path.Combine(CustomAudioFolderPath, "Taunt - SteamIDs"));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Could not create the Custom Voice Lines folders at '{CustomAudioFolderPath}': {ex.GetType().Name}: {ex.Message}. Custom voice lines will be unavailable; the rest of the mod will load normally.");
            }

            var LethalGargoyle = ModAssets.LoadAsset<EnemyType>("LethalGargoyle");
            var LethalGargoyleTN = ModAssets.LoadAsset<TerminalNode>("LethalGargoyleTN");
            var LethalGargoyleTK = ModAssets.LoadAsset<TerminalKeyword>("LethalGargoyleTK");

            // LoadAsset returns null rather than throwing when a name doesn't resolve, so a
            // renamed asset in the bundle used to surface as an NRE several lines later.
            if (LethalGargoyle == null || LethalGargoyle.enemyPrefab == null || LethalGargoyleTN == null || LethalGargoyleTK == null)
            {
                Logger.LogError($"Failed to load the gargoyle from '{bundleName}' (EnemyType={LethalGargoyle != null}, prefab={LethalGargoyle?.enemyPrefab != null}, TN={LethalGargoyleTN != null}, TK={LethalGargoyleTK != null}). The enemy will not be registered. This usually means the asset bundle is stale relative to the DLL.");
                return;
            }

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(LethalGargoyle.enemyPrefab);
            Enemies.RegisterEnemy(LethalGargoyle, BoundConfig.SpawnWeight.Value, Levels.LevelTypes.All, LethalGargoyleTN, LethalGargoyleTK);
            harmony.PatchAll();

            Logger.LogInfo($"Checking Soft Dependencies:\nCoroner Is Loaded? " + IsCoronerLoaded + "\nEmployeeClasses Is Loaded? " + IsEmployeeClassesLoaded + "\nEnhancedMonsters Is Loaded? " + IsEnhancedMonstersLoaded);

            if (IsCoronerLoaded)
            {
                SoftDepends.CoronerClass.Init();
            }

            defaultAudioClipFilePaths = GetDefaultAudioClipFilePaths();
            BoundConfig.InitializeAudioClipConfigs(defaultAudioClipFilePaths);

            if (BoundConfig.enableScrap.Value)
            {
                int iRarity = BoundConfig.scrapWeight.Value;
                Item gargoyleScrap = ModAssets.LoadAsset<Item>("Assets/ModAssets/LethalGargoyle/Scrap/GargoyleScrap.asset");
                if (gargoyleScrap == null || gargoyleScrap.spawnPrefab == null)
                {
                    Logger.LogError("Failed to load the Gargoyle Statue scrap from the asset bundle. Scrap registration skipped; the rest of the mod is unaffected.");
                }
                else
                {
                    GargoyleStatue script = gargoyleScrap.spawnPrefab.AddComponent<GargoyleStatue>();
                    script.grabbable = true;
                    script.grabbableToEnemies = true;
                    script.itemProperties = gargoyleScrap;

                    LethalLib.Modules.Utilities.FixMixerGroups(gargoyleScrap.spawnPrefab);
                    LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(gargoyleScrap.spawnPrefab);
                    LethalLib.Modules.Items.RegisterScrap(gargoyleScrap, iRarity, LethalLib.Modules.Levels.LevelTypes.All);
                    Logger.LogInfo($"Gargoyle Statue scrap is registered.");
                }
            }

            stepSound = ModAssets.LoadAsset<AudioClip>("Assets/ModAssets/LethalGargoyle/Audio/sfx_Step.ogg");
            if (stepSound == null)
            {
                Logger.LogError("Failed to load sfx_Step.ogg from the asset bundle - the gargoyle will walk silently.");
            }

            LogSessionBanner();
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        /// <summary>
        /// One block at load so that a pasted LogOutput.log is useful without a follow-up question.
        /// Deliberately uses Logger.LogInfo rather than LGLog: this must appear even with all
        /// diagnostics turned off, because it is the context every bug report needs.
        /// </summary>
        private void LogSessionBanner()
        {
            // The asset bundle's Unity version is the first thing to suspect when the DLL clearly
            // loaded but the model, animations or audio did not, so it leads.
            Logger.LogInfo($"=== LethalGargoyles {PluginInfo.PLUGIN_VERSION} | bundle '{(ModAssets != null ? "loaded" : "MISSING")}' | Unity {UnityEngine.Application.unityVersion} ===");
            Logger.LogInfo($"Soft deps: Coroner={IsCoronerLoaded} EmployeeClasses={IsEmployeeClassesLoaded} EnhancedMonsters={IsEnhancedMonstersLoaded}");
            Logger.LogInfo($"Config: sightRange={Math.Sqrt(LethalGargoyles.src.Enemy.LethalGargoylesAI.DEFAULT_SIGHT_RANGE_SQR):0.00}m aggroRange={BoundConfig.aggroRange.Value} awareDist={BoundConfig.awareDist.Value} push={BoundConfig.enablePush.Value} scrap={BoundConfig.enableScrap.Value}");
            Logger.LogInfo($"Custom voice lines: {CustomAudioFolderPath ?? "(unavailable)"}");
            Logger.LogInfo($"Diagnostics: enabled={BoundConfig.diagEnabled.Value} default={BoundConfig.diagDefaultLevel.Value} repeatLimit={BoundConfig.diagRepeatLimit.Value}");
        }

#pragma warning disable IDE0051 // Remove unused private members
        private void OnEnable()
#pragma warning restore IDE0051 // Remove unused private members
        {
            if (IsEnhancedMonstersLoaded)
            {
                EnhancedMonstersCompatibilityLayer.RegisterCustomMonsterEnemyData();
                Logger.LogInfo($"Gargoyle has been registered with EnhancedMonsters.");
            }

           // PlayerActivityTracker.shipInOrbit = true;
        }

        private static void InitializeNetworkBehaviours()
        {
            // See https://github.com/EvaisaDev/UnityNetcodePatcher?tab=readme-ov-file#preparing-mods-for-patching
            //
            // This is the first real step of Awake, so anything it throws used to abort the whole
            // plugin before enemy registration and harmony.PatchAll() - and GetTypes() can throw
            // ReflectionTypeLoadException while Invoke re-throws from ANY initializer in the
            // assembly, not just the CoronerClass one deliberately skipped below. One bad
            // initializer should cost its own type, not the entire mod.
            Type[] types;
            try
            {
                types = Assembly.GetExecutingAssembly().GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                Logger.LogError($"Could not enumerate assembly types for netcode initialization: {ex.Message}. Continuing with whatever loaded.");
                types = Array.FindAll(ex.Types, t => t != null);
            }

            foreach (var type in types)
            {
                if (type.FullName == "LethalGargoyles.src.SoftDepends.CoronerClass" && !Plugin.Instance.IsCoronerLoaded)
                    continue;
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        try
                        {
                            method.Invoke(null, null);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"Netcode initializer {type.FullName}.{method.Name} threw: {ex.InnerException?.Message ?? ex.Message}. RPCs on that type will not work.");
                        }
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

        // In your Plugin class
        private Dictionary<string, List<string>> GetDefaultAudioClipFilePaths()
        {
            Dictionary<string, List<string>> audioClipFilePaths = new()
            {
                { "General", [] },
                { "Aggro", [] },
                { "Enemy", [] },
                { "PlayerDeath", [] },
                { "GargoyleDeath", [] },
                { "PriorDeath", [] },
                { "Attack", [] },
                { "Hit", [] },
                { "Activity", [] },
                { "SteamIDs", [] }
            };

            if (Plugin.Instance.IsEmployeeClassesLoaded)
            {
                audioClipFilePaths.Add("Class", []);
            }

            foreach (var cat in audioClipFilePaths)
            {
                string category = cat.Key;
                List<string> fileNames = cat.Value;

                FileInfo[] defaultFiles = AudioManager.GetMP3Files(category, "Voice Lines");

                foreach (FileInfo file in defaultFiles)
                {
                    fileNames.Add(file.FullName);
                }

                // Add Coroner default files if Coroner mod is loaded
                if (category == "PriorDeath" && Plugin.Instance.IsCoronerLoaded)
                {
                    FileInfo[] coronerDefaultFiles = AudioManager.GetMP3Files("Coroner", "Voice Lines");
                    foreach (FileInfo file in coronerDefaultFiles)
                    {
                        fileNames.Add(file.FullName);
                    }
                }

                // Add EmployeeClasses default files if EmployeeClasses mod is loaded
                if (Plugin.Instance.IsEmployeeClassesLoaded && category == "Class")
                {
                    FileInfo[] classDefaultFiles = AudioManager.GetMP3Files("EmployeeClass", "Voice Lines");
                    foreach (FileInfo file in classDefaultFiles)
                    {
                        fileNames.Add(file.FullName);
                    }
                }
            }

            return audioClipFilePaths;
        }
    }
}