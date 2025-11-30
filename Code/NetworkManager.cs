using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System;

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
                // OTTIMIZZAZIONE LAG: Disabilita il buffering di Nagle per inviare pacchetti piccoli istantaneamente
                _client.NoDelay = true; 
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
                _client.NoDelay = true; // OTTIMIZZAZIONE LAG ANCHE PER HOST
                _stream = _client.GetStream();
                Debug.Log("[Multiplayer] Client entrato! Sincronizzazione avviata.");
            } catch (Exception e) { Debug.LogError("Errore connessione: " + e.Message); }
        }

        // Invia un'azione di gioco (Spawn, Potere)
        public void SendAction(string actionData)
        {
            if (!IsMultiplayerReady) return;
            try {
                // Formato: "G|TICK|DATI" (G = Game Action)
                int targetTick = LockstepController.Instance.CurrentTick + 5; // Buffer di 5 tick
                string packet = $"G|{targetTick}|{actionData}\n";
                SendRaw(packet);
            } catch (Exception e) { Debug.LogWarning("Errore invio azione: " + e.Message); }
        }

        // Invia la posizione del cursore (Priorità bassa, non sincronizzata col Tick)
        public void SendCursorPos(float x, float y)
        {
            if (!IsMultiplayerReady) return;
            try {
                // Formato: "C|X|Y" (C = Cursor)
                // Usiamo numeri interi o float con pochi decimali per risparmiare banda
                string packet = $"C|{x:F1}|{y:F1}\n"; 
                SendRaw(packet);
            } catch {}
        }

        private void SendRaw(string message)
        {
            byte[] msg = Encoding.ASCII.GetBytes(message);
            _stream.Write(msg, 0, msg.Length);
        }

        void Update()
        {
            if (!IsMultiplayerReady) return;

            if (_stream.DataAvailable)
            {
                try {
                    byte[] data = new byte[4096];
                    int bytes = _stream.Read(data, 0, data.Length);
                    string responseData = Encoding.ASCII.GetString(data, 0, bytes);
                    
                    string[] lines = responseData.Split('\n');
                    foreach(var line in lines)
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        
                        // Analizziamo il tipo di pacchetto
                        string[] parts = line.Split('|');
                        if (parts.Length < 2) continue;

                        string type = parts[0];

                        if (type == "G") // Game Action (Lockstep)
                        {
                            int tick = int.Parse(parts[1]);
                            string content = parts[2];
                            
                            if (!LockstepController.Instance.PendingActions.ContainsKey(tick))
                                LockstepController.Instance.PendingActions[tick] = new System.Collections.Generic.List<string>();
                            
                            LockstepController.Instance.PendingActions[tick].Add(content);
                        }
                        else if (type == "C") // Cursor Update (Immediato)
                        {
                            if (parts.Length >= 3)
                            {
                                float x = float.Parse(parts[1]);
                                float y = float.Parse(parts[2]);
                                // Aggiorna visivamente il cursore dell'amico
                                CursorHandler.Instance.UpdateRemoteCursor(x, y);
                            }
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
    }
}