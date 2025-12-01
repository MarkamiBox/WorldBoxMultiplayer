using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System;
using System.Collections;
using HarmonyLib; 

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

        void Awake() { Instance = this; }

        public void StartHost(int port)
        {
            try {
                _server = new TcpListener(System.Net.IPAddress.Any, port);
                _server.Start();
                _server.BeginAcceptTcpClient(OnClientConnected, null);
                _isHost = true;
                IsConnected = true; 
                IsMapLoaded = true; // L'host ha già la mappa
                WorldBoxMultiplayer.instance.UpdateStatus("Waiting for players...");
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
                IsMapLoaded = false; // Client aspetta la mappa
                WorldBoxMultiplayer.instance.UpdateStatus("Connected! Waiting for Map...");
                StartCoroutine(KeepAliveRoutine());
                Debug.Log("[Multiplayer] Client Connected!");
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
                _shouldStartTransfer = true; // Trigger auto-sync
                
                // Eseguiamo l'aggiornamento UI nel thread principale via Update()
                
            } catch (Exception e) { Debug.LogError("Conn Error: " + e.Message); }
        }

        public void SendRaw(string message) { try { byte[] msg = Encoding.UTF8.GetBytes(message); _stream.Write(msg, 0, msg.Length); } catch {} }
        
        // Action wrappers
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
        public void RequestResync() { if (_isHost) SaveTransferHandler.Instance.StartTransfer(); }

        private IEnumerator KeepAliveRoutine()
        {
            while (IsConnected) {
                if (_isHost) SendTickSync(LockstepController.Instance.CurrentTick);
                yield return new WaitForSeconds(1f);
            }
        }

        void Update()
        {
            // Logica Host nel thread principale
            if (_shouldStartTransfer) { 
                WorldBoxMultiplayer.instance.UpdateStatus("Player Joined! Syncing...");
                SaveTransferHandler.Instance.StartTransfer(); 
                _shouldStartTransfer = false; 
            }
            
            if (!IsMultiplayerReady) return;

            if (_stream.DataAvailable)
            {
                try {
                    byte[] data = new byte[65536]; 
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
                        
                        // Elabora azioni solo se la mappa è caricata
                        else if (IsMapLoaded) {
                            if (type == "G") LockstepController.Instance.AddPendingAction(int.Parse(parts[1]), parts[2]);
                            else if (type == "T") LockstepController.Instance.SetServerTick(int.Parse(parts[1]));
                            else if (type == "C" && CursorHandler.Instance) CursorHandler.Instance.UpdateRemoteCursor(float.Parse(parts[1]), float.Parse(parts[2]));
                            else if (type == "P" && CursorHandler.Instance) CursorHandler.Instance.SetRemotePower(parts[1]);
                            else if (type == "L") WorldBoxMultiplayer.instance.SetLaw(parts[1], bool.Parse(parts[2]));
                            else if (type == "S") WorldBoxMultiplayer.instance.SetSpeed(parts[1]);
                            else if (type == "H") LockstepController.Instance.CheckRemoteHash(int.Parse(parts[1]), long.Parse(parts[2]));
                            else if (type == "N") WorldBoxMultiplayer.instance.SetName(parts[1], long.Parse(parts[2]), parts[3]);
                            else if (type == "A") WorldBoxMultiplayer.instance.SetEra(parts[1]);
                            else if (type == "K") WorldBoxMultiplayer.instance.SetKingdomData(long.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
                        }
                        
                        if (type == "D") Disconnect();
                    }
                } catch (Exception e) { Debug.LogError("NetRead: " + e.Message); }
            }
            
            if (IsMapLoaded) LockstepController.Instance.NetworkUpdate();
        }

        public void Disconnect()
        {
            try {
                SendRaw("D\n");
                if (_stream != null) _stream.Close();
                if (_client != null) _client.Close();
                if (_server != null) _server.Stop();
            } catch {}
            IsConnected = false;
            _stream = null;
            _client = null;
            _server = null;
            _isHost = false;
            IsMapLoaded = false;
            WorldBoxMultiplayer.instance.UpdateStatus("Disconnected");
        }
        
        public bool IsHost() { return _isHost; }
    }
}