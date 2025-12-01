using UnityEngine;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Collections;
using System;
using System.Reflection;
using HarmonyLib;

namespace WorldBoxMultiplayer
{
    public class SaveTransferHandler : MonoBehaviour
    {
        public static SaveTransferHandler Instance;
        
        private const int CHUNK_SIZE = 16384; // 16KB
        // USA LO SLOT 15 (Uno slot sicuro che il gioco riconosce sicuramente)
        private const int SYNC_SLOT_ID = 15; 
        
        public bool IsTransferring = false;
        public float Progress = 0f;
        
        private Dictionary<int, byte[]> _receivedChunks = new Dictionary<int, byte[]>();
        private int _totalChunks = 0;
        private int _receivedCount = 0;

        void Awake() { Instance = this; }

        // --- HOST: SALVA E INVIA ---

        public void StartTransfer()
        {
            if (IsTransferring) { Debug.LogWarning("[Sync] Transfer busy!"); return; }
            IsTransferring = true;
            Progress = 0f;
            
            // PAUSE GAME ON HOST to ensure state doesn't change during transfer
            Config.paused = true;
            
            Debug.Log($"[Sync] 1. Saving current world to SLOT {SYNC_SLOT_ID}...");
            
            // 1. Chiedi al gioco di salvare sullo slot 15
            bool saveTriggered = CallGameSave(SYNC_SLOT_ID);
            if (!saveTriggered) Debug.LogError("[Sync] ERROR: SaveManager methods not found!");

            // 2. Aspetta che il file venga scritto su disco
            StartCoroutine(WaitForFileAndSend());
        }

        // ... (WaitForFileAndSend remains same) ...



        private bool CallGameSave(int slot)
        {
            try {
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType == null) {
                    Debug.LogError("[Sync] SaveManager type not found!");
                    return false;
                }

                // Find instance
                UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
                if (instance == null) {
                    Debug.LogError("[Sync] SaveManager instance not found!");
                    return false;
                }

                var trav = Traverse.Create(instance);

                // 1. Set Slot
                Debug.Log($"[Sync] Setting slot to {slot}...");
                trav.Method("setCurrentSlot", new object[] { slot }).GetValue();

                // 2. Save
                Debug.Log("[Sync] Calling saveToCurrentPath...");
                trav.Method("saveToCurrentPath").GetValue();
                
                return true;

            } catch (Exception e) { Debug.LogError($"[Sync] Save Call Exception: {e.Message}"); }
            return false;
        }

        private string GetSavePath(int slot)
        {
            try {
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType != null) {
                    UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
                    if (instance != null) {
                        var trav = Traverse.Create(instance);
                        // Try getSlotPathWbox first (most specific)
                        if (trav.Method("getSlotPathWbox", new object[] { slot }).MethodExists()) {
                            string p = trav.Method("getSlotPathWbox", new object[] { slot }).GetValue<string>();
                            Debug.Log($"[Sync] Game returned path (Wbox): {p}");
                            return p;
                        }
                        // Try getSlotSavePath
                        if (trav.Method("getSlotSavePath", new object[] { slot }).MethodExists()) {
                            string p = trav.Method("getSlotSavePath", new object[] { slot }).GetValue<string>();
                            Debug.Log($"[Sync] Game returned path (SavePath): {p}");
                            return p;
                        }
                    }
                }
            } catch (Exception e) { Debug.LogError($"[Sync] Path Reflection Error: {e.Message}"); }

