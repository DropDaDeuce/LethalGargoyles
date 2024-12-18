using HarmonyLib;
using LethalGargoyles.src.Utility;
using Unity.Netcode;
using UnityEngine;

namespace LethalGargoyles.src.Patch
{
    [HarmonyPatch]
    public class NetworkObjectManager
    {

        [HarmonyPostfix, HarmonyPatch(typeof(GameNetworkManager), "Start")]
        public static void Init()
        {
            if (networkPrefab != null) return;

            if (Plugin.ModAssets != null)
            {
                networkPrefab = (GameObject)Plugin.ModAssets.LoadAsset("LGNetworkHandler");
                networkPrefab.AddComponent<AudioManager>();
                NetworkManager.Singleton.AddNetworkPrefab(networkPrefab);
            }
        }

        public static GameObject? networkPrefab;

        [HarmonyPostfix, HarmonyPatch(typeof(StartOfRound), "Awake")]
        public static void LoadClipsHostPostFix()
        {
            if (!NetworkManager.Singleton.IsHost) return;

            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                var networkHandlerHost = Object.Instantiate(networkPrefab, Vector3.zero, Quaternion.identity);
                 networkHandlerHost?.GetComponent<NetworkObject>().Spawn();
            }
        }
    }
}
public static class StaticHelpers
{
    
}
