using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WorldBoxMultiplayer
{
    public class LockstepController : MonoBehaviour
    {
        public static LockstepController Instance;
        public const float BaseDeltaTime = 0.05f; // 20 TPS
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
        public void AddPendingAction(int tick, string action) { if (!PendingActions.ContainsKey(tick)) PendingActions[tick] = new List<string>(); PendingActions[tick].Add(action); }
        public void SetServerTick(int tick) { _serverTickLimit = tick; }
        
        public void UpdateTimeScale() { 
            float speedVal = 1f; 
            try { var field = Traverse.Create(Config.time_scale_asset).Field("speed_val"); if (field.FieldExists()) speedVal = (float)field.GetValue<int>(); } catch {} 
            CurrentDeltaTime = BaseDeltaTime * speedVal; 
        }

        public void CheckRemoteHash(int tick, long remoteHash) { 
            long localHash = CalculateWorldHash(); 
            if (localHash != remoteHash) { 
                DesyncDetected = true; 
                // We rely on Auto-Sync to fix this
            } else DesyncDetected = false; 
        }

        private long CalculateWorldHash() { 
            if (World.world == null) return 0; 
            long hash = 0; 
            hash += World.world.units.Count * 1000; 
            hash += World.world.cities.Count * 1000000; 
            // Simple checksum: check first unit health
            if (World.world.units.Count > 0) { 
                var list = Traverse.Create(World.world.units).Field("simpleList").GetValue<List<Actor>>(); 
                if (list != null && list.Count > 0 && list[0] != null) hash += list[0].data.health; 
            } 
            return hash; 
        }

        public void NetworkUpdate() {
            if (!NetworkManager.Instance.IsConnected || _initFailed || !NetworkManager.Instance.IsMapLoaded) return;
            
            if (_mapBoxUpdateMethod == null) { 
                if (World.world == null) return; 
                _mapBoxUpdateMethod = AccessTools.Method(typeof(MapBox), "Update"); 
                if (_mapBoxUpdateMethod == null) { _initFailed = true; return; } 
                UpdateTimeScale(); 
            }

            _accumulatedTime += Time.deltaTime;
            int loops = 0;
            while (_accumulatedTime >= BaseDeltaTime && loops < 5) {
                // Host dictates the pace, Client follows server tick limit
                bool canAdvance = NetworkManager.Instance.IsHost() || CurrentTick < _serverTickLimit;
                
                if (canAdvance) { 
                    RunGameTick(); 
                    _accumulatedTime -= BaseDeltaTime; 
                    
                    // Periodic Hash Check (Every 50 ticks)
                    if (CurrentTick % 50 == 0) NetworkManager.Instance.SendHash(CurrentTick, CalculateWorldHash());
                    if (NetworkManager.Instance.IsHost()) NetworkManager.Instance.SendTickSync(CurrentTick);
                    
                    loops++; 
                } else break; 
            }
        }

        private void RunGameTick() {
            // Execute scheduled actions
            if (PendingActions.ContainsKey(CurrentTick)) { 
                foreach (var actionJson in PendingActions[CurrentTick]) ExecuteAction(actionJson); 
                PendingActions.Remove(CurrentTick); 
            }

            if (World.world != null) {
                try {
                    Config.paused = false; // UNPAUSE
                    IsRunningManualStep = true; 
                    _mapBoxUpdateMethod.Invoke(World.world, null); // FORCE STEP
                    IsRunningManualStep = false; 
                    Config.paused = true; // PAUSE
                } catch { IsRunningManualStep = false; Config.paused = true; }
            }
            CurrentTick++;
        }

        private void ExecuteAction(string json) {
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
                        // Execute click or spawn
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