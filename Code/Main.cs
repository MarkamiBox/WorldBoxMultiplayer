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
        private Rect _windowRect = new Rect(20, 20, 280, 400);
        
        private string _roomCodeInput = "";
        private string _port = "7777";
        private string _status = "Disconnected";
        private string _myRoomCode = "Generating..."; 
        private string _myLocalIP = "127.0.0.1";

        void Awake()
        {
            instance = this;
            try 
            {
                _myLocalIP = GetLocalIPAddress();
                // Generates a short 8-char code
                _myRoomCode = GenerateCompactRoomCode(_myLocalIP, int.Parse(_port));

                Harmony harmony = new Harmony("com.markami.worldbox.multiplayer.final");
                harmony.PatchAll();
                
                if (GetComponent<NetworkManager>() == null) gameObject.AddComponent<NetworkManager>();
                if (GetComponent<LockstepController>() == null) gameObject.AddComponent<LockstepController>();
                if (GetComponent<CursorHandler>() == null) gameObject.AddComponent<CursorHandler>();
                if (GetComponent<SaveTransferHandler>() == null) gameObject.AddComponent<SaveTransferHandler>();

                Debug.Log("MULTIPLAYER ULTIMATE LOADED");
            }
            catch (Exception e) { Debug.LogError(e.Message); }
        }

        // --- SYNC FUNCTIONS ---

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
                ColorAsset colorAsset = AssetManager.kingdom_colors_library.get(colorId.ToString());
                if (colorAsset != null) k.updateColor(colorAsset);
                k.generateBanner();
            }
        }

        public void SetLaw(string id, bool state) {
            LockstepController.Instance.IsRunningManualStep = true;
            if (World.world.world_laws.dict.ContainsKey(id))
                World.world.world_laws.dict[id].boolVal = state;
            LockstepController.Instance.IsRunningManualStep = false;
        }

        public void SetSpeed(string id) {
            Config.setWorldSpeed(id);
            LockstepController.Instance.UpdateTimeScale();
        }

        public void SyncMap(int seed, string size, string template) {
            Config.customMapSize = size;
            Config.current_map_template = template;
            UnityEngine.Random.InitState(seed);
            try { Traverse.Create(typeof(Randy)).Field("rnd").SetValue(new System.Random(seed)); } catch {}
            Traverse.Create(MapBox.instance).Field("mapStats").Field("seed").SetValue(seed);
            MapBox.instance.generateNewMap();
        }

        // --- UI ---

        void Update() { if (Input.GetKeyDown(KeyCode.M)) _showUI = !_showUI; }
        
        void OnGUI() {
            if (!_showUI) return;
            _windowRect = GUI.Window(102030, _windowRect, DrawWindow, "Multiplayer Ultimate");
        }

        void DrawWindow(int id) {
            if (SaveTransferHandler.Instance.IsTransferring) {
                GUI.Label(new Rect(10, 50, 260, 30), "SYNCING WORLD DATA...");
                GUI.Box(new Rect(10, 80, 260, 20), "");
                GUI.color = Color.green;
                GUI.Box(new Rect(10, 80, 260 * SaveTransferHandler.Instance.Progress, 20), "");
                GUI.color = Color.white;
                GUI.Label(new Rect(10, 105, 260, 20), $"{(int)(SaveTransferHandler.Instance.Progress * 100)}%");
                GUI.DragWindow();
                return;
            }

            GUI.color = Color.yellow;
            GUI.Label(new Rect(10, 25, 260, 20), "YOUR IP: " + _myLocalIP);
            GUI.color = Color.white;
            GUI.Label(new Rect(10, 50, 260, 20), "Status: " + _status);
            
            if (LockstepController.Instance.DesyncDetected) {
                GUI.color = Color.red;
                GUI.Label(new Rect(10, 80, 260, 40), "DESYNC DETECTED!\nGame paused.");
                if (NetworkManager.Instance.IsHost() && GUI.Button(new Rect(10, 120, 260, 30), "FIX SYNC")) NetworkManager.Instance.RequestResync();
                GUI.color = Color.white;
            } else if (NetworkManager.Instance.IsConnected) {
                GUI.color = Color.green;
                GUI.Label(new Rect(10, 80, 260, 20), "GAME SYNCED");
                GUI.color = Color.white;
            }

            if (!NetworkManager.Instance.IsConnected) {
                GUI.Label(new Rect(10, 120, 260, 20), "HOST (Room Code):");
                // Make the code field selectable and easier to read
                GUI.TextField(new Rect(10, 140, 260, 20), _myRoomCode);
                
                if (GUI.Button(new Rect(10, 165, 260, 30), "START SERVER")) { 
                    NetworkManager.Instance.StartHost(int.Parse(_port)); 
                    _status = "Waiting for players..."; 
                }

                GUI.Label(new Rect(10, 205, 260, 20), "JOIN (Enter Code):");
                _roomCodeInput = GUI.TextField(new Rect(10, 225, 260, 20), _roomCodeInput);
                
                if (GUI.Button(new Rect(10, 250, 260, 30), "CONNECT")) {
                    if (DecodeCompactRoomCode(_roomCodeInput, out string ip, out int port)) { 
                        NetworkManager.Instance.StartClient(ip, port); 
                        _status = "Connecting..."; 
                    } else { 
                        _status = "Invalid Code"; 
                    }
                }
            } else {
                if (GUI.Button(new Rect(10, 280, 260, 30), "DISCONNECT")) { NetworkManager.Instance.Disconnect(); _status = "Disconnected"; }
                if (NetworkManager.Instance.IsHost() && GUI.Button(new Rect(10, 320, 260, 30), "FORCE SAVE TRANSFER")) SaveTransferHandler.Instance.StartTransfer();
            }
            GUI.DragWindow();
        }

        // --- COMPACT CODE GENERATOR (8 Chars) ---
        public string GenerateCompactRoomCode(string ip, int port) {
            try {
                byte[] ipBytes = IPAddress.Parse(ip).GetAddressBytes(); // 4 bytes
                byte[] portBytes = BitConverter.GetBytes((ushort)port); // 2 bytes
                
                // Combine into 6 bytes array
                byte[] combined = new byte[6];
                Array.Copy(ipBytes, 0, combined, 0, 4);
                Array.Copy(portBytes, 0, combined, 4, 2);

                // Convert to Base64 and remove padding "==" to make it look like a code
                return Convert.ToBase64String(combined).Replace("=", "");
            } catch { return "Error"; }
        }

        public bool DecodeCompactRoomCode(string code, out string ip, out int port) {
            ip = ""; port = 0;
            try {
                // Restore padding if needed
                string base64 = code;
                while (base64.Length % 4 != 0) base64 += "=";

                byte[] bytes = Convert.FromBase64String(base64);
                if (bytes.Length < 6) return false;

                // Extract IP
                byte[] ipBytes = new byte[4];
                Array.Copy(bytes, 0, ipBytes, 0, 4);
                ip = new IPAddress(ipBytes).ToString();

                // Extract Port
                port = BitConverter.ToUInt16(bytes, 4);
                return true;
            } catch { return false; }
        }

        public static string GetLocalIPAddress() {
            try {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
            } catch {}
            return "127.0.0.1";
        }
    }
}