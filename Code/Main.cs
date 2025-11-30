using UnityEngine;
using HarmonyLib;
using System.Reflection;
using NCMS;
using System.Net; 
using System.Net.Sockets;

namespace WorldBoxMultiplayer
{
    // Nome classe unico per evitare conflitti con RulerBox o altre mod
    [ModEntry]
    public class MultiplayerModMain : MonoBehaviour
    {
        public static MultiplayerModMain instance;
        private bool _showUI = false;
        private Rect _windowRect = new Rect(20, 20, 250, 230);
        
        // UI Variables
        private string _ipAddress = "127.0.0.1";
        private string _port = "7777";
        private string _status = "Non Connesso";
        private string _lastError = "";
        private string _myLocalIP = "Cercando...";

        void Awake()
        {
            instance = this;
            try 
            {
                Debug.Log(">>> WORLD BOX MULTIPLAYER: AVVIO IN CORSO <<<");
                _myLocalIP = GetLocalIPAddress();

                Harmony harmony = new Harmony("com.markami.worldbox.multiplayer.unique");
                try {
                    harmony.PatchAll();
                } catch (System.Exception ex) {
                    Debug.LogError("Harmony Errore: " + ex.Message);
                }
                
                if (GetComponent<NetworkManager>() == null) gameObject.AddComponent<NetworkManager>();
                if (GetComponent<LockstepController>() == null) gameObject.AddComponent<LockstepController>();
                
                // --- AGGIUNTA: Componente Cursore ---
                if (GetComponent<CursorHandler>() == null) gameObject.AddComponent<CursorHandler>();
                // ------------------------------------
                
                Debug.Log(">>> MOD CARICATA <<<");
            }
            catch (System.Exception e)
            {
                Debug.LogError("ERRORE AWAKE MOD: " + e.ToString());
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.M))
            {
                _showUI = !_showUI;
            }
        }

        void OnGUI()
        {
            if (!_showUI) return;
            _windowRect = GUI.Window(888999, _windowRect, DrawNetworkWindow, "Multiplayer Mod");
        }

        void DrawNetworkWindow(int windowID)
        {
            if (!string.IsNullOrEmpty(_lastError))
            {
                GUI.color = Color.red;
                GUI.Label(new Rect(10, 20, 230, 40), "ERRORE: " + _lastError);
                GUI.color = Color.white;
                GUI.DragWindow();
                return;
            }

            GUI.Label(new Rect(10, 20, 230, 20), "Stato: " + _status);

            GUI.color = Color.yellow;
            GUI.Label(new Rect(10, 40, 230, 20), "Il tuo IP:");
            GUI.TextField(new Rect(10, 60, 230, 20), _myLocalIP);
            GUI.color = Color.white;

            if (!NetworkManager.Instance.IsConnected)
            {
                GUI.Label(new Rect(10, 90, 100, 20), "IP Amico:");
                _ipAddress = GUI.TextField(new Rect(80, 90, 160, 20), _ipAddress);

                GUI.Label(new Rect(10, 115, 100, 20), "Porta:");
                _port = GUI.TextField(new Rect(80, 115, 160, 20), _port);

                if (GUI.Button(new Rect(10, 145, 110, 30), "HOST"))
                {
                    _status = "Avvio Server...";
                    if (NetworkManager.Instance != null) {
                        NetworkManager.Instance.StartHost(int.Parse(_port));
                        _status = "Server Attivo";
                    }
                }

                if (GUI.Button(new Rect(130, 145, 110, 30), "JOIN"))
                {
                    _status = "Connessione...";
                    if (NetworkManager.Instance != null) {
                        NetworkManager.Instance.StartClient(_ipAddress, int.Parse(_port));
                        _status = "Connesso";
                    }
                }
            }
            else
            {
                if (GUI.Button(new Rect(10, 100, 230, 30), "DISCONNETTI"))
                {
                    NetworkManager.Instance.Disconnect();
                    _status = "Disconnesso";
                }
                GUI.Label(new Rect(10, 140, 230, 40), "Sincronizzato.");
            }
            GUI.DragWindow();
        }

        public static string GetLocalIPAddress()
        {
            try {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            } catch {}
            return "127.0.0.1";
        }
    }
}