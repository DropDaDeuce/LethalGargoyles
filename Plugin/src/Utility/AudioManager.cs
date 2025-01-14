using BepInEx.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Threading;
using LethalGargoyles.src.Config;

namespace LethalGargoyles.src.Utility;
public class AudioManager : NetworkBehaviour
{
    public static List<AudioClip> tauntClips = [];
    public static List<AudioClip> aggroClips = [];
    public static List<AudioClip> enemyClips = [];
    public static List<AudioClip> playerDeathClips = [];
    public static List<AudioClip> deathClips = [];
    public static List<AudioClip> priorDeathClips = [];
    public static List<AudioClip> activityClips = [];
    public static List<AudioClip> attackClips = [];
    public static List<AudioClip> hitClips = [];
    public static List<AudioClip> classClips = [];
    public static List<AudioClip> playerClips = [];
    public static AudioManager? Instance;

    //public static Dictionary<string, ConfigEntry<bool>> AudioClipEnableConfig { get; set; } = [];
    public static Dictionary<string, List<string>> AudioClipFilePaths { get; private set; } = [];
    private readonly Dictionary<ulong, bool> clientReady = [];

    [Conditional("DEBUG")]
    static void LogIfDebugBuild(string text)
    {
        Plugin.Logger.LogInfo(text);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Instance = this;

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler("SendLGAudioClip", OnReceivedMessage);
        Plugin.Logger.LogInfo("Registered message handler for 'SendLGAudioClip'");

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectedCallback;
        }
        else
        {
            Plugin.Logger.LogInfo($"Connected to host with ID: {NetworkManager.Singleton.LocalClientId}");
        }

