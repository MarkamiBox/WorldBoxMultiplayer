using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WorldBoxMultiplayer
{
    public class LockstepController : MonoBehaviour
    {
        public static LockstepController Instance;
        public const float BaseDeltaTime = 0.05f; // 20 Ticks/Second for smoother gameplay
        public float CurrentDeltaTime = 0.05f;
        public bool IsRunningManualStep = false; 
        public int CurrentTick = 0;
        public bool DesyncDetected = false;
        
        private float _accumulatedTime = 0f;
        private MethodInfo _mapBoxUpdateMethod;
        private bool _initFailed = false;
        private int _serverTickLimit = 0; 

        public Dictionary<int, List<string>> PendingActions = new Dictionary<int, List<string>>();

        void Awake() { Instance = this; }

        public void AddPendingAction(int tick, string action) {
            if (!PendingActions.ContainsKey(tick)) PendingActions[tick] = new List<string>();
            PendingActions[tick].Add(action);
        }
        public void SetServerTick(int tick) { _serverTickLimit = tick; }
        
        public void UpdateTimeScale() {
            float speedVal = 1f;
            try {
                var field = Traverse.Create(Config.time_scale_asset).Field("speed_val");
                if (field.FieldExists()) speedVal = (float)field.GetValue<int>();
            } catch { speedVal = 1f; }
            CurrentDeltaTime = BaseDeltaTime * speedVal;
        }

        public void CheckRemoteHash(int tick, long remoteHash) {
            long localHash = CalculateWorldHash();
            if (localHash != remoteHash) {
                DesyncDetected = true;
            } else DesyncDetected = false;
        }

        private long CalculateWorldHash() {
            if (World.world == null) return 0;
            long hash = 0;
            hash += World.world.units.Count * 1000;
            hash += World.world.cities.Count * 1000000;
            return hash;
        }

        public void NetworkUpdate() {
            if (!NetworkManager.Instance.IsConnected || _initFailed || !NetworkManager.Instance.IsMapLoaded) return;

            if (_mapBoxUpdateMethod == null) {
                if (World.world == null) return;
                
                // Metodo di ricerca migliorato
                _mapBoxUpdateMethod = typeof(MapBox).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                
                if (_mapBoxUpdateMethod == null) { 
                    _initFailed = true; 
                    Debug.LogError("[Lockstep] CRITICAL: MapBox.Update() NOT FOUND! Game will freeze.");
                    return; 
                }
                UpdateTimeScale();
                Debug.Log("[Lockstep] Engine Hooked Successfully.");
            }

            _accumulatedTime += Time.deltaTime;
            int loops = 0;
            // Allow up to 10 loops to catch up if lagging
            while (_accumulatedTime >= BaseDeltaTime && loops < 10) {
                // Host is the authority, so it can always advance. Client waits for server tick.
                bool canAdvance = NetworkManager.Instance.IsHost() || CurrentTick < _serverTickLimit;
                
                if (canAdvance) {
                    RunGameTick();
                    _accumulatedTime -= BaseDeltaTime;
                    
                    // Sync hash less frequently to save bandwidth (every 100 ticks = 5 seconds)
                    if (CurrentTick % 100 == 0) NetworkManager.Instance.SendHash(CurrentTick, CalculateWorldHash());
                    
                    // Host sends tick sync every tick to keep clients smooth
                    if (NetworkManager.Instance.IsHost()) NetworkManager.Instance.SendTickSync(CurrentTick);
                    
                    loops++;
                } else {
                    // Client is ahead of server, wait.
                    _accumulatedTime = 0; // Prevent accumulation while waiting
                    break; 
                }
            }
        }

        private void RunGameTick() {
            if (PendingActions.ContainsKey(CurrentTick)) {
                foreach (var actionJson in PendingActions[CurrentTick]) ExecuteAction(actionJson);
                PendingActions.Remove(CurrentTick);
            }
            if (World.world != null) {
                if (!World.world.isPaused()) {
                    try {
                        IsRunningManualStep = true; 
                        _mapBoxUpdateMethod.Invoke(MapBox.instance, null);
                        IsRunningManualStep = false; 
                    } catch (System.Exception e) { 
                        IsRunningManualStep = false; 
                        Debug.LogError($"[Lockstep] Game Tick Error: {e.Message}\n{e.StackTrace}");
                    }
                } else {
                    // Debug.Log("[Lockstep] World is Paused"); // Uncomment if needed
                }
            }
            CurrentTick++;
        }

        private void ExecuteAction(string json) {
            // ... (Codice uguale a prima) ...
            // Per brevità non lo ripeto tutto, ma assicurati di copiare la logica POWER dallo script precedente
             try {
                string[] parts = json.Split(':');
                if (parts.Length < 2) return;
                if (parts[0] == "POWER") {
                    string powerID = parts[1];
                    int x = int.Parse(parts[2]); int y = int.Parse(parts[3]);
                    WorldTile tile = World.world.GetTile(x, y);
                    GodPower power = AssetManager.powers.get(powerID);
                    if (power != null && tile != null) {
                        IsRunningManualStep = true;
                        if (power.click_action != null) power.click_action(tile, powerID);
                        else if (!string.IsNullOrEmpty(power.drop_id)) {
                            object dropManager = Traverse.Create(MapBox.instance).Field("drop_manager").GetValue();
                             if (dropManager != null) Traverse.Create(dropManager).Method("spawn", new object[] { tile, power.drop_id, -1f, -1f, -1L }).GetValue();
                        }
                        IsRunningManualStep = false; 
                    }
                }
            } catch {}
        }
    }
}