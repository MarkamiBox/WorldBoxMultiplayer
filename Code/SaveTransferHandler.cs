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
        
        // Ridotto per stabilità
        private const int CHUNK_SIZE = 16384; 
        private const string SYNC_SLOT = "save999"; 

        public bool IsTransferring = false;
        public float Progress = 0f;
        
        private Dictionary<int, byte[]> _receivedChunks = new Dictionary<int, byte[]>();
        private int _totalChunks = 0;
        private int _receivedCount = 0;

        void Awake() { Instance = this; }

        // --- HOST: INVIO ---

        public void StartTransfer()
        {
            if (IsTransferring) return;
            StartCoroutine(TransferRoutine());
        }

        private IEnumerator TransferRoutine()
        {
            IsTransferring = true;
            Progress = 0f;
            Debug.Log("[Sync] 1. Tentativo salvataggio...");

            // 1. Chiama il salvataggio del gioco
            bool saveCallSuccess = CallGameSave(SYNC_SLOT);
            if (!saveCallSuccess) Debug.LogWarning("[Sync] SaveManager non trovato, provo comunque a cercare il file...");

            // 2. Attesa attiva del file (max 2 secondi)
            string path = GetSavePath(SYNC_SLOT);
            Debug.Log($"[Sync] 2. Cerco il file in: {path}");
            
            float timeout = 2f;
            while (!File.Exists(path) && timeout > 0)
            {
                timeout -= 0.1f;
                yield return new WaitForSeconds(0.1f);
            }

            if (!File.Exists(path))
            {
                Debug.LogError($"[Sync] ERRORE FATALE: File {path} non trovato dopo il salvataggio!");
                IsTransferring = false;
                yield break;
            }

            // 3. Lettura e Compressione
            byte[] rawData = null;
            try {
                rawData = File.ReadAllBytes(path);
            } catch (Exception e) {
                Debug.LogError($"[Sync] Errore lettura file: {e.Message}");
                IsTransferring = false;
                yield break;
            }

            Debug.Log($"[Sync] 3. File letto ({rawData.Length} bytes). Compressione...");
            byte[] compressedData = Compress(rawData);
            Debug.Log($"[Sync] 4. Compresso a {compressedData.Length} bytes. Invio...");

            // 4. Invio
            int totalChunks = Mathf.CeilToInt((float)compressedData.Length / CHUNK_SIZE);
            
            // Invia Header
            NetworkManager.Instance.SendRaw($"FILE_START|{totalChunks}|{compressedData.Length}\n");
            yield return new WaitForSeconds(0.5f); // Pausa per assicurare che l'header arrivi

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * CHUNK_SIZE;
                int size = Mathf.Min(CHUNK_SIZE, compressedData.Length - offset);
                byte[] chunk = new byte[size];
                Array.Copy(compressedData, offset, chunk, 0, size);
                
                string b64 = Convert.ToBase64String(chunk);
                NetworkManager.Instance.SendRaw($"FILE_DATA|{i}|{b64}\n");
                
                Progress = (float)i / totalChunks;
                
                // Inviamo un po' più lentamente per non intasare
                if (i % 10 == 0) 
                {
                    Debug.Log($"[Sync] Invio Chunk {i}/{totalChunks}");
                    yield return null; 
                }
            }

            Debug.Log("[Sync] INVIO COMPLETATO!");
            IsTransferring = false;
        }

        private bool CallGameSave(string slot)
        {
            try {
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType == null) return false;
                
                // Prova metodo statico
                MethodInfo method = AccessTools.Method(saveMgrType, "saveGame");
                if (method != null) { method.Invoke(null, new object[] { slot }); return true; }

                // Prova istanza
                UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
                if (instance != null) { 
                    Traverse.Create(instance).Method("saveGame", new object[] { slot }).GetValue(); 
                    return true; 
                }
            } catch (Exception e) { Debug.LogError($"[Sync] Save Error: {e.Message}"); }
            return false;
        }

        private string GetSavePath(string slot)
        {
            // Cerca in entrambe le posizioni comuni
            string p1 = Path.Combine(Application.persistentDataPath, "saves", $"{slot}.wbox");
            if (File.Exists(p1)) return p1;
            
            string p2 = Path.Combine(Application.persistentDataPath, "save_folder", $"{slot}.wbox");
            if (File.Exists(p2)) return p2;

            return p1; // Default
        }

        // --- CLIENT: RICEZIONE ---

        public void OnReceiveStart(int totalChunks, int totalBytes)
        {
            Debug.Log($"[Sync] RICEZIONE INIZIATA! Totale: {totalChunks} chunks.");
            IsTransferring = true;
            _totalChunks = totalChunks;
            _receivedChunks.Clear();
            _receivedCount = 0;
            Progress = 0f;
            
            Config.paused = true;
            NetworkManager.Instance.IsMapLoaded = false;
            WorldBoxMultiplayer.instance.UpdateStatus($"Downloading Map... ({totalChunks})");
        }

        public void OnReceiveChunk(int index, string dataB64)
        {
            if (!IsTransferring) return;

            if (!_receivedChunks.ContainsKey(index)) {
                try {
                    _receivedChunks[index] = Convert.FromBase64String(dataB64);
                    _receivedCount++;
                    Progress = (float)_receivedCount / _totalChunks;
                } catch { Debug.LogError($"[Sync] Errore chunk {index}"); }
            }

            if (_receivedCount >= _totalChunks) FinishReception();
        }

        private void FinishReception()
        {
            IsTransferring = false;
            Debug.Log("[Sync] Download finito. Ricostruzione...");
            WorldBoxMultiplayer.instance.UpdateStatus("Loading World...");

            try {
                using (MemoryStream ms = new MemoryStream()) {
                    for (int i = 0; i < _totalChunks; i++) {
                        if (_receivedChunks.ContainsKey(i)) ms.Write(_receivedChunks[i], 0, _receivedChunks[i].Length);
                        else { Debug.LogError($"[Sync] MANCA IL PEZZO {i}!"); return; }
                    }
                    
                    byte[] compressedData = ms.ToArray();
                    byte[] rawData = Decompress(compressedData);
                    
                    string path = GetSavePath(SYNC_SLOT);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, rawData);
                }

                Debug.Log("[Sync] Caricamento in gioco...");
                
                // Carica
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType != null) {
                    MethodInfo method = AccessTools.Method(saveMgrType, "loadGame");
                    if (method != null) method.Invoke(null, new object[] { SYNC_SLOT });
                    else {
                        var inst = UnityEngine.Object.FindObjectOfType(saveMgrType);
                        if(inst != null) Traverse.Create(inst).Method("loadGame", new object[] { SYNC_SLOT }).GetValue();
                    }
                }
                
                Debug.Log("[Sync] GIOCO PRONTO!");
                NetworkManager.Instance.IsMapLoaded = true;
                LockstepController.Instance.CurrentTick = 0;
                WorldBoxMultiplayer.instance.UpdateStatus("Connected & Synced");

            } catch (Exception e) {
                Debug.LogError("[Sync] Errore caricamento: " + e.Message);
                NetworkManager.Instance.IsMapLoaded = true; // Sblocca comunque
            }
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