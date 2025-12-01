using UnityEngine;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System;
using System.Reflection;
using HarmonyLib;

namespace WorldBoxMultiplayer
{
    public class SaveTransferHandler : MonoBehaviour
    {
        public static SaveTransferHandler Instance;
        private const int CHUNK_SIZE = 65536; // 64KB
        private const string SYNC_SLOT = "save999"; // Usiamo un nome slot standard per evitare errori del gioco

        public bool IsTransferring = false;
        public float Progress = 0f;
        
        private Dictionary<int, byte[]> _receivedChunks = new Dictionary<int, byte[]>();
        private int _totalChunks = 0;
        private int _receivedCount = 0;

        void Awake() { Instance = this; }

        public void StartTransfer()
        {
            if (IsTransferring) {
                Debug.LogWarning("[Sync] Transfer already running!");
                return;
            }
            IsTransferring = true;
            Progress = 0f;
            
            Debug.Log($"[Sync] 1. Saving Game to slot '{SYNC_SLOT}'...");
            
            // Tenta di salvare
            bool saveOk = CallGameSave(SYNC_SLOT);
            if (!saveOk) {
                Debug.LogError("[Sync] FAIL: SaveManager.saveGame returned false/error.");
                IsTransferring = false;
                return;
            }

            // Cerca il file
            string path = GetSavePath(SYNC_SLOT);
            Debug.Log($"[Sync] 2. Looking for file at: {path}");
            
            if (!File.Exists(path)) { 
                Debug.LogError($"[Sync] FAIL: File not found after save!");
                IsTransferring = false; 
                return; 
            }

            try {
                byte[] rawData = File.ReadAllBytes(path);
                Debug.Log($"[Sync] 3. File read. Size: {rawData.Length} bytes. Compressing...");
                
                byte[] compressedData = Compress(rawData);
                Debug.Log($"[Sync] 4. Compressed size: {compressedData.Length} bytes. Sending...");

                StartCoroutine(SendFileRoutine(compressedData));
            } catch (Exception e) {
                Debug.LogError($"[Sync] CRITICAL ERROR: {e.Message}");
                IsTransferring = false;
            }
        }

        private bool CallGameSave(string slot)
        {
            try {
                // Metodo 1: Reflection su istanza (più probabile)
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType != null) {
                    UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
                    if (instance != null) {
                        Traverse.Create(instance).Method("saveGame", new object[] { slot }).GetValue();
                        return true;
                    }
                    // Metodo 2: Statico
                    MethodInfo method = AccessTools.Method(saveMgrType, "saveGame");
                    if (method != null) { method.Invoke(null, new object[] { slot }); return true; }
                }
            } catch (Exception e) { Debug.LogError($"[Sync] CallGameSave Exception: {e.Message}"); }
            return false;
        }

        private string GetSavePath(string slot)
        {
            return System.IO.Path.Combine(Application.persistentDataPath, "saves", $"{slot}.wbox");
        }

        private System.Collections.IEnumerator SendFileRoutine(byte[] data)
        {
            int totalChunks = Mathf.CeilToInt((float)data.Length / CHUNK_SIZE);
            Debug.Log($"[Sync] Sending HEADER. Chunks: {totalChunks}");
            
            NetworkManager.Instance.SendRaw($"FILE_START|{totalChunks}|{data.Length}\n");
            yield return new WaitForSeconds(0.5f); // Attesa più lunga per l'header

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * CHUNK_SIZE;
                int size = Mathf.Min(CHUNK_SIZE, data.Length - offset);
                byte[] chunk = new byte[size];
                Array.Copy(data, offset, chunk, 0, size);
                
                string b64 = Convert.ToBase64String(chunk);
                NetworkManager.Instance.SendRaw($"FILE_DATA|{i}|{b64}\n");
                
                Progress = (float)i / totalChunks;
                if (i % 20 == 0) {
                    Debug.Log($"[Sync] Sending Chunk {i}/{totalChunks}...");
                    yield return null;
                }
            }
            
            IsTransferring = false;
            Debug.Log("[Sync] SEND COMPLETE!");
        }

        public void OnReceiveStart(int totalChunks, int totalBytes)
        {
            Debug.Log($"[Sync] RECEIVE START! Expecting {totalBytes} bytes.");
            IsTransferring = true;
            _totalChunks = totalChunks;
            _receivedChunks.Clear();
            _receivedCount = 0;
            Progress = 0f;
            
            Config.paused = true;
            NetworkManager.Instance.IsMapLoaded = false;
            WorldBoxMultiplayer.instance.UpdateStatus("Downloading Map...");
        }

        public void OnReceiveChunk(int index, string dataB64)
        {
            if (!IsTransferring) return;

            if (!_receivedChunks.ContainsKey(index)) {
                try {
                    _receivedChunks[index] = Convert.FromBase64String(dataB64);
                    _receivedCount++;
                    Progress = (float)_receivedCount / _totalChunks;
                    
                    // Log ogni 10%
                    if (_receivedCount % Mathf.Max(1, _totalChunks/10) == 0) 
                        Debug.Log($"[Sync] Download: {Progress*100:F0}%");

                } catch { Debug.LogError($"[Sync] Chunk {index} CORRUPT!"); }
            }

            if (_receivedCount >= _totalChunks) FinishReception();
        }

        private void FinishReception()
        {
            IsTransferring = false;
            Debug.Log("[Sync] DOWNLOAD DONE. Reconstructing...");
            WorldBoxMultiplayer.instance.UpdateStatus("Loading...");

            try {
                using (MemoryStream ms = new MemoryStream()) {
                    for (int i = 0; i < _totalChunks; i++) {
                        if (_receivedChunks.ContainsKey(i)) ms.Write(_receivedChunks[i], 0, _receivedChunks[i].Length);
                        else { Debug.LogError($"[Sync] MISSING CHUNK {i}!"); return; }
                    }
                    
                    byte[] compressedData = ms.ToArray();
                    byte[] rawData = Decompress(compressedData);
                    
                    string path = GetSavePath(SYNC_SLOT);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, rawData);
                }

                Debug.Log("[Sync] Loading into game...");
                CallGameLoad(SYNC_SLOT);
                
                Debug.Log("[Sync] GAME READY.");
                NetworkManager.Instance.IsMapLoaded = true;
                LockstepController.Instance.CurrentTick = 0;
                WorldBoxMultiplayer.instance.UpdateStatus("Connected & Synced");

            } catch (Exception e) {
                Debug.LogError("[Sync] LOAD ERROR: " + e.Message);
                NetworkManager.Instance.IsMapLoaded = true; // Sblocca per evitare softlock
            }
        }

        private void CallGameLoad(string slot)
        {
            try {
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType == null) return;
                UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
                if (instance != null) { Traverse.Create(instance).Method("loadGame", new object[] { slot }).GetValue(); return; }
                MethodInfo method = AccessTools.Method(saveMgrType, "loadGame");
                if (method != null) method.Invoke(null, new object[] { slot });
            } catch (Exception e) { Debug.LogError("[Sync] CallGameLoad Error: " + e.Message); }
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