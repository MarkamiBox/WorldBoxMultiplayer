using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WorldBoxMultiplayer
{
    public class LockstepController : MonoBehaviour
    {
        public static LockstepController Instance;
        
        // 0.05f = 20 Tick al secondo (molto più fluido di 0.1f)
        public const float BaseDeltaTime = 0.05f; 
        public float CurrentDeltaTime = 0.05f;

        public bool IsRunningManualStep = false; 
        public int CurrentTick = 0;
        public bool DesyncDetected = false;
        
        public float _accumulatedTime = 0f;
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
                Debug.LogError($"DESYNC! Tick:{tick}");
            } else DesyncDetected = false;
        }

        private long CalculateWorldHash() {
            if (World.world == null) return 0;
            long hash = 0;
            hash += World.world.units.Count * 1000;
            hash += World.world.cities.Count * 1000000;
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
                bool canAdvance = NetworkManager.Instance.IsHost() || CurrentTick < _serverTickLimit;
                if (canAdvance) {
                    RunGameTick();
                    _accumulatedTime -= BaseDeltaTime;
                    if (CurrentTick % 50 == 0) NetworkManager.Instance.SendHash(CurrentTick, CalculateWorldHash());
                    if (NetworkManager.Instance.IsHost()) NetworkManager.Instance.SendTickSync(CurrentTick);
                    loops++;
                } else break; 
            }
        }

        private void RunGameTick() {
            // Esegui Azioni (Inclusi quelli passati che potrebbero essere arrivati in ritardo)
            List<int> ticksToRemove = new List<int>();
            foreach (var kvp in PendingActions) {
                if (kvp.Key <= CurrentTick) {
                    foreach (var actionJson in kvp.Value) ExecuteAction(actionJson);
                    ticksToRemove.Add(kvp.Key);
                }
            }
            foreach (int t in ticksToRemove) PendingActions.Remove(t);

            // Fai avanzare il mondo (SIMULAZIONE)
            if (World.world != null) {
                try {
                    // IL TRUCCO: Togliamo momentaneamente la pausa per far muovere le unità
                    Config.paused = false;
                    IsRunningManualStep = true; 
                    
                    // Chiamiamo Update (che ora vedrà paused=false e muoverà le cose)
                    _mapBoxUpdateMethod.Invoke(MapBox.instance, null);
                    
                    IsRunningManualStep = false; 
                    Config.paused = true; // Rimettiamo subito la pausa
                } catch { IsRunningManualStep = false; Config.paused = true; }
            }
            CurrentTick++;
        }

        private void ExecuteAction(string json) {
            try {
                // Debug.Log($"[Lockstep] Executing: {json}");
                string[] parts = json.Split(':');
                if (parts.Length < 2) return;
                if (parts[0] == "POWER") {
                    string powerID = parts[1];
                    int x = int.Parse(parts[2]); int y = int.Parse(parts[3]);
                    WorldTile tile = World.world.GetTile(x, y);
                    GodPower power = AssetManager.powers.get(powerID);
                    if (power != null && tile != null) {
                        Debug.Log($"[Lockstep] Spawning Power: {powerID} at {x},{y}");
                        IsRunningManualStep = true;
                        if (power.click_action != null) power.click_action(tile, powerID);
                        else if (!string.IsNullOrEmpty(power.drop_id)) {
                            object dropManager = Traverse.Create(MapBox.instance).Field("drop_manager").GetValue();
                             if (dropManager != null) Traverse.Create(dropManager).Method("spawn", new object[] { tile, power.drop_id, -1f, -1f, -1L }).GetValue();
                        }
                        IsRunningManualStep = false; 
                    }
                }
            } catch (System.Exception e) { Debug.LogError($"[Lockstep] Action Error: {e.Message}"); }
        }
    }
}