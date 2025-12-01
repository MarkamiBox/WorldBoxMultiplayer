using UnityEngine;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System;

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
            
            // Reflection for SaveManager.saveGame
            var saveMgr = Type.GetType("SaveManager");
            if (saveMgr != null) {
                var method = saveMgr.GetMethod("saveGame", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) ?? saveMgr.GetMethod("SaveGame", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                method?.Invoke(null, new object[] { SYNC_SLOT });
            }
            
            string path = Application.persistentDataPath + "/saves/" + SYNC_SLOT + ".wbox";
            if (!File.Exists(path)) { IsTransferring = false; return; }
            byte[] rawData = File.ReadAllBytes(path);
            byte[] compressedData = Compress(rawData);
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
        }

        public void OnReceiveStart(int totalChunks, int totalBytes)
        {
            IsTransferring = true;
            _totalChunks = totalChunks;
            _receivedChunks.Clear();
            _receivedCount = 0;
            Progress = 0f;
            Config.paused = true;
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
            using (MemoryStream ms = new MemoryStream()) {
                for (int i = 0; i < _totalChunks; i++) if (_receivedChunks.ContainsKey(i)) ms.Write(_receivedChunks[i], 0, _receivedChunks[i].Length);
                byte[] compressedData = ms.ToArray();
                byte[] rawData = Decompress(compressedData);
                string path = Application.persistentDataPath + "/saves/" + SYNC_SLOT + ".wbox";
                File.WriteAllBytes(path, rawData);
            }
            
            // Reflection for SaveManager.loadGame
            var saveMgr = Type.GetType("SaveManager");
            if (saveMgr != null) {
                var method = saveMgr.GetMethod("loadGame", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) ?? saveMgr.GetMethod("LoadGame", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                method?.Invoke(null, new object[] { SYNC_SLOT });
            }
            
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