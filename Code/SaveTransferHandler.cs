using UnityEngine;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System;
using HarmonyLib;

namespace WorldBoxMultiplayer
{
    public class SaveTransferHandler : MonoBehaviour
    {
        public static SaveTransferHandler Instance;
        private const int CHUNK_SIZE = 8192; 
        private const string SYNC_SLOT = "mp_sync_slot";
        public bool IsTransferring = false;
        public float Progress = 0f;
        private Dictionary<int, byte[]> _receivedChunks = new Dictionary<int, byte[]>();
        private int _totalChunks = 0;
        private int _receivedCount = 0;

        void Awake() { Instance = this; }

        public void StartTransfer()
        {
            IsTransferring = true;
            Progress = 0f;
            
            Debug.Log("[Sync] Starting Save Transfer...");
            // Use Traverse to call SaveManager.saveGame(string)
            try {
                var trav = Traverse.Create(Type.GetType("SaveManager"));
                if (trav.Method("saveGame", new object[] { SYNC_SLOT }).MethodExists()) 
                    trav.Method("saveGame", new object[] { SYNC_SLOT }).GetValue();
                else 
                    Debug.LogError("[Sync] SaveManager.saveGame not found!");
            } catch (Exception e) { Debug.LogError("[Sync] Save Error: " + e.Message); }
            
            string path = Application.persistentDataPath + "/saves/" + SYNC_SLOT + ".wbox";
            if (!File.Exists(path)) { 
                Debug.LogError("[Sync] Save file not found at: " + path);
                IsTransferring = false; 
                return; 
            }
            
            byte[] rawData = File.ReadAllBytes(path);
            byte[] compressedData = Compress(rawData);
            Debug.Log($"[Sync] Sending {compressedData.Length} bytes...");
            StartCoroutine(SendFileRoutine(compressedData));
        }

        private System.Collections.IEnumerator SendFileRoutine(byte[] data)
        {
            int totalChunks = Mathf.CeilToInt((float)data.Length / CHUNK_SIZE);
            NetworkManager.Instance.SendRaw($"FILE_START|{totalChunks}|{data.Length}\n");
            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * CHUNK_SIZE;
                int size = Mathf.Min(CHUNK_SIZE, data.Length - offset);
                byte[] chunk = new byte[size];
                Array.Copy(data, offset, chunk, 0, size);
                string b64 = Convert.ToBase64String(chunk);
                NetworkManager.Instance.SendRaw($"FILE_DATA|{i}|{b64}\n");
                Progress = (float)i / totalChunks;
                if (i % 10 == 0) yield return null;
            }
            IsTransferring = false;
            Debug.Log("[Sync] Transfer Complete.");
        }

        public void OnReceiveStart(int totalChunks, int totalBytes)
        {
            IsTransferring = true;
            _totalChunks = totalChunks;
            _receivedChunks.Clear();
            _receivedCount = 0;
            Progress = 0f;
            Config.paused = true;
            Debug.Log($"[Sync] Receiving {totalBytes} bytes in {totalChunks} chunks...");
            StartCoroutine(TransferTimeoutRoutine());
        }

        private System.Collections.IEnumerator TransferTimeoutRoutine() {
            float timer = 0f;
            while (IsTransferring && timer < 30f) {
                timer += Time.unscaledDeltaTime;
                yield return null;
            }
            if (IsTransferring) {
                Debug.LogError("[Sync] Transfer Timed Out!");
                IsTransferring = false;
                Config.paused = false;
            }
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
            Debug.Log("[Sync] Reception Complete. Processing...");
            try {
                using (MemoryStream ms = new MemoryStream()) {
                    for (int i = 0; i < _totalChunks; i++) if (_receivedChunks.ContainsKey(i)) ms.Write(_receivedChunks[i], 0, _receivedChunks[i].Length);
                    byte[] compressedData = ms.ToArray();
                    byte[] rawData = Decompress(compressedData);
                    string path = Application.persistentDataPath + "/saves/" + SYNC_SLOT + ".wbox";
                    File.WriteAllBytes(path, rawData);
                }
                
                // Use Traverse to call SaveManager.loadGame(string)
                var saveMgrType = Type.GetType("SaveManager");
                if (saveMgrType != null) {
                    var trav = Traverse.Create(saveMgrType);
                    if (trav.Method("loadGame", new object[] { SYNC_SLOT }).MethodExists()) {
                        trav.Method("loadGame", new object[] { SYNC_SLOT }).GetValue();
                        Debug.Log("[Sync] Game Loaded Successfully!");
                    }
                    else Debug.LogError("[Sync] SaveManager.loadGame not found!");
                } else Debug.LogError("[Sync] SaveManager class not found!");
                
            } catch (Exception e) { Debug.LogError("[Sync] Load Error: " + e.Message); }
            
            Config.paused = false;
            NetworkManager.Instance.SendTickSync(0);
        }

        private byte[] Compress(byte[] data) {
            using (MemoryStream output = new MemoryStream()) {
                using (GZipStream dstream = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal)) { dstream.Write(data, 0, data.Length); }
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