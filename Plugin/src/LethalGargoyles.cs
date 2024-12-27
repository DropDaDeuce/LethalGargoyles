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

namespace LethalGargoyles.src
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    [BepInDependency("com.elitemastereric.coroner", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Jade.EmployeeClasses", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger = null!;
        public static Plugin Instance { get; private set; } = null!;
        internal static readonly Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        internal static PluginConfig BoundConfig { get; private set; } = null!;
        public bool IsCoronerLoaded { get; private set; }
        public bool IsEmployeeClassesLoaded { get; private set; }
        public static AssetBundle? ModAssets;
        public static string? CustomAudioFolderPath { get; private set; }

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
            InitializeNetworkBehaviours();

            Instance = this;
            var bundleName = "gargoyleassets";
            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), bundleName));
            if (ModAssets == null)
            {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }
           
            CustomAudioFolderPath = Path.Combine(Path.GetDirectoryName(Paths.ExecutablePath),"Lethal Gargoyles","Custom Voice Lines");
            LogIfDebugBuild(CustomAudioFolderPath);
            // Create the folder if it doesn't exist
            if (!Directory.Exists(CustomAudioFolderPath))
            {
                try
                {
                    Directory.CreateDirectory(CustomAudioFolderPath);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to create custom audio directory: {ex}");
                    // You might want to return or handle the error in some other way here
                    return;
                }
            }

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

            var LethalGargoyle = ModAssets.LoadAsset<EnemyType>("LethalGargoyle");
            var LethalGargoyleTN = ModAssets.LoadAsset<TerminalNode>("LethalGargoyleTN");
            var LethalGargoyleTK = ModAssets.LoadAsset<TerminalKeyword>("LethalGargoyleTK");

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(LethalGargoyle.enemyPrefab);
            Enemies.RegisterEnemy(LethalGargoyle, BoundConfig.SpawnWeight.Value, Levels.LevelTypes.All, LethalGargoyleTN, LethalGargoyleTK);
            harmony.PatchAll();
            
            IsCoronerLoaded = DepIsLoaded("com.elitemastereric.coroner");
            IsEmployeeClassesLoaded = DepIsLoaded("Jade.EmployeeClasses");
            Logger.LogInfo($"Coroner Is Loaded? " + IsCoronerLoaded);
            Logger.LogInfo($"EmployeeClasses Is Loaded? " + IsEmployeeClassesLoaded);

            if (IsCoronerLoaded)
            {
                SoftDepends.CoronerClass.Init();
            }

            defaultAudioClipFilePaths = GetDefaultAudioClipFilePaths();
            BoundConfig.InitializeAudioClipConfigs(defaultAudioClipFilePaths);

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        private static void InitializeNetworkBehaviours()
        {
            // See https://github.com/EvaisaDev/UnityNetcodePatcher?tab=readme-ov-file#preparing-mods-for-patching
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                if (type.FullName == "LethalGargoyles.src.SoftDepends.CoronerClass")
                    continue;
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
                { "Hit", [] }
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