            // Fallback
            return Path.Combine(Application.persistentDataPath, "saves", "save" + slot + ".wbox");
        }

        private IEnumerator WaitForFileAndSend()
        {
            string path = GetSavePath(SYNC_SLOT_ID);
            Debug.Log($"[Sync] 2. Looking for save file at: {path}");

            float timeout = 10f; 
            bool fileFound = false;
            
            while (timeout > 0)
            {
                if (File.Exists(path))
                {
                    DateTime lastWrite = File.GetLastWriteTime(path);
                    if ((DateTime.Now - lastWrite).TotalSeconds < 20)
                    {
                        fileFound = true;
                        break;
                    }
                }
                timeout -= 0.5f;
                yield return new WaitForSeconds(0.5f);
            }

            if (!fileFound)
            {
                Debug.LogError($"[Sync] FATAL: Save file not found at {path}");
                
                // Debug: List files in directory
                try {
                    string dir = Path.GetDirectoryName(path);
                    if (Directory.Exists(dir)) {
                        Debug.Log($"[Sync] Listing files in {dir}:");
                        foreach (var f in Directory.GetFiles(dir)) Debug.Log($" - {Path.GetFileName(f)}");
                    } else {
                        Debug.LogError($"[Sync] Directory does not exist: {dir}");
                    }
                } catch {}

                IsTransferring = false;
                NetworkManager.Instance.Disconnect(); 
                yield break;
            }

            byte[] rawData = null;
            try {
                rawData = File.ReadAllBytes(path);
            } catch (Exception e) {
                Debug.LogError($"[Sync] File Read Error: {e.Message}");
                IsTransferring = false;
                NetworkManager.Instance.Disconnect();
                yield break;
            }

            Debug.Log($"[Sync] 3. Read {rawData.Length} bytes. Compressing...");
            byte[] compressedData = Compress(rawData);
            Debug.Log($"[Sync] 4. Compressed to {compressedData.Length} bytes. Sending...");

            StartCoroutine(SendFileRoutine(compressedData));
        }

        private IEnumerator SendFileRoutine(byte[] data)
        {
            int totalChunks = Mathf.CeilToInt((float)data.Length / CHUNK_SIZE);
            
            // Invia Header
            NetworkManager.Instance.SendRaw($"FILE_START|{totalChunks}|{data.Length}\n");
            yield return new WaitForSeconds(0.5f);

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * CHUNK_SIZE;
                int size = Mathf.Min(CHUNK_SIZE, data.Length - offset);
                byte[] chunk = new byte[size];
                Array.Copy(data, offset, chunk, 0, size);
                
                string b64 = Convert.ToBase64String(chunk);
                NetworkManager.Instance.SendRaw($"FILE_DATA|{i}|{b64}\n");
                
                Progress = (float)i / totalChunks;
                
                // Invia velocemente (50 chunk per frame)
                if (i % 50 == 0) yield return null; 
            }
            
            IsTransferring = false;
            Debug.Log("[Sync] UPLOAD COMPLETE.");
        }

        // --- CLIENT: RICEZIONE ---

        public void OnReceiveStart(int totalChunks, int totalBytes)
        {
            Debug.Log($"[Sync] DOWNLOAD STARTED: {totalBytes} bytes.");
            IsTransferring = true;
            _totalChunks = totalChunks;
            _receivedChunks.Clear();
            _receivedCount = 0;
            Progress = 0f;
            
            Config.paused = true;
            NetworkManager.Instance.IsMapLoaded = false;
            WorldBoxMultiplayer.instance.UpdateStatus($"Downloading Map ({totalChunks})...");
        }

        public void OnReceiveChunk(int index, string dataB64)
        {
            if (!IsTransferring) return;

            if (!_receivedChunks.ContainsKey(index)) {
                try {
                    _receivedChunks[index] = Convert.FromBase64String(dataB64);
                    _receivedCount++;
                    Progress = (float)_receivedCount / _totalChunks;
                } catch { Debug.LogError($"[Sync] Chunk {index} Corrupt"); }
            }

            if (_receivedCount >= _totalChunks) FinishReception();
        }

        private void FinishReception()
        {
            IsTransferring = false;
            Debug.Log("[Sync] Download finished. Installing map...");
            WorldBoxMultiplayer.instance.UpdateStatus("Loading World...");

            try {
                using (MemoryStream ms = new MemoryStream()) {
                    for (int i = 0; i < _totalChunks; i++) {
                        if (_receivedChunks.ContainsKey(i)) ms.Write(_receivedChunks[i], 0, _receivedChunks[i].Length);
                        else { Debug.LogError($"[Sync] MISSING CHUNK {i}"); return; }
                    }
                    
                    byte[] compressedData = ms.ToArray();
                    byte[] rawData = Decompress(compressedData);
                    
                    string path = GetSavePath(SYNC_SLOT_ID);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, rawData);
                }

                // Carica lo slot 15
                CallGameLoad(SYNC_SLOT_ID);
                
                // Wait for game to finish loading
                StartCoroutine(WaitForMapLoad());

            } catch (Exception e) {
                Debug.LogError("[Sync] Load Error: " + e.Message);
                NetworkManager.Instance.IsMapLoaded = true; 
                NetworkManager.Instance.Disconnect(); 
            }
        }

        private IEnumerator WaitForMapLoad()
        {
            Debug.Log("[Sync] Waiting for game to load...");
            yield return new WaitForSeconds(1f); // Give time for loading to start

            // Wait while loading
            while (IsGameLoading()) {
                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log("[Sync] MAP LOADED! Unpausing...");
            NetworkManager.Instance.IsMapLoaded = true;
            LockstepController.Instance.CurrentTick = 0;
            WorldBoxMultiplayer.instance.UpdateStatus("Connected & Synced");
            
            // Unpause client
            Config.paused = false;
        }

        private bool IsGameLoading()
        {
            try {
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType != null) {
                    UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
                    if (instance != null) {
                        return Traverse.Create(instance).Method("isLoadingSaveAnimationActive").GetValue<bool>();
                    }
                }
            } catch {}
            return false;
        }

        private void CallGameLoad(int slot)
        {
            try {
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType == null) return;

                UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
                if (instance != null) {
                    var trav = Traverse.Create(instance);
                    
                    // 1. Set Slot
                    Debug.Log($"[Sync] Setting load slot to {slot}...");
                    trav.Method("setCurrentSlot", new object[] { slot }).GetValue();

                    // 2. Load
                    Debug.Log("[Sync] Calling startLoadSlot...");
                    trav.Method("startLoadSlot").GetValue();
                }
            } catch (Exception e) { Debug.LogError($"[Sync] LoadCall Error: {e.Message}"); }
        }

        private byte[] Compress(byte[] data) {
            using (MemoryStream output = new MemoryStream()) {
                using (GZipStream dstream = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest)) { dstream.Write(data, 0, data.Length); }
                return output.ToArray();
            }
        }
        private byte[] Decompress(byte[] data) {
            using (MemoryStream input = new MemoryStream(data))
            using (GZipStream dstream = new GZipStream(input, System.IO.Compression.CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream()) { dstream.CopyTo(output); return output.ToArray(); }
        }
    }
}