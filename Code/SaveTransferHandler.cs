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
        private const int CHUNK_SIZE = 16384; 
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
            
            Debug.Log("[Sync] STARTING SAVE TRANSFER...");
            bool saveSuccess = CallGameSave(SYNC_SLOT);
            if (!saveSuccess) {
                Debug.LogError("[Sync] FAILED to save game locally.");
                IsTransferring = false;
                return;
            }
            
            string path = GetSavePath(SYNC_SLOT);
            if (!File.Exists(path)) { 
                Debug.LogError($"[Sync] File not found: {path}");
                IsTransferring = false; 
                return; 
            }

            try {
                byte[] rawData = File.ReadAllBytes(path);
                byte[] compressedData = Compress(rawData);
                StartCoroutine(SendFileRoutine(compressedData));
            } catch (Exception e) {
                Debug.LogError($"[Sync] Error: {e.Message}");
                IsTransferring = false;
            }
        }

        private bool CallGameSave(string slot)
        {
            try {
                Type saveMgrType = AccessTools.TypeByName("SaveManager");
                if (saveMgrType == null) return false;

                MethodInfo method = AccessTools.Method(saveMgrType, "saveGame");
                if (method != null) { method.Invoke(null, new object[] { slot }); return true; }

                UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(saveMgrType);
                if (instance != null) { Traverse.Create(instance).Method("saveGame", new object[] { slot }).GetValue(); return true; }
            } catch {}
            return false;
        }

        private string GetSavePath(string slot)
        {
            return System.IO.Path.Combine(Application.persistentDataPath, "saves", $"{slot}.wbox");
        }

        private System.Collections.IEnumerator SendFileRoutine(byte[] data)
        {
            int totalChunks = Mathf.CeilToInt((float)data.Length / CHUNK_SIZE);
            NetworkManager.Instance.SendRaw($"FILE_START|{totalChunks}|{data.Length}\n");
            
            // Wait a bit to ensure Header arrives first
            yield return new WaitForSeconds(0.2f);

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * CHUNK_SIZE;
                int size = Mathf.Min(CHUNK_SIZE, data.Length - offset);
                byte[] chunk = new byte[size];
                Array.Copy(data, offset, chunk, 0, size);
                
                string b64 = Convert.ToBase64String(chunk);
                NetworkManager.Instance.SendRaw($"FILE_DATA|{i}|{b64}\n");
                
                Progress = (float)i / totalChunks;
                if (i % 5 == 0) yield return null; 
            }
            
            IsTransferring = false;
            Debug.Log("[Sync] TRANSFER COMPLETE.");
        }

        public void OnReceiveStart(int totalChunks, int totalBytes)
        {
            Debug.Log($"[Sync] DOWNLOADING MAP... {totalChunks} chunks.");
            IsTransferring = true;
            _totalChunks = totalChunks;
            _receivedChunks.Clear();
            _receivedCount = 0;
            Progress = 0f;
            
            Config.paused = true;
            NetworkManager.Instance.IsMapLoaded = false;
            WorldBoxMultiplayer.instance.UpdateStatus("Syncing Map...");
        }

        public void OnReceiveChunk(int index, string dataB64)
        {
            if (!IsTransferring) return; 

            if (!_receivedChunks.ContainsKey(index)) {
                try {
                    _receivedChunks[index] = Convert.FromBase64String(dataB64);
                    _receivedCount++;
                    Progress = (float)_receivedCount / _totalChunks;
                } catch {}
            }

            if (_receivedCount >= _totalChunks) FinishReception();
        }

        private void FinishReception()
        {
            IsTransferring = false;
            Debug.Log("[Sync] Download finished. Loading...");
            WorldBoxMultiplayer.instance.UpdateStatus("Loading World...");

            try {
                using (MemoryStream ms = new MemoryStream()) {
                    for (int i = 0; i < _totalChunks; i++) 
                        if (_receivedChunks.ContainsKey(i)) ms.Write(_receivedChunks[i], 0, _receivedChunks[i].Length);
                    
                    byte[] compressedData = ms.ToArray();
                    byte[] rawData = Decompress(compressedData);
                    
                    string path = GetSavePath(SYNC_SLOT);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, rawData);
                }

                CallGameLoad(SYNC_SLOT);
                
                Debug.Log("[Sync] GAME READY.");
                NetworkManager.Instance.IsMapLoaded = true;
                LockstepController.Instance.CurrentTick = 0;
                WorldBoxMultiplayer.instance.UpdateStatus("Connected & Synced");

            } catch (Exception e) {
                Debug.LogError("[Sync] LOAD ERROR: " + e.Message);
                // EMERGENCY RECOVERY
                Config.paused = false;
                NetworkManager.Instance.IsMapLoaded = true; // Let them play even if broken
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