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
        
        // AUMENTATO CHUNK SIZE: 16KB -> 64KB per ridurre il numero di pacchetti e l'overhead
        private const int CHUNK_SIZE = 65536; 
        private const string SYNC_SLOT = "mp_sync_slot";

        public bool IsTransferring = false;
        public float Progress = 0f;
        
        private Dictionary<int, byte[]> _receivedChunks = new Dictionary<int, byte[]>();
        private int _totalChunks = 0;
        private int _receivedCount = 0;
        private System.Diagnostics.Stopwatch _timer;

        void Awake() { Instance = this; }

        public void StartTransfer()
        {
            if (IsTransferring) { Debug.LogWarning("[Sync] Transfer already in progress!"); return; }
            IsTransferring = true;
            Progress = 0f;
            _timer = System.Diagnostics.Stopwatch.StartNew();
            
            Debug.Log($"[Sync {GetTime()}] STARTING SAVE TRANSFER...");

            // 1. SALVATAGGIO
            bool saveSuccess = CallGameSave(SYNC_SLOT);
            if (!saveSuccess) {
                Debug.LogError("[Sync] FAILED to save game locally.");
                IsTransferring = false;
                return;
            }
            Debug.Log($"[Sync {GetTime()}] Game Saved locally.");
            
            // 2. LETTURA FILE
            string path = GetSavePath(SYNC_SLOT);
            if (!File.Exists(path)) { 
                Debug.LogError($"[Sync] File not found at: {path}");
                IsTransferring = false; 
                return; 
            }

            try {
                byte[] rawData = File.ReadAllBytes(path);
                Debug.Log($"[Sync {GetTime()}] Read file: {rawData.Length / 1024} KB");

                // 3. COMPRESSIONE
                byte[] compressedData = Compress(rawData);
                Debug.Log($"[Sync {GetTime()}] Compressed to: {compressedData.Length / 1024} KB ({(float)compressedData.Length/rawData.Length:P})");

                StartCoroutine(SendFileRoutine(compressedData));
            } catch (Exception e) {
                Debug.LogError($"[Sync] Error preparing file: {e.Message}");
                IsTransferring = false;
            }
        }

        private string GetTime() { return _timer != null ? (_timer.ElapsedMilliseconds / 1000f).ToString("F2") + "s" : "0.00s"; }

        private bool CallGameSave(string slot)
        {
            try {
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType == null) return false;
                MethodInfo method = AccessTools.Method(saveMgrType, "saveGame");
                if (method != null) { method.Invoke(null, new object[] { slot }); return true; }
                UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
                if (instance != null) { Traverse.Create(instance).Method("saveGame", new object[] { slot }).GetValue(); return true; }
            } catch (Exception e) { Debug.LogError($"[Sync] Save Call Error: {e.Message}"); }
            return false;
        }

        private string GetSavePath(string slot)
        {
            return System.IO.Path.Combine(Application.persistentDataPath, "saves", $"{slot}.wbox");
        }

        private System.Collections.IEnumerator SendFileRoutine(byte[] data)
        {
            int totalChunks = Mathf.CeilToInt((float)data.Length / CHUNK_SIZE);
            
            Debug.Log($"[Sync {GetTime()}] Sending Header. Total Chunks: {totalChunks}");
            NetworkManager.Instance.SendRaw($"FILE_START|{totalChunks}|{data.Length}\n");
            
            // Pausa per essere sicuri che l'header arrivi prima dei dati
            yield return new WaitForSeconds(0.5f);

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * CHUNK_SIZE;
                int size = Mathf.Min(CHUNK_SIZE, data.Length - offset);
                byte[] chunk = new byte[size];
                Array.Copy(data, offset, chunk, 0, size);
                
                string b64 = Convert.ToBase64String(chunk);
                
                // Log ogni 10 chunk per non intasare la console, ma vedere che avanza
                if (i % 10 == 0 || i == totalChunks - 1) 
                    Debug.Log($"[Sync {GetTime()}] Sending Chunk {i}/{totalChunks} (Size: {b64.Length})");

                NetworkManager.Instance.SendRaw($"FILE_DATA|{i}|{b64}\n");
                
                Progress = (float)i / totalChunks;
                
                // OTTIMIZZAZIONE: Inviamo 50 chunk per frame invece di 5. 
                // Se la mappa è grande, 5 era TROPPO lento.
                if (i % 50 == 0) yield return null; 
            }
            
            IsTransferring = false;
            Debug.Log($"[Sync {GetTime()}] TRANSFER COMPLETE. All chunks sent.");
        }

        public void OnReceiveStart(int totalChunks, int totalBytes)
        {
            Debug.Log($"[Sync] RECEIVE STARTED. Expecting {totalBytes} bytes in {totalChunks} chunks.");
            IsTransferring = true;
            _totalChunks = totalChunks;
            _receivedChunks.Clear();
            _receivedCount = 0;
            Progress = 0f;
            _timer = System.Diagnostics.Stopwatch.StartNew();
            
            Config.paused = true;
            NetworkManager.Instance.IsMapLoaded = false;
            WorldBoxMultiplayer.instance.UpdateStatus($"Downloading Map ({totalChunks} parts)...");
        }

        public void OnReceiveChunk(int index, string dataB64)
        {
            if (!IsTransferring) return;

            if (!_receivedChunks.ContainsKey(index)) {
                try {
                    _receivedChunks[index] = Convert.FromBase64String(dataB64);
                    _receivedCount++;
                    Progress = (float)_receivedCount / _totalChunks;
                    
                    if (_receivedCount % 10 == 0) 
                         Debug.Log($"[Sync {GetTime()}] Received Chunk {index}/{_totalChunks}");
                    
                } catch { Debug.LogError($"[Sync] Chunk {index} CORRUPTED!"); }
            }

            if (_receivedCount >= _totalChunks) FinishReception();
        }

        private void FinishReception()
        {
            IsTransferring = false;
            Debug.Log($"[Sync {GetTime()}] Download finished. Reconstructing file...");
            WorldBoxMultiplayer.instance.UpdateStatus("Reconstructing World...");

            try {
                using (MemoryStream ms = new MemoryStream()) {
                    for (int i = 0; i < _totalChunks; i++) 
                    {
                        if (_receivedChunks.ContainsKey(i)) ms.Write(_receivedChunks[i], 0, _receivedChunks[i].Length);
                        else {
                            Debug.LogError($"[Sync] MISSING CHUNK {i}! Transfer Failed.");
                            WorldBoxMultiplayer.instance.UpdateStatus("Sync Failed: Missing Data");
                            return;
                        }
                    }
                    
                    byte[] compressedData = ms.ToArray();
                    Debug.Log($"[Sync] Decompressing {compressedData.Length} bytes...");
                    byte[] rawData = Decompress(compressedData);
                    
                    string path = GetSavePath(SYNC_SLOT);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, rawData);
                }

                Debug.Log("[Sync] Loading World into Game...");
                WorldBoxMultiplayer.instance.UpdateStatus("Loading World...");
                CallGameLoad(SYNC_SLOT);
                
                Debug.Log("[Sync] GAME READY!");
                NetworkManager.Instance.IsMapLoaded = true;
                LockstepController.Instance.CurrentTick = 0;
                WorldBoxMultiplayer.instance.UpdateStatus("Connected & Synced");

            } catch (Exception e) {
                Debug.LogError("[Sync] CRITICAL LOAD ERROR: " + e.Message);
                Config.paused = false; // Sblocca in caso di errore
                NetworkManager.Instance.IsMapLoaded = true; 
            }
        }

        private void CallGameLoad(string slot)
        {
            Type saveMgrType = AccessTools.TypeByName("SaveManager");
            if (saveMgrType == null) return;

            UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
            if (instance != null) { Traverse.Create(instance).Method("loadGame", new object[] { slot }).GetValue(); return; }
            
            MethodInfo method = AccessTools.Method(saveMgrType, "loadGame");
            if (method != null) method.Invoke(null, new object[] { slot });
        }

        private byte[] Compress(byte[] data) {
            using (MemoryStream output = new MemoryStream()) {
                using (GZipStream dstream = new GZipStream(output, CompressionLevel.Fastest)) { dstream.Write(data, 0, data.Length); }
                return output.ToArray();
            }
        }
        private byte[] Decompress(byte[] data) {
            using (MemoryStream input = new MemoryStream(data))
            using (GZipStream dstream = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream()) { dstream.CopyTo(output); return output.ToArray(); }
        }
    }
}