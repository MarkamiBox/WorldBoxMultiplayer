using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System;
using System.Collections.Generic;

namespace WorldBoxMultiplayer
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance;
        public bool IsConnected = false;
        
        public bool IsMultiplayerReady 
        {
            get { return IsConnected && _stream != null; }
        }

        private TcpClient _client;
        private TcpListener _server;
        private NetworkStream _stream;
        private bool _isHost = false;

        void Awake()
        {
            Instance = this;
        }

        public void StartHost(int port)
        {
            try {
                _server = new TcpListener(System.Net.IPAddress.Any, port);
                _server.Start();
                _server.BeginAcceptTcpClient(OnClientConnected, null);
                _isHost = true;
                IsConnected = true; 
                Debug.Log("[Multiplayer] Server Avviato. In attesa...");
            } catch (Exception e) { Debug.LogError("Errore Host: " + e.Message); }
        }

        public void StartClient(string ip, int port)
        {
            try {
                _client = new TcpClient();
                _client.NoDelay = true; // Fondamentale per ridurre il lag
                _client.Connect(ip, port);
                _stream = _client.GetStream();
                IsConnected = true;
                Debug.Log("[Multiplayer] Client Connesso!");
            } catch (Exception e) { Debug.LogError("Errore Client: " + e.Message); }
        }

        private void OnClientConnected(IAsyncResult ar)
        {
            try {
                _client = _server.EndAcceptTcpClient(ar);
                _client.NoDelay = true;
                _stream = _client.GetStream();
                Debug.Log("[Multiplayer] Client entrato!");
                
                // Appena entra, invia il Seed attuale per sincronizzare la mappa
                SendMapSeed();
            } catch (Exception e) { Debug.LogError("Errore connessione: " + e.Message); }
        }

        public void SendMapSeed()
        {
            if (!IsMultiplayerReady) return;
            // M | Seed | SizeID
            int seed = World.world.mapStats.seed;
            string size = Config.customMapSize; // Es "standard" o "giant"
            SendRaw($"M|{seed}|{size}\n");
        }

        public void SendAction(string actionData)
        {
            if (!IsMultiplayerReady) return;
            // G | TickTarget | Azione
            int targetTick = LockstepController.Instance.CurrentTick + 2; // Ritardo ridotto a 2 tick
            SendRaw($"G|{targetTick}|{actionData}\n");
        }

        public void SendTickSync(int tick)
        {
            if (!IsMultiplayerReady || !_isHost) return;
            // T | TickCorrente (Dice al client: "Puoi avanzare fino a qui")
            SendRaw($"T|{tick}\n");
        }

        public void SendCursorPos(float x, float y)
        {
            if (!IsMultiplayerReady) return;
            SendRaw($"C|{x:F1}|{y:F1}\n");
        }

        private void SendRaw(string message)
        {
            try {
                byte[] msg = Encoding.ASCII.GetBytes(message);
                _stream.Write(msg, 0, msg.Length);
            } catch {}
        }

        void Update()
        {
            if (!IsMultiplayerReady) return;

            if (_stream.DataAvailable)
            {
                try {
                    byte[] data = new byte[8192]; 
                    int bytes = _stream.Read(data, 0, data.Length);
                    string responseData = Encoding.ASCII.GetString(data, 0, bytes);
                    
                    string[] lines = responseData.Split('\n');
                    foreach(var line in lines)
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        string[] parts = line.Split('|');
                        if (parts.Length < 2) continue;

                        string type = parts[0];

                        if (type == "G") // Game Action
                        {
                            int tick = int.Parse(parts[1]);
                            string content = parts[2];
                            LockstepController.Instance.AddPendingAction(tick, content);
                        }
                        else if (type == "T") // Tick Sync (Dal Server)
                        {
                            int serverTick = int.Parse(parts[1]);
                            LockstepController.Instance.SetServerTick(serverTick);
                        }
                        else if (type == "C") // Cursore
                        {
                            float x = float.Parse(parts[1]);
                            float y = float.Parse(parts[2]);
                            if (CursorHandler.Instance) CursorHandler.Instance.UpdateRemoteCursor(x, y);
                        }
                        else if (type == "M") // Map Sync
                        {
                            int seed = int.Parse(parts[1]);
                            string size = parts[2];
                            // Rigenera il mondo con lo stesso seme!
                            WorldBoxMultiplayer.instance.SyncMap(seed, size);
                        }
                    }
                } catch (Exception e) { Debug.LogError("Errore lettura: " + e.Message); }
            }
            
            LockstepController.Instance.NetworkUpdate();
        }

        public void Disconnect()
        {
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