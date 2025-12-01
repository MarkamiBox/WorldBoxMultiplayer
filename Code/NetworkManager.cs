using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib; 

namespace WorldBoxMultiplayer
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance;
        public bool IsConnected = false;
        public bool IsMultiplayerReady { get { return IsConnected && _stream != null; } }

        private TcpClient _client;
        private TcpListener _server;
        private NetworkStream _stream;
        private bool _isHost = false;
        private bool _shouldSyncMap = false;

        private Dictionary<string, bool> _lastLawsState = new Dictionary<string, bool>();
        private string _lastSpeed = "";
        private string _lastEra = "";

        void Awake() { Instance = this; }

        public void StartHost(int port)
        {
            try {
                _server = new TcpListener(System.Net.IPAddress.Any, port);
                _server.Start();
                _server.BeginAcceptTcpClient(OnClientConnected, null);
                _isHost = true;
                IsConnected = true; 
                StartCoroutine(SyncCheckerRoutine());
                Debug.Log("[Multiplayer] Server Started.");
            } catch (Exception e) { Debug.LogError("Host Error: " + e.Message); }
        }

        public void StartClient(string ip, int port)
        {
            try {
                _client = new TcpClient();
                _client.NoDelay = true; 
                _client.Connect(ip, port);
                _stream = _client.GetStream();
                IsConnected = true;
                StartCoroutine(SyncCheckerRoutine());
                Debug.Log("[Multiplayer] Client Connected!");
            } catch (Exception e) { Debug.LogError("Client Error: " + e.Message); }
        }

        // Polling loop to check for changes (Safer than patching)
        private IEnumerator SyncCheckerRoutine()
        {
            while (true)
            {
                if (IsMultiplayerReady)
                {
                    CheckLaws();
                    CheckSpeed();
                    CheckEra();
                }
                yield return new WaitForSeconds(0.5f);
            }
        }

        private void CheckSpeed()
        {
            if (Config.time_scale_asset == null) return;
            string current = Config.time_scale_asset.id;
            if (_lastSpeed != current) { _lastSpeed = current; SendSpeedChange(current); }
        }

        private void CheckEra()
        {
            object activeEraObj = Traverse.Create(World.world.era_manager).Field("active_era").GetValue();
            if (activeEraObj == null) return;
            string current = Traverse.Create(activeEraObj).Field("id").GetValue<string>();
            if (_lastEra != current) { _lastEra = current; SendEraChange(current); }
        }

        private void CheckLaws()
        {
            if (World.world?.world_laws?.dict == null) return;
            foreach (var kvp in World.world.world_laws.dict)
            {
                string id = kvp.Key;
                bool state = kvp.Value.boolVal;
                if (!_lastLawsState.ContainsKey(id) || _lastLawsState[id] != state)
                {
                    _lastLawsState[id] = state;
                    SendLawToggle(id, state);
                }
            }
        }

        private void OnClientConnected(IAsyncResult ar)
        {
            try {
                if (_server == null) return;
                _client = _server.EndAcceptTcpClient(ar);
                if (_client == null) return;
                
                _client.NoDelay = true;
                _stream = _client.GetStream();
                _shouldSyncMap = true; 
                Debug.Log("[Multiplayer] Client Connected to Host!");
            } catch (Exception e) { Debug.LogError("Conn Error: " + e.Message); }
        }

        public void SendRaw(string message) { 
            if (_stream == null || _client == null || !_client.Connected) return;
            try { 
                byte[] msg = Encoding.UTF8.GetBytes(message); 
                _stream.Write(msg, 0, msg.Length); 
            } catch (Exception e) { 
                Debug.LogError("SendRaw Error: " + e.Message); 
                Disconnect(); 
            } 
        }
        public void SendAction(string actionData) { if (!IsMultiplayerReady) return; int targetTick = LockstepController.Instance.CurrentTick + 2; SendRaw($"G|{targetTick}|{actionData}\n"); }
        public void SendNameChange(string type, long id, string newName) { if (!IsMultiplayerReady) return; string name64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(newName)); SendRaw($"N|{type}|{id}|{name64}\n"); }
        public void SendKingdomCustomization(long id, int colorId, int bannerId) { if (!IsMultiplayerReady) return; SendRaw($"K|{id}|{colorId}|{bannerId}\n"); }
        public void SendEraChange(string eraID) { if (!IsMultiplayerReady) return; SendRaw($"A|{eraID}\n"); }
        public void SendPowerSelection(string powerID) { if (!IsMultiplayerReady) return; SendRaw($"P|{powerID}\n"); }
        public void SendTickSync(int tick) { if(IsMultiplayerReady) SendRaw($"T|{tick}\n"); }
        public void SendCursorPos(float x, float y) { if(IsMultiplayerReady) SendRaw($"C|{x:F1}|{y:F1}\n"); }
        public void SendLawToggle(string id, bool state) { if(IsMultiplayerReady) SendRaw($"L|{id}|{state}\n"); }
        public void SendSpeedChange(string id) { if(IsMultiplayerReady) SendRaw($"S|{id}\n"); }
        public void SendHash(int tick, long hash) { if(IsMultiplayerReady) SendRaw($"H|{tick}|{hash}\n"); }
        public void SendDisconnect() { if(IsMultiplayerReady) SendRaw("D\n"); }
        public void SendRequestMap() { if(IsMultiplayerReady) SendRaw("R\n"); }
        public void RequestResync() { if (_isHost) SaveTransferHandler.Instance.StartTransfer(); else SendRequestMap(); }

        void Update()
        {
            if (_shouldSyncMap) { SaveTransferHandler.Instance.StartTransfer(); _shouldSyncMap = false; }
            if (!IsMultiplayerReady) return;

            if (_stream.DataAvailable)
            {
                try {
                    byte[] data = new byte[32768]; 
                    int bytes = _stream.Read(data, 0, data.Length);
                    string responseData = Encoding.UTF8.GetString(data, 0, bytes);
                    
                    string[] lines = responseData.Split('\n');
                    foreach(var line in lines)
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        string[] parts = line.Split('|');
                        if (parts.Length < 1) continue;

                        string type = parts[0];
                        if (type == "FILE_START") SaveTransferHandler.Instance.OnReceiveStart(int.Parse(parts[1]), int.Parse(parts[2]));
                        else if (type == "FILE_DATA") SaveTransferHandler.Instance.OnReceiveChunk(int.Parse(parts[1]), parts[2]);
                        else if (type == "G" && parts.Length >= 3) LockstepController.Instance.AddPendingAction(int.Parse(parts[1]), parts[2]);
                        else if (type == "T") LockstepController.Instance.SetServerTick(int.Parse(parts[1]));
                        else if (type == "C") { if (CursorHandler.Instance) CursorHandler.Instance.UpdateRemoteCursor(float.Parse(parts[1]), float.Parse(parts[2])); }
                        else if (type == "P" && CursorHandler.Instance) CursorHandler.Instance.SetRemotePower(parts[1]);
                        else if (type == "L") WorldBoxMultiplayer.instance.SetLaw(parts[1], bool.Parse(parts[2]));
                        else if (type == "S") WorldBoxMultiplayer.instance.SetSpeed(parts[1]);
                        else if (type == "H") LockstepController.Instance.CheckRemoteHash(int.Parse(parts[1]), long.Parse(parts[2]));
                        else if (type == "N") WorldBoxMultiplayer.instance.SetName(parts[1], long.Parse(parts[2]), parts[3]);
                        else if (type == "A") WorldBoxMultiplayer.instance.SetEra(parts[1]);
                        else if (type == "K") WorldBoxMultiplayer.instance.SetKingdomData(long.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
                        else if (type == "D") { Debug.Log("[Sync] Partner disconnected."); Disconnect(); }
                        else if (type == "R" && _isHost) { Debug.Log("[Sync] Client requested map."); SaveTransferHandler.Instance.StartTransfer(); }
                    }
                } catch (Exception e) { Debug.LogError("NetRead: " + e.Message); }
            }
            
            LockstepController.Instance.NetworkUpdate();
        }

        public void Disconnect()
        {
            SendDisconnect();
            try {
                if (_stream != null) _stream.Close();
                if (_client != null) _client.Close();
                if (_server != null) _server.Stop();
            } catch {}
            IsConnected = false;
            _stream = null;
            _client = null;
            _server = null;
            _isHost = false;
        }
        
        public bool IsHost() { return _isHost; }
    }
}