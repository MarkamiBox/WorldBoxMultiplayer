using UnityEngine;
using HarmonyLib;
using System.Reflection;
using NCMS; 
using System.Net; 
using System.Net.Sockets;
using System.Text;
using System;

namespace WorldBoxMultiplayer
{
    [ModEntry]
    public class WorldBoxMultiplayer : MonoBehaviour
    {
        public static WorldBoxMultiplayer instance;
        private bool _showUI = false;
        private Rect _windowRect = new Rect(20, 20, 300, 400);
        
        private string _roomCodeInput = "";
        private string _port = "7777";
        private string _status = "Disconnected";
        private string _myRoomCode = "Generating..."; 
        private string _myLocalIP = "127.0.0.1";

        void Awake()
        {
            instance = this;
            _myLocalIP = GetLocalIPAddress();
            _myRoomCode = GenerateRoomCode(_myLocalIP, "7777");
            
            // Add components FIRST, before patching
            try {
                if (GetComponent<NetworkManager>() == null) gameObject.AddComponent<NetworkManager>();
                if (GetComponent<StateSyncManager>() == null) gameObject.AddComponent<StateSyncManager>();
                if (GetComponent<ClientController>() == null) gameObject.AddComponent<ClientController>();
                if (GetComponent<CursorHandler>() == null) gameObject.AddComponent<CursorHandler>();
                if (GetComponent<SaveTransferHandler>() == null) gameObject.AddComponent<SaveTransferHandler>();
                Debug.Log("[Multiplayer] Components added successfully");
            } catch (Exception e) { 
                Debug.LogError("[Multiplayer] Failed to add components: " + e.Message); 
            }
            
            // Apply Harmony patches separately
            try {
                Harmony harmony = new Harmony("com.markami.worldbox.multiplayer.authserver");
                harmony.PatchAll();
                Debug.Log("[Multiplayer] Harmony patches applied");
            } catch (Exception e) { 
                Debug.LogError("[Multiplayer] Harmony patching failed (non-critical): " + e.Message); 
            }
            
            Debug.Log("MULTIPLAYER AUTHSERVER LOADED");
        }
        
        public void UpdateStatus(string s) { _status = s; }

        public void SetName(string type, long id, string name64) {
            try {
                string name = Encoding.UTF8.GetString(Convert.FromBase64String(name64));
                if (type == "Actor") { foreach(var a in World.world.units) if(a.id == id) { a.data.name = name; break; } }
                else if (type == "City") { City c = World.world.cities.get(id); if (c != null) c.data.name = name; }
                else if (type == "Kingdom") { Kingdom k = World.world.kingdoms.get(id); if (k != null) k.data.name = name; }
            } catch {}
        }
        
        public void SetEra(string eraID) {
            WorldAgeAsset asset = AssetManager.era_library.get(eraID);
            if (asset != null) {
                var eraMgr = World.world.era_manager;
                var method = eraMgr.GetType().GetMethod("setEra") ?? eraMgr.GetType().GetMethod("SetEra");
                if (method != null) method.Invoke(eraMgr, new object[] { asset, true });
            }
        }
        
        public void SetKingdomData(long id, int colorId, int bannerId) {
            Kingdom k = World.world.kingdoms.get(id);
            if (k != null) { 
                k.data.color_id = colorId; 
                k.data.banner_icon_id = bannerId; 
                ColorAsset c = AssetManager.kingdom_colors_library.get(colorId.ToString()); 
                if(c!=null) k.updateColor(c); 
                k.generateBanner(); 
            }
        }
        
        public void SetLaw(string id, bool state) {
            if (World.world.world_laws.dict.ContainsKey(id)) World.world.world_laws.dict[id].boolVal = state;
        }
        
        public void SetSpeed(string id) {
            Config.setWorldSpeed(id);
        }

        void Update() { 
            if (Input.GetKeyDown(KeyCode.M)) _showUI = !_showUI; 
        }

        void OnGUI() {
            if (!_showUI) return;
            _windowRect = GUI.Window(102030, _windowRect, DrawWindow, "Multiplayer (Auth Server)");
        }

