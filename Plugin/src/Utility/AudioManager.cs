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
    /// <summary>
    /// Per-clip wire size limit. The host pushes raw OGG bytes to each client over
    /// CustomMessagingManager; anything larger than this is skipped for that client.
    /// The HOST must apply the same limit when loading locally, or the two sides end up with
    /// different clip lists and every taunt naming a host-only clip fails silently on clients.
    /// </summary>
    public const int MaxClipBytes = 512000;

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
                        if (audioData?.Length > MaxClipBytes)
                        {
                            // continue, NOT break. A single oversized file used to abandon every
                            // remaining clip in its category for that client, while the host - whose
                            // load path has no size check at all - still had all of them. The two
                            // sides then disagreed about what existed, and every taunt naming a
                            // missing clip failed silently on the client.
                            Plugin.Logger.LogError($"Skipping Clip({clipName}): {audioData?.Length} bytes exceeds the {MaxClipBytes} byte (500KB) limit. Remaining clips in '{category}' will still be sent.");
                            continue;
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
                    if (audioData?.Length > MaxClipBytes)
                    {
                        // continue, not break - see the note above.
                        Plugin.Logger.LogError($"Skipping Clip({clipName}): {audioData?.Length} bytes exceeds the {MaxClipBytes} byte (500KB) limit. Remaining clips in '{category}' will still be sent.");
                        continue;
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

            // NVorbis units: TotalSamples is PER CHANNEL, while ReadSamples' count is a number of
            // INTERLEAVED float values. The old code allocated float[TotalSamples] and read
            // TotalSamples values, which happens to be exactly right for mono (channels == 1) and
            // exactly HALF for stereo - so a stereo clip decoded to half its length and cut off
            // mid-sentence on every client, while the host, which loads through
            // DownloadHandlerAudioClip on a different path, heard it whole.
            int channels = vorbis.Channels;
            int samplesPerChannel = (int)vorbis.TotalSamples;
            var audioBuffer = new float[samplesPerChannel * channels];
            AudioClip clip = AudioClip.Create(clipName, samplesPerChannel, channels, vorbis.SampleRate, false);
            int read = vorbis.ReadSamples(audioBuffer, 0, audioBuffer.Length);
            if (read < audioBuffer.Length)
            {
                LGLog.Warn(LogCat.Audio, $"Clip '{clipName}' decoded short: got {read} of {audioBuffer.Length} samples ({channels}ch). It will play truncated.");
            }
            clip.SetData(audioBuffer, 0); // <-- your clip Remember to destroy when not use anymore
            List<AudioClip> clipList = GetClipListByCategory(category);
            clipList.Add(clip);
            LGLog.Info(LogCat.Audio, $"Clip loaded: {clip.name} ({channels}ch, {vorbis.SampleRate}Hz, {clip.length:0.00}s)");
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

                // Apply the SAME size limit the send path applies. Without this the host loads a
                // clip that no client can ever receive, then names it in a TauntClientRpc that
                // every client fails to resolve - which is invisible from the host's seat.
                try
                {
                    var info = new FileInfo(fileName);
                    if (info.Exists && info.Length > MaxClipBytes)
                    {
                        LGLog.Warn(LogCat.Audio, $"Skipping '{clipName}' locally too: {info.Length} bytes exceeds the {MaxClipBytes} byte limit, so no client can receive it. Re-export it smaller.");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    LGLog.Warn(LogCat.Audio, $"Could not stat '{fileName}': {ex.GetType().Name}. Loading anyway.");
                }

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
        // DEEP COPY. This used to alias Plugin.defaultAudioClipFilePaths by reference and then
        // mutate it, and nothing ever cleared it - so every list this method appended to grew
        // permanently, compounding on every lobby. With Coroner loaded that meant PriorDeath went
        // 140 entries on the first lobby, 201 on the second, 262 on the third, each duplicate
        // costing another AudioClip in host RAM AND another full network push to every client.
        AudioClipFilePaths = [];
        foreach (var cat in defaultAudioClipFilePaths)
        {
            AudioClipFilePaths[cat.Key] = new List<string>(cat.Value);
        }

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
                // NOTE: the Coroner DEFAULTS are deliberately not added here. Plugin's
                // GetDefaultAudioClipFilePaths already includes them, and adding them again is
                // what produced 61 duplicate entries per lobby. This block handles CUSTOM Coroner
                // files only.
                FileInfo[] coronerCustomFiles = GetMP3Files("Coroner", "Custom Voice Lines");

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
        string? folderLoc = folderName switch
        {
            "Voice Lines" => Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location),folderName),
            "Custom Voice Lines" => Plugin.CustomAudioFolderPath,
            _ => Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location), folderName),
        };
        
        // One table, one guarded read. This was twelve near-identical cases each calling
        // GetFiles("*.*") on an unchecked DirectoryInfo, which had three problems:
        //   1. "*.*" picked up Thumbs.db, .txt, .reapeaks - each minting a config toggle and an
        //      "Unsupported audio file format" error. Only OGG survives the pipeline.
        //   2. A missing folder threw DirectoryNotFoundException, which escaped OnNetworkSpawn
        //      BEFORE LoadAudioClipsFromConfig() ran - so the host loaded ZERO clips and the
        //      gargoyle went mute for the entire lobby, with only a raw stack trace to show why.
        //   3. There was no "EmployeeClass" case, so three call sites asking for it silently got
        //      an empty array. "Class" reads the same folder, which is the only reason custom
        //      EmployeeClass lines worked at all.
        string? subPath = type switch
        {
            "General" => Path.Combine("Taunt - General"),
            "Aggro" => Path.Combine("Taunt - Aggro"),
            "Enemy" => Path.Combine("Taunt - Enemy"),
            "PlayerDeath" => Path.Combine("Taunt - Player Death"),
            "GargoyleDeath" => Path.Combine("Taunt - Gargoyle Death"),
            "PriorDeath" => Path.Combine("Taunt - Prior Death"),
            "Coroner" => Path.Combine("Taunt - Prior Death", "Coroner"),
            "Class" or "EmployeeClass" => Path.Combine("Taunt - EmployeeClass"),
            "Activity" => Path.Combine("Taunt - Activity"),
            "Attack" => Path.Combine("Combat Dialog", "Attack"),
            "Hit" => Path.Combine("Combat Dialog", "Hit"),
            "SteamIDs" => Path.Combine("Taunt - SteamIDs"),
            _ => null,
        };

        if (subPath == null)
        {
            LGLog.Warn(LogCat.Audio, $"Unknown voice line category '{type}' requested - no clips will load for it. This is a code bug, not a user error.");
            return [];
        }

        string full = Path.Combine(folderLoc, subPath);
        try
        {
            var dir = new DirectoryInfo(full);
            if (!dir.Exists)
            {
                // Not an error for the Custom folder - a player simply may not have added any.
                LGLog.Debug(LogCat.Audio, $"Voice line folder missing, skipping: {full}");
                return [];
            }
            return dir.GetFiles("*.ogg");
        }
        catch (Exception ex)
        {
            LGLog.Warn(LogCat.Audio, $"Could not read voice line folder '{full}': {ex.GetType().Name}: {ex.Message}. That category will be empty rather than silencing the whole mod.");
            return [];
        }
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