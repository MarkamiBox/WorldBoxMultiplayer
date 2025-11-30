using UnityEngine;
using HarmonyLib;
using System.Reflection;
using NCMS; 
using System.Net; 
using System.Net.Sockets;

namespace WorldBoxMultiplayer
{
    [ModEntry]
    public class WorldBoxMultiplayer : MonoBehaviour
    {
        public static WorldBoxMultiplayer instance;
        private bool _showUI = false;
        private Rect _windowRect = new Rect(20, 20, 250, 260);
        
        private string _ipAddress = "127.0.0.1";
        private string _port = "7777";
        private string _status = "Non Connesso";
        private string _myLocalIP = "Cercando...";

        void Awake()
        {
            instance = this;
            try 
            {
                _myLocalIP = GetLocalIPAddress();
                Harmony harmony = new Harmony("com.markami.worldbox.multiplayer.unique");
                harmony.PatchAll();
                
                if (GetComponent<NetworkManager>() == null) gameObject.AddComponent<NetworkManager>();
                if (GetComponent<LockstepController>() == null) gameObject.AddComponent<LockstepController>();
                if (GetComponent<CursorHandler>() == null) gameObject.AddComponent<CursorHandler>();

                Debug.Log("MOD MULTIPLAYER CARICATA");
            }
            catch (System.Exception e) { Debug.LogError(e.Message); }
        }

        public void SyncMap(int seed, string size)
        {
            Debug.Log($"[Sync] Generazione mondo Sync: Seed {seed}, Size {size}");
            Config.customMapSize = size;
            
            // FIX: Usiamo Reflection per impostare il seed perché mapStats potrebbe essere inaccessibile
            Traverse.Create(MapBox.instance).Field("mapStats").Field("seed").SetValue(seed);
            
            // FIX: generateNewMap non vuole argomenti
            MapBox.instance.generateNewMap(); 
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.M)) _showUI = !_showUI;
        }

        void OnGUI()
        {
            if (!_showUI) return;
            _windowRect = GUI.Window(102030, _windowRect, DrawWindow, "Multiplayer Mod");
        }

        void DrawWindow(int id)
        {
            GUI.color = Color.yellow;
            GUI.Label(new Rect(10, 25, 230, 20), "TUO IP: " + _myLocalIP);
            GUI.color = Color.white;
            
            GUI.Label(new Rect(10, 50, 230, 20), "Stato: " + _status);

            if (!NetworkManager.Instance.IsConnected)
            {
                _ipAddress = GUI.TextField(new Rect(10, 75, 150, 20), _ipAddress);
                _port = GUI.TextField(new Rect(170, 75, 50, 20), _port);

                if (GUI.Button(new Rect(10, 105, 100, 30), "HOST"))
                {
                    NetworkManager.Instance.StartHost(int.Parse(_port));
                    _status = "Hosting...";
                }
                if (GUI.Button(new Rect(120, 105, 100, 30), "JOIN"))
                {
                    NetworkManager.Instance.StartClient(_ipAddress, int.Parse(_port));
                    _status = "Connecting...";
                }
            }
            else
            {
                if (GUI.Button(new Rect(10, 105, 210, 30), "DISCONNECT"))
                {
                    NetworkManager.Instance.Disconnect();
                    _status = "Disconnesso";
                }
                
                if (NetworkManager.Instance.IsHost())
                {
                    if (GUI.Button(new Rect(10, 145, 210, 30), "FORZA SYNC MAPPA"))
                    {
                        NetworkManager.Instance.SendMapSeed();
                    }
                }
            }
            GUI.DragWindow();
        }

        public static string GetLocalIPAddress()
        {
            try {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
            } catch {}
            return "127.0.0.1";
        }
    }
}