        void DrawWindow(int id) {
            var netMgr = NetworkManager.Instance ?? GetComponent<NetworkManager>();
            var saveTrans = SaveTransferHandler.Instance ?? GetComponent<SaveTransferHandler>();
            var clientCtrl = ClientController.Instance ?? GetComponent<ClientController>();
            var syncMgr = StateSyncManager.Instance ?? GetComponent<StateSyncManager>();
            
            if (saveTrans != null && saveTrans.IsTransferring) {
                GUI.Label(new Rect(10, 50, 260, 30), "SYNCING WORLD... PLEASE WAIT");
                GUI.Box(new Rect(10, 80, 260, 20), "");
                GUI.color = Color.green;
                GUI.Box(new Rect(10, 80, 260 * saveTrans.Progress, 20), "");
                GUI.color = Color.white;
                GUI.Label(new Rect(10, 105, 260, 20), $"{(int)(saveTrans.Progress * 100)}%");
                GUI.DragWindow();
                return;
            }
            
            GUI.color = Color.yellow;
            GUI.Label(new Rect(10, 25, 260, 20), "IP: " + _myLocalIP);
            GUI.color = Color.white;
            GUI.Label(new Rect(10, 50, 260, 20), "Status: " + _status);
            
            bool isConnected = netMgr != null && netMgr.IsConnected;
            bool isHost = netMgr != null && netMgr.IsHost();
            bool isClient = clientCtrl != null && clientCtrl.IsClientMode;
            
            if (isConnected) {
                GUI.color = Color.green;
                if (isHost) GUI.Label(new Rect(10, 70, 260, 20), "MODE: HOST (Authoritative)");
                else if (isClient) GUI.Label(new Rect(10, 70, 260, 20), "MODE: CLIENT (Visual Sync)");
                GUI.color = Color.white;
                
                if (syncMgr != null && isHost) {
                    GUI.Label(new Rect(10, 90, 260, 20), $"Sync Interval: {syncMgr.SyncInterval:F2}s");
                }
            }

            if (!isConnected) {
                GUI.Label(new Rect(10, 120, 260, 20), "HOST CODE:");
                GUI.TextField(new Rect(10, 140, 260, 20), _myRoomCode);
                if (GUI.Button(new Rect(10, 165, 260, 30), "HOST")) { 
                    if (netMgr != null) {
                        netMgr.StartHost(int.Parse(_port)); 
                        _status = "Waiting..."; 
                    } else {
                        _status = "Error: NetworkManager not found";
                    }
                }
                
                GUI.Label(new Rect(10, 205, 260, 20), "JOIN CODE:");
                _roomCodeInput = GUI.TextField(new Rect(10, 225, 260, 20), _roomCodeInput);
                if (GUI.Button(new Rect(10, 250, 260, 30), "CONNECT")) {
                    if (netMgr != null) {
                        if (DecodeRoomCode(_roomCodeInput, out string ip, out int port)) { 
                            netMgr.StartClient(ip, port); 
                            _status = "Connecting..."; 
                        }
                        else _status = "Invalid Code";
                    } else {
                        _status = "Error: NetworkManager not found";
                    }
                }
            } else {
                if (GUI.Button(new Rect(10, 290, 260, 30), "DISCONNECT")) netMgr.Disconnect();
                
                if (isHost && saveTrans != null) {
                    GUI.Label(new Rect(10, 330, 260, 20), "HOST CONTROLS:");
                    if (GUI.Button(new Rect(10, 350, 260, 30), "Force Full Resync")) 
                        saveTrans.StartTransfer();
                }
            }
            GUI.DragWindow();
        }
        
        public string GenerateRoomCode(string ip, string port) { 
            return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ip}:{port}")); 
        }
        
        public bool DecodeRoomCode(string code, out string ip, out int port) {
            ip = ""; port = 0;
            try { 
                string[] parts = Encoding.UTF8.GetString(Convert.FromBase64String(code)).Split(':'); 
                ip = parts[0]; port = int.Parse(parts[1]); 
                return true; 
            } catch { return false; }
        }
        
        public static string GetLocalIPAddress() { 
            try {
                string fallbackIP = "127.0.0.1";
                var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
                
                // First pass: look for common LAN addresses (prefer 192.168.1.x, 192.168.0.x, 10.x.x.x)
                foreach (var ip in addresses) {
                    if (ip.AddressFamily == AddressFamily.InterNetwork) {
                        string ipStr = ip.ToString();
                        // Skip known virtual adapter ranges
                        if (ipStr.StartsWith("192.168.56.") || // VirtualBox
                            ipStr.StartsWith("192.168.137.") || // Windows ICS
                            ipStr.StartsWith("169.254.")) // Link-local
                            continue;
                        
                        // Prefer common home network ranges
                        if (ipStr.StartsWith("192.168.1.") || 
                            ipStr.StartsWith("192.168.0.") ||
                            ipStr.StartsWith("10."))
                            return ipStr;
                        
                        fallbackIP = ipStr; // Keep as fallback
                    }
                }
                return fallbackIP;
            } catch {} 
            return "127.0.0.1"; 
        }
    }
}