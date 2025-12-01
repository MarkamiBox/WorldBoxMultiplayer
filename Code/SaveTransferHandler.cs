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
            
            Debug.Log($"[Sync] 1. Saving current world to SLOT {SYNC_SLOT_ID}...");
            
            // 1. Chiedi al gioco di salvare sullo slot 15
            bool saveTriggered = CallGameSave(SYNC_SLOT_ID);
            if (!saveTriggered) Debug.LogError("[Sync] ERROR: SaveManager methods not found!");

            // 2. Aspetta che il file venga scritto su disco
            StartCoroutine(WaitForFileAndSend());
        }

        private IEnumerator WaitForFileAndSend()
        {
            string path = GetSavePath(SYNC_SLOT_ID);
            Debug.Log($"[Sync] 2. Looking for save file at: {path}");

            // Attesa attiva (Max 5 secondi)
            float timeout = 5f;
            bool fileFound = false;
            
            while (timeout > 0)
            {
                if (File.Exists(path))
                {
                    // Verifica che il file sia recente (scritto negli ultimi 10 secondi)
                    DateTime lastWrite = File.GetLastWriteTime(path);
                    if ((DateTime.Now - lastWrite).TotalSeconds < 10)
                    {
                        fileFound = true;
                        break;
                    }
                }
                timeout -= 0.2f;
                yield return new WaitForSeconds(0.2f);
            }

            if (!fileFound)
            {
                Debug.LogError("[Sync] FATAL: Save file not found or too old. The game failed to save.");
                IsTransferring = false;
                yield break;
            }

            // 3. Leggi il file
            byte[] rawData = null;
            try {
                rawData = File.ReadAllBytes(path);
            } catch (Exception e) {
                Debug.LogError($"[Sync] File Read Error: {e.Message}");
                IsTransferring = false;
                yield break;
            }

            Debug.Log($"[Sync] 3. Read {rawData.Length} bytes. Compressing...");
            byte[] compressedData = Compress(rawData);
            Debug.Log($"[Sync] 4. Compressed to {compressedData.Length} bytes. Sending...");

            StartCoroutine(SendFileRoutine(compressedData));
        }

        private bool CallGameSave(int slot)
        {
            try {
                // Usa Reflection per trovare SaveManager (funziona su diverse versioni del gioco)
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType == null) return false;

                // Prova metodo statico: SaveManager.saveGame(int)
                MethodInfo method = AccessTools.Method(saveMgrType, "saveGame", new Type[] { typeof(int) });
                if (method != null) {
                    method.Invoke(null, new object[] { slot });
                    return true;
                }

                // Prova istanza
                UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
                if (instance != null) {
                    Traverse.Create(instance).Method("saveGame", new object[] { slot }).GetValue();
                    return true;
                }
            } catch (Exception e) { Debug.LogError($"[Sync] Save Call Exception: {e.Message}"); }
            return false;
        }

        private string GetSavePath(int slot)
        {
            // Costruisci il percorso standard: .../saves/save15.wbox
            return Path.Combine(Application.persistentDataPath, "saves", "save" + slot + ".wbox");
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
                
                Debug.Log("[Sync] MAP LOADED!");
                NetworkManager.Instance.IsMapLoaded = true;
                LockstepController.Instance.CurrentTick = 0;
                WorldBoxMultiplayer.instance.UpdateStatus("Connected & Synced");

            } catch (Exception e) {
                Debug.LogError("[Sync] Load Error: " + e.Message);
                NetworkManager.Instance.IsMapLoaded = true; // Sblocca comunque per non freezare
            }
        }

        private void CallGameLoad(int slot)
        {
            try {
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType == null) return;

                MethodInfo method = AccessTools.Method(saveMgrType, "loadGame", new Type[] { typeof(int) });
                if (method != null) { method.Invoke(null, new object[] { slot }); return; }

                UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
                if (instance != null) {
                    Traverse.Create(instance).Method("loadGame", new object[] { slot }).GetValue();
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