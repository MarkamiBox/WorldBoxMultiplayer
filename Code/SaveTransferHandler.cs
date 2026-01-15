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
        private const int CHUNK_SIZE = 16384; 
        private const int SYNC_SLOT_ID = 15; 
        public bool IsTransferring = false;
        public float Progress = 0f;
        private Dictionary<int, byte[]> _receivedChunks = new Dictionary<int, byte[]>();
        private int _totalChunks = 0;
        private int _receivedCount = 0;

        void Awake() { Instance = this; }
        
        public void StartTransfer() {
            if (IsTransferring) return; IsTransferring = true; Progress = 0f;
            Debug.Log($"[Sync] Saving to slot {SYNC_SLOT_ID}...");
            bool ok = CallGameSave(SYNC_SLOT_ID);
            if (!ok) { Debug.LogError("[Sync] Save Failed!"); IsTransferring=false; return; }
            StartCoroutine(WaitForFileAndSend());
        }

        private IEnumerator WaitForFileAndSend() {
            string path = GetSavePath(SYNC_SLOT_ID);
            float timeout = 5f; bool fileFound = false;
            while (timeout > 0) { if (File.Exists(path)) { fileFound = true; break; } timeout -= 0.2f; yield return new WaitForSeconds(0.2f); }
            if (!fileFound) { Debug.LogError("[Sync] File not found."); IsTransferring=false; yield break; }
            
            yield return new WaitForSeconds(0.2f); 
            
            byte[] rawData = null;
            try { rawData = File.ReadAllBytes(path); } catch { IsTransferring=false; yield break; }
            StartCoroutine(SendFileRoutine(Compress(rawData)));
        }

        private bool CallGameSave(int slot) {
            try { Type t = AccessTools.TypeByName("SaveManager"); if(t==null)return false; MethodInfo m=AccessTools.Method(t,"saveGame",new Type[]{typeof(int)}); if(m!=null){m.Invoke(null,new object[]{slot}); return true;} UnityEngine.Object i=UnityEngine.Object.FindObjectOfType(t); if(i!=null){Traverse.Create(i).Method("saveGame",new object[]{slot}).GetValue(); return true;} } catch{} return false;
        }
        private string GetSavePath(int slot) { return Path.Combine(Application.persistentDataPath, "saves", "save"+slot+".wbox"); }

        private IEnumerator SendFileRoutine(byte[] data) {
            int totalChunks = Mathf.CeilToInt((float)data.Length / CHUNK_SIZE);
            NetworkManager.Instance.SendRaw($"FILE_START|{totalChunks}|{data.Length}\n");
            yield return new WaitForSeconds(0.5f);
            for (int i = 0; i < totalChunks; i++) {
                int size = Mathf.Min(CHUNK_SIZE, data.Length - i * CHUNK_SIZE);
                byte[] chunk = new byte[size]; Array.Copy(data, i * CHUNK_SIZE, chunk, 0, size);
                NetworkManager.Instance.SendRaw($"FILE_DATA|{i}|{Convert.ToBase64String(chunk)}\n");
                Progress = (float)i / totalChunks; if (i % 50 == 0) yield return null; 
            }
            IsTransferring = false;
        }

        public void OnReceiveStart(int c, int b) { IsTransferring = true; _totalChunks = c; _receivedChunks.Clear(); _receivedCount = 0; Config.paused = true; NetworkManager.Instance.IsMapLoaded = false; WorldBoxMultiplayer.instance.UpdateStatus($"Downloading ({c})..."); }
        public void OnReceiveChunk(int i, string d) { if(!IsTransferring)return; if(!_receivedChunks.ContainsKey(i)){ try{_receivedChunks[i]=Convert.FromBase64String(d);_receivedCount++;Progress=(float)_receivedCount/_totalChunks;}catch{}} if(_receivedCount>=_totalChunks) FinishReception(); }
        
        private void FinishReception() {
            IsTransferring = false;
            try {
                using (MemoryStream ms = new MemoryStream()) {
                    for (int i=0; i<_totalChunks; i++) if(_receivedChunks.ContainsKey(i)) ms.Write(_receivedChunks[i],0,_receivedChunks[i].Length);
                    File.WriteAllBytes(GetSavePath(SYNC_SLOT_ID), Decompress(ms.ToArray()));
                }
                CallGameLoad(SYNC_SLOT_ID);
                NetworkManager.Instance.IsMapLoaded = true;
                
                if (ClientController.Instance != null) {
                    ClientController.Instance.IsClientMode = true;
                    ClientController.Instance.ClearState();
                }
                
                WorldBoxMultiplayer.instance.UpdateStatus("Synced - Client Mode Active");
                Config.paused = false; 
            } catch (Exception e) { 
                Debug.LogError($"[Sync] Load failed: {e.Message}");
                NetworkManager.Instance.IsMapLoaded = true; 
                Config.paused = false; 
            }
        }
        private void CallGameLoad(int slot) { try { Type t=AccessTools.TypeByName("SaveManager"); if(t==null)return; MethodInfo m=AccessTools.Method(t,"loadGame",new Type[]{typeof(int)}); if(m!=null){m.Invoke(null,new object[]{slot});return;} UnityEngine.Object i=UnityEngine.Object.FindObjectOfType(t); if(i!=null)Traverse.Create(i).Method("loadGame",new object[]{slot}).GetValue(); } catch{} }
        
        // FIX QUI SOTTO: Uso esplicito di System.IO.Compression.CompressionLevel
        private byte[] Compress(byte[] d) { 
            using(MemoryStream o=new MemoryStream()){ 
                using(GZipStream z=new GZipStream(o, System.IO.Compression.CompressionLevel.Fastest)){
                    z.Write(d,0,d.Length);
                } 
                return o.ToArray(); 
            } 
        }
        
        // FIX QUI SOTTO: Uso esplicito di System.IO.Compression.CompressionMode
        private byte[] Decompress(byte[] d) { 
            using(MemoryStream i=new MemoryStream(d)) 
            using(GZipStream z=new GZipStream(i, System.IO.Compression.CompressionMode.Decompress)) 
            using(MemoryStream o=new MemoryStream()){ 
                z.CopyTo(o); 
                return o.ToArray(); 
            } 
        }
    }
}