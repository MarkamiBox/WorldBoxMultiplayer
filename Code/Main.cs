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
        private Rect _windowRect = new Rect(20, 20, 280, 280);
        
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

                Debug.Log("MOD MULTIPLAYER CARICATA: PRONTA");
            }
            catch (System.Exception e) { Debug.LogError(e.Message); }
        }

        public void SyncMap(int seed, string size)
        {
            Debug.Log($"[Sync] Generazione... Seed: {seed}");
            
            // 1. Imposta dimensione
            Config.customMapSize = size;
            
            // 2. RESET TOTALE RNG
            UnityEngine.Random.InitState(seed);
            
            // 3. Imposta il seed interno (FIX: usa Traverse perché mapStats è privato)
            Traverse.Create(MapBox.instance).Field("mapStats").Field("seed").SetValue(seed);
            
            // 4. Genera (FIX: generateNewMap non vuole argomenti)
            MapBox.instance.generateNewMap();
            
            // 5. Centra la camera (FIX: usa Reflection per trovare move_camera)
            try {
                object moveCam = Traverse.Create(MapBox.instance).Field("move_camera").GetValue();
                if (moveCam != null)
                {
                     Traverse.Create(moveCam).Method("centerCamera").GetValue();
                }
            } catch {}
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.M)) _showUI = !_showUI;
        }

        void OnGUI()
        {
            if (!_showUI) return;
            _windowRect = GUI.Window(102030, _windowRect, DrawWindow, "Multiplayer v1.0");
        }

        void DrawWindow(int id)
        {
            GUI.color = Color.yellow;
            GUI.Label(new Rect(10, 25, 260, 20), "TUO IP: " + _myLocalIP);
            GUI.color = Color.white;
            
            GUI.Label(new Rect(10, 50, 260, 20), "Stato: " + _status);

            if (!NetworkManager.Instance.IsConnected)
            {
                _ipAddress = GUI.TextField(new Rect(10, 75, 180, 20), _ipAddress);
                _port = GUI.TextField(new Rect(200, 75, 50, 20), _port);

                if (GUI.Button(new Rect(10, 105, 120, 30), "HOST"))
                {
                    NetworkManager.Instance.StartHost(int.Parse(_port));
                    _status = "In attesa...";
                }
                if (GUI.Button(new Rect(140, 105, 120, 30), "JOIN"))
                {
                    NetworkManager.Instance.StartClient(_ipAddress, int.Parse(_port));
                    _status = "Connessione...";
                }
            }
            else
            {
                if (GUI.Button(new Rect(10, 105, 260, 30), "DISCONNECT"))
                {
                    NetworkManager.Instance.Disconnect();
                    _status = "Disconnesso";
                }
                
                if (NetworkManager.Instance.IsHost())
                {
                    GUI.color = Color.green;
                    if (GUI.Button(new Rect(10, 145, 260, 40), "SINCRONIZZA MAPPA"))
                    {
                        NetworkManager.Instance.SendMapSeed();
                    }
                    GUI.color = Color.white;
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