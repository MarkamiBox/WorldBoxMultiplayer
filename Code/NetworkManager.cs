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
        
        // Nuova variabile: true solo se siamo connessi E lo scambio dati è attivo
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
                IsConnected = true; // Siamo connessi come logica, ma non ancora "Ready" per giocare
                Debug.Log("[Multiplayer] Server Avviato. In attesa di giocatori...");
            } catch (Exception e) { Debug.LogError("Errore Host: " + e.Message); }
        }

        public void StartClient(string ip, int port)
        {
            try {
                _client = new TcpClient();
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
                _stream = _client.GetStream();
                Debug.Log("[Multiplayer] Un client si è unito! Iniziamo la simulazione.");
            } catch (Exception e) { Debug.LogError("Errore connessione in entrata: " + e.Message); }
        }

        public void SendAction(string actionData)
        {
            if (!IsMultiplayerReady) return;
            
            try {
                int targetTick = LockstepController.Instance.CurrentTick + 5;
                string packet = $"{targetTick}|{actionData}\n";
                byte[] msg = Encoding.ASCII.GetBytes(packet);
                _stream.Write(msg, 0, msg.Length);
            } catch (Exception e) { Debug.LogWarning("Errore invio: " + e.Message); }
        }

        void Update()
        {
            // Se non siamo "Pronti" (es. Host che aspetta), non facciamo nulla.
            // Questo impedisce al gioco di bloccarsi mentre aspetti l'amico.
            if (!IsMultiplayerReady) return;

            // Lettura dati
            if (_stream.DataAvailable)
            {
                try {
                    byte[] data = new byte[4096]; // Buffer più grande
                    int bytes = _stream.Read(data, 0, data.Length);
                    string responseData = Encoding.ASCII.GetString(data, 0, bytes);
                    
                    string[] lines = responseData.Split('\n');
                    foreach(var line in lines)
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        string[] parts = line.Split('|');
                        if (parts.Length >= 2)
                        {
                            int tick = int.Parse(parts[0]);
                            string content = parts[1];
                            
                            if (!LockstepController.Instance.PendingActions.ContainsKey(tick))
                                LockstepController.Instance.PendingActions[tick] = new System.Collections.Generic.List<string>();
                            
                            LockstepController.Instance.PendingActions[tick].Add(content);
                        }
                    }
                } catch (Exception e) { Debug.LogError("Errore lettura rete: " + e.Message); }
            }
            
            // FONDAMENTALE: Facciamo avanzare il gioco
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