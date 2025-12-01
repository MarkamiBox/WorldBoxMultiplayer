using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib; 
using System.Net;

namespace WorldBoxMultiplayer
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance;
        public bool IsConnected = false;
        public bool IsMapLoaded = false;
        public bool IsMultiplayerReady { get { return IsConnected && _stream != null; } }

        private TcpClient _client;
        private TcpListener _server;
        private NetworkStream _stream;
        private bool _isHost = false;
        private bool _shouldStartTransfer = false;

        private StringBuilder _incomingBuffer = new StringBuilder();
        private byte[] _receiveBuffer = new byte[65536]; 
        private Dictionary<string, bool> _lastLawsState = new Dictionary<string, bool>();
        private string _lastSpeed = "";
        private string _lastEra = "";

        void Awake() { Instance = this; }

        public void StartHost(int port)
        {
            try {
                _server = new TcpListener(IPAddress.Any, port);
                _server.Start();
                _server.BeginAcceptTcpClient(OnClientConnected, null);
                _isHost = true;
                IsConnected = true; 
                IsMapLoaded = true;
                WorldBoxMultiplayer.instance.UpdateStatus("Waiting for players...");
                StartCoroutine(SyncCheckerRoutine());
                Debug.Log($"[Multiplayer] Server Started on Port {port}");
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
                IsMapLoaded = false;
                WorldBoxMultiplayer.instance.UpdateStatus("Connected! Waiting for Map...");
                StartCoroutine(SyncCheckerRoutine());
                Debug.Log($"[Multiplayer] Client Connected to {ip}");
            } catch (Exception e) { 
                WorldBoxMultiplayer.instance.UpdateStatus("Connection Failed");
                Debug.LogError("Client Error: " + e.Message); 
            }
        }

        private void OnClientConnected(IAsyncResult ar)
        {
            try {
                _client = _server.EndAcceptTcpClient(ar);
                _client.NoDelay = true;
                _stream = _client.GetStream();
                Debug.Log("[Multiplayer] Client joined! Triggering Transfer...");
                _shouldStartTransfer = true; 
            } catch (Exception e) { Debug.LogError("Conn Error: " + e.Message); }
        }

        void Update()
        {
            if (_shouldStartTransfer) { 
                WorldBoxMultiplayer.instance.UpdateStatus("Sending Map (Slot 15)...");
                SaveTransferHandler.Instance.StartTransfer(); 
                _shouldStartTransfer = false; 
            }
            
            if (!IsMultiplayerReady) return;

            if (_stream.DataAvailable)
            {
                try {
                    int bytesRead = _stream.Read(_receiveBuffer, 0, _receiveBuffer.Length);
                    if (bytesRead > 0)
                    {
                        string chunk = Encoding.UTF8.GetString(_receiveBuffer, 0, bytesRead);
                        _incomingBuffer.Append(chunk);

                        string content = _incomingBuffer.ToString();
                        int newlineIndex;

                        while ((newlineIndex = content.IndexOf('\n')) != -1)
                        {
                            string packet = content.Substring(0, newlineIndex).Trim();
                            content = content.Substring(newlineIndex + 1);
                            if (!string.IsNullOrEmpty(packet)) ProcessPacket(packet);
                        }
                        _incomingBuffer.Clear();
                        _incomingBuffer.Append(content);
                    }
                } catch (Exception e) { Debug.LogError("NetRead: " + e.Message); }
            }
            
            if (IsMapLoaded) LockstepController.Instance.NetworkUpdate();
        }

        private void ProcessPacket(string packet)
        {
            try {
                string[] parts = packet.Split('|');
                if (parts.Length < 1) return;
                string type = parts[0];

                if (type == "FILE_START") SaveTransferHandler.Instance.OnReceiveStart(int.Parse(parts[1]), int.Parse(parts[2]));
                else if (type == "FILE_DATA") SaveTransferHandler.Instance.OnReceiveChunk(int.Parse(parts[1]), parts[2]);
                
                else if (IsMapLoaded) {
                    if (type == "G") LockstepController.Instance.AddPendingAction(int.Parse(parts[1]), parts[2]);
                    else if (type == "T") LockstepController.Instance.SetServerTick(int.Parse(parts[1]));
                    else if (type == "C" && CursorHandler.Instance) {
                        // Debug.Log($"[Net] Cursor Update: {parts[1]}, {parts[2]}"); // Uncomment for verbose debug
                        CursorHandler.Instance.UpdateRemoteCursor(float.Parse(parts[1]), float.Parse(parts[2]));
                    }
                    else if (type == "P" && CursorHandler.Instance) CursorHandler.Instance.SetRemotePower(parts[1]);
                    else if (type == "L") WorldBoxMultiplayer.instance.SetLaw(parts[1], bool.Parse(parts[2]));
                    else if (type == "S") WorldBoxMultiplayer.instance.SetSpeed(parts[1]);
                    else if (type == "H") LockstepController.Instance.CheckRemoteHash(int.Parse(parts[1]), long.Parse(parts[2]));
                    else if (type == "N") WorldBoxMultiplayer.instance.SetName(parts[1], long.Parse(parts[2]), parts[3]);
                    else if (type == "A") WorldBoxMultiplayer.instance.SetEra(parts[1]);
                    else if (type == "K") WorldBoxMultiplayer.instance.SetKingdomData(long.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
                }
                
                if (type == "D") Disconnect();
                else if (type == "R" && IsHost()) {
                    Debug.Log("[Sync] Client Ready! Unpausing game...");
                    Config.paused = false;
                    WorldBoxMultiplayer.instance.UpdateStatus("Client Ready - Game Started");
                }

            } catch (Exception e) { Debug.LogError($"Packet Error: {e.Message}"); }
        }
        
        private IEnumerator SyncCheckerRoutine() { while(true) { if(IsMultiplayerReady && IsMapLoaded) { CheckLaws(); CheckSpeed(); CheckEra(); } yield return new WaitForSeconds(0.5f); } }
        private void CheckSpeed() { if (Config.time_scale_asset != null && _lastSpeed != Config.time_scale_asset.id) { _lastSpeed = Config.time_scale_asset.id; SendSpeedChange(_lastSpeed); } }
        private void CheckEra() { object ae = Traverse.Create(World.world.era_manager).Field("active_era").GetValue(); if(ae!=null){ string id = Traverse.Create(ae).Field("id").GetValue<string>(); if(_lastEra!=id){ _lastEra=id; SendEraChange(id); } } }
        private void CheckLaws() { if(World.world?.world_laws?.dict!=null) foreach(var kvp in World.world.world_laws.dict) { if(!_lastLawsState.ContainsKey(kvp.Key) || _lastLawsState[kvp.Key]!=kvp.Value.boolVal) { _lastLawsState[kvp.Key]=kvp.Value.boolVal; SendLawToggle(kvp.Key, kvp.Value.boolVal); } } }

        public void SendRaw(string message) { try { byte[] msg = Encoding.UTF8.GetBytes(message); _stream.Write(msg, 0, msg.Length); } catch {} }
        public void SendAction(string d) { if (IsMultiplayerReady) SendRaw($"G|{LockstepController.Instance.CurrentTick+2}|{d}\n"); }
        public void SendNameChange(string t, long id, string n) { if (IsMultiplayerReady) SendRaw($"N|{t}|{id}|{Convert.ToBase64String(Encoding.UTF8.GetBytes(n))}\n"); }
        public void SendKingdomCustomization(long id, int c, int b) { if (IsMultiplayerReady) SendRaw($"K|{id}|{c}|{b}\n"); }
        public void SendEraChange(string id) { if (IsMultiplayerReady) SendRaw($"A|{id}\n"); }
        public void SendPowerSelection(string id) { if (IsMultiplayerReady) SendRaw($"P|{id}\n"); }
        public void SendTickSync(int t) { if(IsMultiplayerReady) SendRaw($"T|{t}\n"); }
        public void SendCursorPos(float x, float y) { if(IsMultiplayerReady) SendRaw($"C|{x:F1}|{y:F1}\n"); }
        public void SendLawToggle(string id, bool s) { if(IsMultiplayerReady) SendRaw($"L|{id}|{s}\n"); }
        public void SendSpeedChange(string id) { if(IsMultiplayerReady) SendRaw($"S|{id}\n"); }
        public void SendHash(int t, long h) { if(IsMultiplayerReady) SendRaw($"H|{t}|{h}\n"); }
        public void SendDisconnect() { if(IsMultiplayerReady) SendRaw("D\n"); }
        public void SendClientReady() { if(IsMultiplayerReady) SendRaw("R\n"); }
        public void RequestResync() { if (_isHost) SaveTransferHandler.Instance.StartTransfer(); }

        public void Disconnect()
        {
            try { SendRaw("D\n"); if (_stream != null) _stream.Close(); if (_client != null) _client.Close(); if (_server != null) _server.Stop(); } catch {}
            IsConnected = false; _stream = null; _client = null; _server = null; _isHost = false; IsMapLoaded = false; _incomingBuffer.Clear();
            WorldBoxMultiplayer.instance.UpdateStatus("Disconnected");
        }
        public bool IsHost() { return _isHost; }
    }
}