        if (IsHost)
        {
            Plugin.Logger.LogInfo($"Creating Audio Clip List");
            LoadClipList(Plugin.defaultAudioClipFilePaths);
            LoadAudioClipsFromConfig();
#if DEBUG 
            Plugin.Instance.StartCoroutine(LogClipCounts());
#endif
        }
    }

    public override void OnNetworkDespawn()
    {
        Plugin.Logger.LogInfo($"Clearing clip lists");
        tauntClips.Clear();
        aggroClips.Clear();
        enemyClips.Clear();
        playerDeathClips.Clear();
        deathClips.Clear();
        priorDeathClips.Clear();
        activityClips.Clear();
        attackClips.Clear();
        hitClips.Clear();
        classClips.Clear();
        playerClips.Clear();

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnectedCallback;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnectedCallback;
        }
    }

    private IEnumerator LogClipCounts()
    {
        yield return null; // Wait for one frame

        LogIfDebugBuild("Loaded gargoyle general taunt clips count: " + tauntClips.Count);
        LogIfDebugBuild("Loaded gargoyle aggro taunt clips count: " + aggroClips.Count);
        LogIfDebugBuild("Loaded gargoyle enemy taunt clips count: " + enemyClips.Count);
        LogIfDebugBuild("Loaded gargoyle player death taunt clips count: " + playerDeathClips.Count);
        LogIfDebugBuild("Loaded gargoyle gargoyle death taunt clips count: " + deathClips.Count);
        LogIfDebugBuild("Loaded gargoyle prior death taunt clips count: " + priorDeathClips.Count);
        LogIfDebugBuild("Loaded gargoyle activity taunt clips count: " + activityClips.Count);
        LogIfDebugBuild("Loaded gargoyle class taunt clips count: " + classClips.Count);
        LogIfDebugBuild("Loaded gargoyle voice attack clips count: " + attackClips.Count);
        LogIfDebugBuild("Loaded gargoyle voice hit clips count: " + hitClips.Count);
        LogIfDebugBuild("Loaded gargoyle player clips count: " + playerClips.Count);
    }

    private void OnClientConnectedCallback(ulong clientId)
    {
        Plugin.Logger.LogInfo($"Client connected: {clientId}");
        // Ensure the client entry exists before starting the coroutine
        if (!clientReady.ContainsKey(clientId))
        {
            clientReady.Add(clientId, true );
        }
        SendAudioClipsDelayed(clientId);
    }

    private void OnClientDisconnectedCallback(ulong clientId)
    {
        if (clientReady.ContainsKey(clientId))
        {
            clientReady.Remove(clientId);
        }
    }

    public async void SendAudioClipsDelayed(ulong clientId)
    {
        bool isPlayerFullyLoaded = false;
        List<ulong> fullyLoadedPlayers = StartOfRound.Instance.fullyLoadedPlayers;

        CancellationTokenSource cts = new();
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        await Task.Run(() =>
        {
            while (!isPlayerFullyLoaded || !clientReady.ContainsKey(clientId))
            {
                LogIfDebugBuild($"Client: {clientId}, is still loading.");
                for (int i = 0; i < fullyLoadedPlayers.Count; i++)
                {
                    if (fullyLoadedPlayers[i] == clientId)
                    {
                        isPlayerFullyLoaded = true;
                        break;
                    }
                }

                Task.Yield();
            }
        }, cts.Token);

        foreach (var cat in AudioClipFilePaths)
        {
            string category = cat.Key;
            List<string> fileNames = cat.Value;
            List<AudioClip> clipList = GetClipListByCategory(category);

            foreach (string fileName in fileNames)
            {
                string clipName = Path.GetFileNameWithoutExtension(fileName);

                // Try to get the config entry
                if (PluginConfig.AudioClipEnableConfig.TryGetValue(clipName, out ConfigEntry<bool> configEntry))
                {
                    // Check if the entry is enabled
                    if (configEntry.Value)
                    {
                        byte[]? audioData = AudioFileToByteArray(fileName);
                        if (audioData?.Length > 512000)
                        {
                            Plugin.Logger.LogError($"Sending Clip({clipName}) failed. Max clip size is 512000 bytes or 500KB");
                            break;
                        }
                        else if (audioData != null)
                        {
                            await WaitForClientReady(clientId);
                            if (!clientReady.ContainsKey(clientId)) break;
                            await SendAudioClipToClient(clientId, audioData, clipName, category);
                            clientReady[clientId] = false;
                            SetClientReadyClientRpc(false, clientId);
                            Plugin.Logger.LogInfo($"Sent Clip({clipName}) to ClientID({clientId})");
                        }
                    }
                    else
                    {
                        // Log that the clip is disabled in the config
                        Plugin.Instance.LogIfDebugBuild($"Clip '{clipName}' is disabled in the config.");
                    }
                }
                else
                {
                    // Log that no config entry was found for the clip
                    Plugin.Instance.LogIfDebugBuild($"No config entry found for clip '{clipName}'. Sending anyway.");

                    // Send the audio clip even if no config entry was found
                    byte[]? audioData = AudioFileToByteArray(fileName);
                    if (audioData?.Length > 512000)
                    {
                        Plugin.Logger.LogError($"Sending Clip({clipName}) failed. Max clip size is 512000 bytes or 500KB");
                        break;
                    }
                    else if (audioData != null)
                    {
                        await WaitForClientReady(clientId);
                        if (!clientReady.ContainsKey(clientId)) break;
                        await SendAudioClipToClient(clientId, audioData, clipName, category);
                        clientReady[clientId] = false;
                        SetClientReadyClientRpc(false, clientId);
                        Plugin.Logger.LogInfo($"Sent Clip({clipName}) to ClientID({clientId})");
                    }
                }
            }
        }
    }

    private async Task WaitForClientReady(ulong clientId)
    {
        CancellationTokenSource cts = new();
        cts.CancelAfter(TimeSpan.FromSeconds(5)); // Set timeout to 5 seconds

        try
        {
            await Task.Run(async () =>
            {
                while (!clientReady[clientId])
                {
                    await Task.Delay(100, cts.Token); // Use cancellation token with Task.Delay
                }
            }, cts.Token); // Pass cancellation token to Task.Run
        }
        catch (OperationCanceledException)
        {
            if (!clientReady.ContainsKey(clientId))
            {
                Plugin.Logger.LogWarning($"Client {clientId} is disconnected.");
                return;
            }
            Plugin.Logger.LogWarning($"Client {clientId} did not respond within the timeout, trying to send clip anyways.");
            clientReady[clientId] = true;
        }
    }

    private byte[]? AudioFileToByteArray(string filePath)
    {
        // Determine AudioType based on file extension
        AudioType audioType = Path.GetExtension(filePath).ToLower() switch
        {
            ".mp3" => AudioType.MPEG,
            ".wav" => AudioType.WAV,
            ".ogg" => AudioType.OGGVORBIS, // Add support for OGG
            _ => AudioType.UNKNOWN
        };

        if (audioType == AudioType.UNKNOWN)
        {
            Plugin.Logger.LogError($"Unsupported audio file format: {filePath}");
            return null;
        }

        UnityWebRequest webRequest = UnityWebRequestMultimedia.GetAudioClip(filePath, audioType);
        webRequest.downloadHandler = new DownloadHandlerBuffer();
        var operation = webRequest.SendWebRequest();
        while (!operation.isDone)
        {
            Task.Yield();
        }

        if (webRequest.result == UnityWebRequest.Result.ProtocolError)
        {
            Plugin.Logger.LogError(webRequest.error);
            return null;
        }
        else
        {
            return webRequest.downloadHandler.data;
        }
    }

    private async Task SendAudioClipToClient(ulong clientId, byte[] audioData, string clipName, string category)
    {
        int totalBufferSize = audioData.Length + 200;
        LogIfDebugBuild($"Initializing the writer with length of: {totalBufferSize}");
        using FastBufferWriter writer = new(totalBufferSize, Allocator.Temp);

        LogIfDebugBuild("writer initialized");

        // Include fragment index and total fragment count
        writer.WriteValueSafe(category);
        writer.WriteValueSafe(clipName);

        if (writer.Capacity < audioData.Length)
        {
            Plugin.Logger.LogError("Writer Capacity is less than clip size!");
            return;
        }

        writer.WriteValueSafe(audioData.Length);
        writer.WriteBytesSafe(audioData, audioData.Length, 0);

        //Plugin.Logger.LogInfo("Sending Clip!");
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("SendLGAudioClip", clientId, writer, NetworkDelivery.ReliableFragmentedSequenced); 
        await Task.Yield();
    }

    // Client-side: Handle the incoming audio clip data
    public void OnReceivedMessage(ulong clientId, FastBufferReader messagePayload)
    {
        if (IsServer || IsHost) return; // Don't process on the server or host
        SetClientReadyServerRpc(true, NetworkManager.Singleton.LocalClientId);
        messagePayload.ReadValueSafe(out string category);
        messagePayload.ReadValueSafe(out string clipName);
        messagePayload.ReadValueSafe(out int audioDataLength); // Read fragment length
        byte[] audioData = new byte[audioDataLength];
        messagePayload.ReadBytesSafe(ref audioData, audioDataLength);

        StartCoroutine(ProcessAudioClip(audioData, clipName, category));
    }

    private IEnumerator ProcessAudioClip(byte[] audioData, string clipName, string category)
    {       
            //Copyright provided for use of NVorbis. The code below is of my own writing,
            //But some of the methods and types used to make this work are from NVorbis.

            /* Copyright (c) 2020 Andrew Ward (NVorbis library)
            Permission is hereby granted, free of charge, to any person obtaining a copy
            of this software and associated documentation files(the "Software"), to deal
            in the Software without restriction, including without limitation the rights
            to use, copy, modify, merge, publish, distribute, sublicense, and/ or sell
            copies of the Software, and to permit persons to whom the Software is
            furnished to do so, subject to the following conditions:

                    The above copyright notice and this permission notice shall be included in all
                    copies or substantial portions of the Software.

            THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
            IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
            FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
            AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
            LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
            OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
            SOFTWARE.*/

            using var vorbis = new NVorbis.VorbisReader(new MemoryStream(audioData, false));

            var audioBuffer = new float[vorbis.TotalSamples]; // Just dump everything
            AudioClip clip = AudioClip.Create(clipName, (int)(vorbis.TotalSamples / vorbis.Channels), vorbis.Channels, vorbis.SampleRate, false);
            int read = vorbis.ReadSamples(audioBuffer, 0, (int)vorbis.TotalSamples);
            clip.SetData(audioBuffer, 0); // <-- your clip Remember to destroy when not use anymore
            List<AudioClip> clipList = GetClipListByCategory(category);
            clipList.Add(clip);
            Plugin.Logger.LogInfo("Clip Loaded: " + clip.name);
            //StartOfRound.Instance.ship3DAudio.PlayOneShot(clip);
            yield break;
    }

    private void LoadAudioClipsFromConfig()
    {
        foreach (var kvp in AudioClipFilePaths)
        {
            string category = kvp.Key;
            List<string> fileNames = kvp.Value;
            List<AudioClip> clipList = GetClipListByCategory(category);

            foreach (string fileName in fileNames)
            {
                string clipName = Path.GetFileNameWithoutExtension(fileName);

                Plugin.Instance.LogIfDebugBuild($"Trying to load clip: {clipName}");

                // Try to get the config entry
                if (PluginConfig.AudioClipEnableConfig.TryGetValue(clipName, out ConfigEntry<bool> configEntry))
                {
                    // Check if the entry is enabled
                    if (configEntry.Value)
                    {
                        StartCoroutine(LoadAudioClip(fileName, category));
                    }
                    else
                    {
                        // Log that the clip is disabled in the config
                        Plugin.Instance.LogIfDebugBuild($"Clip '{clipName}' is disabled in the config.");
                    }
                }
                else
                {
                    // Log that no config entry was found for the clip
                    Plugin.Instance.LogIfDebugBuild($"No config entry found for clip '{clipName}'. Loading anyway.");

                    // Continue loading the clip even if no entry is found
                    StartCoroutine(LoadAudioClip(fileName, category));
                }
            }
        }
    }

    // Modified LoadAudioClip coroutine to take a file path
    private IEnumerator LoadAudioClip(string filePath, string category)
    {
        // Determine AudioType based on file extension
        AudioType audioType = Path.GetExtension(filePath).ToLower() switch
        {
            ".ogg" => AudioType.OGGVORBIS, // Add support for OGG
            _ => AudioType.UNKNOWN
        };

        if (audioType == AudioType.UNKNOWN)
        {
            Plugin.Logger.LogError($"Unsupported audio file format: {filePath}");
            yield break; // Exit the coroutine
        }

        UnityWebRequest webRequest = UnityWebRequestMultimedia.GetAudioClip(filePath, audioType);
        yield return webRequest.SendWebRequest();
        
        if (webRequest.result == UnityWebRequest.Result.ProtocolError)
        {
            Plugin.Logger.LogError(webRequest.error);
        }
        else
        {
            AudioClip clip = DownloadHandlerAudioClip.GetContent(webRequest);
            List<AudioClip> clipList = GetClipListByCategory(category);
            clip.name = Path.GetFileNameWithoutExtension(filePath);
            clipList.Add(clip);
            Plugin.Logger.LogInfo("Loaded clip: " + clip.name + " | Catagory: " + category);
        }
    }

    // Helper method to get the clip list based on category name
    public static List<AudioClip> GetClipListByCategory(string category)
    {
        return category switch
        {
            "General" => tauntClips,
            "Aggro" => aggroClips,
            "Enemy" => enemyClips,
            "PlayerDeath" => playerDeathClips,
            "GargoyleDeath" => deathClips,
            "PriorDeath" => priorDeathClips,
            "Activity" => activityClips,
            "Class" => classClips,
            "Attack" => attackClips,
            "Hit" => hitClips,
            "SteamIDs" => playerClips,
            _ => throw new ArgumentException($"Invalid audio clip category: {category}"),// Or throw an exception
        };
    }

    // In your AudioManager class
    public void LoadClipList(Dictionary<string, List<string>> defaultAudioClipFilePaths)
    {
        AudioClipFilePaths = defaultAudioClipFilePaths; // Start with the defaults
        foreach (var cat in AudioClipFilePaths)
        {
            string category = cat.Key;
            List<string> fileNames = cat.Value;

            // Get custom files
            FileInfo[] customFiles = GetMP3Files(category, "Custom Voice Lines");

            // Add custom files, replacing any with the same name as default files
            foreach (FileInfo customFile in customFiles)
            {
                string customFileName = Path.GetFileNameWithoutExtension(customFile.FullName);
                bool replaced = false;

                for (int i = 0; i < fileNames.Count; i++)
                {
                    string defaultFileName = Path.GetFileNameWithoutExtension(fileNames[i]);
                    if (customFileName == defaultFileName)
                    {
                        fileNames[i] = customFile.FullName;
                        replaced = true;
                        break;
                    }
                }

                if (!replaced)
                {
                    fileNames.Add(customFile.FullName);
                }
            }

            // Handle Coroner files if Coroner mod is loaded
            if (category == "PriorDeath" && Plugin.Instance.IsCoronerLoaded)
            {
                FileInfo[] coronerDefaultFiles = GetMP3Files("Coroner", "Voice Lines");
                FileInfo[] coronerCustomFiles = GetMP3Files("Coroner", "Custom Voice Lines");

                // Add Coroner default files
                foreach (FileInfo file in coronerDefaultFiles)
                {
                    fileNames.Add(file.FullName);
                }

                // Add Coroner custom files, replacing defaults if necessary
                foreach (FileInfo customFile in coronerCustomFiles)
                {
                    string customFileName = Path.GetFileNameWithoutExtension(customFile.FullName);
                    bool replaced = false;

                    for (int i = 0; i < fileNames.Count; i++)
                    {
                        string defaultFileName = Path.GetFileNameWithoutExtension(fileNames[i]);
                        if (customFileName == defaultFileName)
                        {
                            fileNames[i] = customFile.FullName;
                            replaced = true;
                            break;
                        }
                    }

                    if (!replaced)
                    {
                        fileNames.Add(customFile.FullName);
                    }
                }
            }

            // Handle EmployeeClasses files if EmployeeClasses mod is loaded
            if (Plugin.Instance.IsEmployeeClassesLoaded && category == "Class")
            {
                FileInfo[] classDefaultFiles = GetMP3Files("EmployeeClass", "Voice Lines");
                FileInfo[] classCustomFiles = GetMP3Files("EmployeeClass", "Custom Voice Lines");

                // Add EmployeeClasses default files
                foreach (FileInfo file in classDefaultFiles)
                {
                    fileNames.Add(file.FullName);
                }

                // Add EmployeeClasses custom files, replacing defaults if necessary
                foreach (FileInfo customFile in classCustomFiles)
                {
                    string customFileName = Path.GetFileNameWithoutExtension(customFile.FullName);
                    bool replaced = false;

                    for (int i = 0; i < fileNames.Count; i++)
                    {
                        string defaultFileName = Path.GetFileNameWithoutExtension(fileNames[i]);
                        if (customFileName == defaultFileName)
                        {
                            fileNames[i] = customFile.FullName;
                            replaced = true;
                            break;
                        }
                    }

                    if (!replaced)
                    {
                        fileNames.Add(customFile.FullName);
                    }
                }
            }
        }
    }

    public static FileInfo[] GetMP3Files(string type, string folderName)
    {
        DirectoryInfo directoryInfo;

        string? folderLoc = folderName switch
        {
            "Voice Lines" => Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location),folderName),
            "Custom Voice Lines" => Plugin.CustomAudioFolderPath,
            _ => Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location), folderName),
        };
        
        switch (type)
        {
            case "General":
                directoryInfo = new DirectoryInfo(Path.Combine(folderLoc, "Taunt - General"));
                return directoryInfo.GetFiles("*.*");
            case "Aggro":
                directoryInfo = new DirectoryInfo(Path.Combine(folderLoc, "Taunt - Aggro"));
                return directoryInfo.GetFiles("*.*");
            case "Enemy":
                directoryInfo = new DirectoryInfo(Path.Combine(folderLoc, "Taunt - Enemy"));
                return directoryInfo.GetFiles("*.*");
            case "PlayerDeath":
                directoryInfo = new DirectoryInfo(Path.Combine(folderLoc, "Taunt - Player Death"));
                return directoryInfo.GetFiles("*.*");
            case "GargoyleDeath":
                directoryInfo = new DirectoryInfo(Path.Combine(folderLoc, "Taunt - Gargoyle Death"));
                return directoryInfo.GetFiles("*.*");
            case "PriorDeath":
                directoryInfo = new DirectoryInfo(Path.Combine(folderLoc, "Taunt - Prior Death"));
                return directoryInfo.GetFiles("*.*");
            case "Coroner":
                directoryInfo = new DirectoryInfo(Path.Combine(folderLoc, "Taunt - Prior Death", "Coroner"));
                return directoryInfo.GetFiles("*.*");
            case "Class":
                directoryInfo = new DirectoryInfo(Path.Combine(folderLoc, "Taunt - EmployeeClass"));
                return directoryInfo.GetFiles("*.*");
            case "Activity":
                directoryInfo = new DirectoryInfo(Path.Combine(folderLoc, "Taunt - Activity"));
                return directoryInfo.GetFiles("*.*");
            case "Attack":
                directoryInfo = new DirectoryInfo(Path.Combine(folderLoc, "Combat Dialog", "Attack"));
                return directoryInfo.GetFiles("*.*");
            case "Hit":
                directoryInfo = new DirectoryInfo(Path.Combine(folderLoc, "Combat Dialog", "Hit"));
                return directoryInfo.GetFiles("*.*");
            case "SteamIDs":
                directoryInfo = new DirectoryInfo(Path.Combine(folderLoc, "Taunt - SteamIDs"));
                return directoryInfo.GetFiles("*.*");
        }
        return [];
    }

    [ClientRpc]
    private void SetClientReadyClientRpc(bool isReady, ulong clientId)
    {
        // No need to access a NetworkVariable, just update the dictionary
        if (clientReady.ContainsKey(NetworkManager.Singleton.LocalClientId) &&
            NetworkManager.Singleton.LocalClientId == clientId)
        {
            clientReady[NetworkManager.Singleton.LocalClientId] = isReady;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetClientReadyServerRpc(bool isReady, ulong clientId)
    {
        // Update the client's readiness in the dictionary on the server
        if (clientReady.ContainsKey(clientId))
        {
            clientReady[clientId] = isReady;
        }
    }
}