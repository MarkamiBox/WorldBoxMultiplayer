using UnityEngine;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System;
using System.Reflection;
using HarmonyLib; // Usa Harmony/Traverse per sicurezza

namespace WorldBoxMultiplayer
{
    public class SaveTransferHandler : MonoBehaviour
    {
        public static SaveTransferHandler Instance;
        private const int CHUNK_SIZE = 16384; // 16KB per pacchetto (più veloce)
        private const string SYNC_SLOT = "mp_sync_slot";
        public bool IsTransferring = false;
        public float Progress = 0f;
        private Dictionary<int, byte[]> _receivedChunks = new Dictionary<int, byte[]>();
        private int _totalChunks = 0;
        private int _receivedCount = 0;

        void Awake() { Instance = this; }

        public void StartTransfer()
        {
            if (IsTransferring) return;
            IsTransferring = true;
            Progress = 0f;
            
            Debug.Log("[Sync] Inizio procedura di salvataggio...");

            // 1. Salva il gioco usando il metodo interno di WorldBox
            try {
                var saveMgr = Traverse.Create(Type.GetType("SaveManager"));
                if (saveMgr.Method("saveGame", new object[] { SYNC_SLOT }).MethodExists()) 
                    saveMgr.Method("saveGame", new object[] { SYNC_SLOT }).GetValue();
                else 
                    Debug.LogError("[Sync] Errore: SaveManager.saveGame non trovato!");
            } catch (Exception e) { Debug.LogError("[Sync] Errore salvataggio: " + e.Message); }
            
            // 2. Trova il percorso del file (Metodo Robusto)
            string path = GetSavePath(SYNC_SLOT);
            Debug.Log("[Sync] Cerco file in: " + path);

            if (!File.Exists(path)) { 
                Debug.LogError("[Sync] File non trovato! Impossibile inviare.");
                IsTransferring = false; 
                return; 
            }

            // 3. Leggi e Comprimi
            byte[] rawData = File.ReadAllBytes(path);
            byte[] compressedData = Compress(rawData);
            Debug.Log($"[Sync] File letto. Originale: {rawData.Length}, Compresso: {compressedData.Length}");

            // 4. Invia
            StartCoroutine(SendFileRoutine(compressedData));
        }

        private string GetSavePath(string slot)
        {
            // Prova a chiedere al gioco dove salva i file
            try {
                var saveMgr = Traverse.Create(Type.GetType("SaveManager"));
                if (saveMgr.Method("generateSavePath", new object[] { slot }).MethodExists())
                    return (string)saveMgr.Method("generateSavePath", new object[] { slot }).GetValue();
            } catch {}
            // Fallback manuale
            return Application.persistentDataPath + "/saves/" + slot + ".wbox";
        }

        private System.Collections.IEnumerator SendFileRoutine(byte[] data)
        {
            int totalChunks = Mathf.CeilToInt((float)data.Length / CHUNK_SIZE);
            NetworkManager.Instance.SendRaw($"FILE_START|{totalChunks}|{data.Length}\n");
            
            yield return new WaitForSeconds(0.1f); // Piccola pausa per sicurezza

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * CHUNK_SIZE;
                int size = Mathf.Min(CHUNK_SIZE, data.Length - offset);
                byte[] chunk = new byte[size];
                Array.Copy(data, offset, chunk, 0, size);
                
                string b64 = Convert.ToBase64String(chunk);
                NetworkManager.Instance.SendRaw($"FILE_DATA|{i}|{b64}\n");
                
                Progress = (float)i / totalChunks;
                if (i % 5 == 0) yield return null; // Non bloccare il thread
            }
            
            IsTransferring = false;
            Debug.Log("[Sync] Invio completato.");
        }

        public void OnReceiveStart(int totalChunks, int totalBytes)
        {
            Debug.Log($"[Sync] Ricezione iniziata: {totalChunks} pezzi.");
            IsTransferring = true;
            _totalChunks = totalChunks;
            _receivedChunks.Clear();
            _receivedCount = 0;
            Progress = 0f;
            
            // Blocca il gioco durante il download
            Config.paused = true;
            NetworkManager.Instance.IsMapLoaded = false;
        }

        public void OnReceiveChunk(int index, string dataB64)
        {
            if (!_receivedChunks.ContainsKey(index)) {
                _receivedChunks[index] = Convert.FromBase64String(dataB64);
                _receivedCount++;
                Progress = (float)_receivedCount / _totalChunks;
            }
            if (_receivedCount >= _totalChunks) FinishReception();
        }

        private void FinishReception()
        {
            IsTransferring = false;
            Debug.Log("[Sync] Download completato. Decompressione...");

            try {
                using (MemoryStream ms = new MemoryStream()) {
                    for (int i = 0; i < _totalChunks; i++) 
                        if (_receivedChunks.ContainsKey(i)) ms.Write(_receivedChunks[i], 0, _receivedChunks[i].Length);
                    
                    byte[] compressedData = ms.ToArray();
                    byte[] rawData = Decompress(compressedData);
                    
                    string path = GetSavePath(SYNC_SLOT);
                    
                    // Assicurati che la cartella esista
                    string dir = Path.GetDirectoryName(path);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    File.WriteAllBytes(path, rawData);
                }

                Debug.Log("[Sync] Caricamento mondo...");
                
                var saveMgr = Traverse.Create(Type.GetType("SaveManager"));
                saveMgr.Method("loadGame", new object[] { SYNC_SLOT }).GetValue();

                Debug.Log("[Sync] Mondo caricato con successo!");
                
                // Sblocca il gioco
                NetworkManager.Instance.IsMapLoaded = true;
                NetworkManager.Instance.SendTickSync(0); // Resetta tick

            } catch (Exception e) {
                Debug.LogError("[Sync] Errore critico caricamento: " + e.Message);
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
            using (GZipStream dstream = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream()) { dstream.CopyTo(output); return output.ToArray(); }
        }
    }
}