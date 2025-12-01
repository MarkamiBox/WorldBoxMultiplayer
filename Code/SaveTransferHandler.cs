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
        private const int CHUNK_SIZE = 32768; // 32KB per pacchetto (più veloce)
        private const string SYNC_SLOT = "mp_sync_slot";
        public bool IsTransferring = false;
        public float Progress = 0f;
        
        private Dictionary<int, byte[]> _receivedChunks = new Dictionary<int, byte[]>();
        private int _totalChunks = 0;
        private int _receivedCount = 0;

        void Awake() { Instance = this; }

        // --- HOST LOGIC ---

        public void StartTransfer()
        {
            if (IsTransferring) return;
            IsTransferring = true;
            Progress = 0f;
            
            Debug.Log("[Sync] STARTING SAVE TRANSFER...");

            // 1. Forza il salvataggio del mondo attuale
            bool saveSuccess = CallGameSave(SYNC_SLOT);
            if (!saveSuccess) {
                Debug.LogError("[Sync] FAILED to save game locally. Aborting.");
                IsTransferring = false;
                return;
            }
            
            // 2. Trova il file
            string path = GetSavePath(SYNC_SLOT);
            if (!File.Exists(path)) { 
                Debug.LogError($"[Sync] Save file not found at: {path}");
                IsTransferring = false; 
                return; 
            }

            // 3. Leggi e Comprimi
            try {
                byte[] rawData = File.ReadAllBytes(path);
                byte[] compressedData = Compress(rawData);
                Debug.Log($"[Sync] File ready. Size: {rawData.Length} -> {compressedData.Length} bytes");

                // 4. Invia
                StartCoroutine(SendFileRoutine(compressedData));
            } catch (Exception e) {
                Debug.LogError($"[Sync] Error preparing file: {e.Message}");
                IsTransferring = false;
            }
        }

        private bool CallGameSave(string slot)
        {
            try {
                // Metodo universale per trovare SaveManager (Statico o Istanza)
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType == null) return false;

                // Prova metodo statico
                MethodInfo method = AccessTools.Method(saveMgrType, "saveGame");
                if (method != null) {
                    method.Invoke(null, new object[] { slot });
                    return true;
                }

                // Prova istanza (MonoBehaviour)
                UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
                if (instance != null) {
                    Traverse.Create(instance).Method("saveGame", new object[] { slot }).GetValue();
                    return true;
                }
            } catch (Exception e) { Debug.LogError($"[Sync] Save Call Error: {e.Message}"); }
            return false;
        }

        private string GetSavePath(string slot)
        {
            // Percorso standard di WorldBox
            return System.IO.Path.Combine(Application.persistentDataPath, "saves", $"{slot}.wbox");
        }

        private System.Collections.IEnumerator SendFileRoutine(byte[] data)
        {
            int totalChunks = Mathf.CeilToInt((float)data.Length / CHUNK_SIZE);
            // Invia Header
            NetworkManager.Instance.SendRaw($"FILE_START|{totalChunks}|{data.Length}\n");
            yield return new WaitForSeconds(0.2f); // Pausa tecnica

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * CHUNK_SIZE;
                int size = Mathf.Min(CHUNK_SIZE, data.Length - offset);
                byte[] chunk = new byte[size];
                Array.Copy(data, offset, chunk, 0, size);
                
                string b64 = Convert.ToBase64String(chunk);
                NetworkManager.Instance.SendRaw($"FILE_DATA|{i}|{b64}\n");
                
                Progress = (float)i / totalChunks;
                
                // Invia più velocemente (ogni 2 frame)
                if (i % 2 == 0) yield return null; 
            }
            
            IsTransferring = false;
            Debug.Log("[Sync] TRANSFER COMPLETE.");
        }

        // --- CLIENT LOGIC ---

        public void OnReceiveStart(int totalChunks, int totalBytes)
        {
            Debug.Log($"[Sync] Receiving map... {totalBytes} bytes / {totalChunks} chunks");
            IsTransferring = true;
            _totalChunks = totalChunks;
            _receivedChunks.Clear();
            _receivedCount = 0;
            Progress = 0f;
            
            // Blocca il gioco durante il download per evitare desync
            Config.paused = true;
            NetworkManager.Instance.IsMapLoaded = false;
        }

        public void OnReceiveChunk(int index, string dataB64)
        {
            if (!IsTransferring) return; // Ignora se non stiamo aspettando file

            if (!_receivedChunks.ContainsKey(index)) {
                try {
                    _receivedChunks[index] = Convert.FromBase64String(dataB64);
                    _receivedCount++;
                    Progress = (float)_receivedCount / _totalChunks;
                } catch { Debug.LogError("[Sync] Chunk corrupted"); }
            }

            if (_receivedCount >= _totalChunks) FinishReception();
        }

        private void FinishReception()
        {
            IsTransferring = false;
            Debug.Log("[Sync] Download finished. Reconstructing...");

            try {
                using (MemoryStream ms = new MemoryStream()) {
                    for (int i = 0; i < _totalChunks; i++) 
                        if (_receivedChunks.ContainsKey(i)) ms.Write(_receivedChunks[i], 0, _receivedChunks[i].Length);
                        else Debug.LogError($"[Sync] MISSING CHUNK {i}");
                    
                    byte[] compressedData = ms.ToArray();
                    byte[] rawData = Decompress(compressedData);
                    
                    string path = GetSavePath(SYNC_SLOT);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)); // Crea cartella se manca
                    File.WriteAllBytes(path, rawData);
                }

                Debug.Log("[Sync] Loading World...");
                CallGameLoad(SYNC_SLOT);
                
                Debug.Log("[Sync] GAME READY.");
                NetworkManager.Instance.IsMapLoaded = true;
                // Resetta il tempo per allineare il Lockstep
                LockstepController.Instance.CurrentTick = 0;

            } catch (Exception e) {
                Debug.LogError("[Sync] CRITICAL LOAD ERROR: " + e.Message);
            }
        }

        private void CallGameLoad(string slot)
        {
            Type saveMgrType = AccessTools.TypeByName("SaveManager");
            if (saveMgrType == null) return;

            // Prova istanza
            UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
            if (instance != null) {
                Traverse.Create(instance).Method("loadGame", new object[] { slot }).GetValue();
                return;
            }
            // Prova statico
            MethodInfo method = AccessTools.Method(saveMgrType, "loadGame");
            if (method != null) method.Invoke(null, new object[] { slot });
        }

        // Compression Helpers
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