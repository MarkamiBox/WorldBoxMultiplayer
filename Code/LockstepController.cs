using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WorldBoxMultiplayer
{
    public class LockstepController : MonoBehaviour
    {
        public static LockstepController Instance;
        
        // COSTANTE TEMPO FISSO: Tutti i PC simuleranno 0.05 secondi per ogni Tick.
        // Questo elimina il problema della velocità diversa tra PC potenti e lenti.
        public const float FixedDeltaTime = 0.05f; 
        
        public bool IsRunningManualStep = false; 
        public int CurrentTick = 0;
        
        private float _accumulatedTime = 0f;
        private MethodInfo _mapBoxUpdateMethod;
        private bool _initFailed = false;
        
        private int _serverTickLimit = 0; 

        public Dictionary<int, List<string>> PendingActions = new Dictionary<int, List<string>>();

        void Awake()
        {
            Instance = this;
        }

        public void AddPendingAction(int tick, string action)
        {
            if (!PendingActions.ContainsKey(tick))
                PendingActions[tick] = new List<string>();
            PendingActions[tick].Add(action);
        }

        public void SetServerTick(int tick)
        {
            _serverTickLimit = tick;
        }

        public void NetworkUpdate()
        {
            if (!NetworkManager.Instance.IsConnected || _initFailed) return;

            if (_mapBoxUpdateMethod == null)
            {
                if (MapBox.instance == null) return;
                _mapBoxUpdateMethod = AccessTools.Method(typeof(MapBox), "Update");
                if (_mapBoxUpdateMethod == null)
                {
                    _initFailed = true;
                    NetworkManager.Instance.Disconnect();
                    return;
                }
            }

            _accumulatedTime += Time.deltaTime;

            // SICUREZZA: Non eseguire più di 5 tick per frame per evitare freeze se si lagga troppo
            int loops = 0;
            while (_accumulatedTime >= FixedDeltaTime && loops < 5)
            {
                bool canAdvance = NetworkManager.Instance.IsHost() || CurrentTick < _serverTickLimit;

                if (canAdvance)
                {
                    RunGameTick();
                    _accumulatedTime -= FixedDeltaTime;
                    
                    if (NetworkManager.Instance.IsHost())
                    {
                        NetworkManager.Instance.SendTickSync(CurrentTick);
                    }
                    loops++;
                }
                else
                {
                    break; 
                }
            }
        }

        private void RunGameTick()
        {
            if (PendingActions.ContainsKey(CurrentTick))
            {
                foreach (var actionJson in PendingActions[CurrentTick])
                {
                    ExecuteAction(actionJson);
                }
                PendingActions.Remove(CurrentTick);
            }

            if (MapBox.instance != null && !MapBox.instance.isPaused())
            {
                try 
                {
                    IsRunningManualStep = true; 
                    // Qui dentro ora Time.deltaTime varrà sempre 0.05f grazie alla patch!
                    _mapBoxUpdateMethod.Invoke(MapBox.instance, null);
                    IsRunningManualStep = false; 
                }
                catch { IsRunningManualStep = false; }
            }

            CurrentTick++;
        }

        private void ExecuteAction(string json)
        {
            try 
            {
                string[] parts = json.Split(':');
                if (parts.Length < 2) return;

                if (parts[0] == "POWER")
                {
                    string powerID = parts[1];
                    int x = int.Parse(parts[2]);
                    int y = int.Parse(parts[3]);
                    WorldTile tile = MapBox.instance.GetTile(x, y);
                    
                    GodPower power = AssetManager.powers.get(powerID);
                    if (power != null && tile != null) 
                    {
                        IsRunningManualStep = true;

                        if (power.click_action != null)
                        {
                            power.click_action(tile, powerID);
                        }
                        else if (!string.IsNullOrEmpty(power.drop_id))
                        {
                            object dropManager = Traverse.Create(MapBox.instance).Field("drop_manager").GetValue();
                            if (dropManager != null)
                            {
                                Traverse.Create(dropManager).Method("spawn", new object[] { tile, power.drop_id, -1f, -1f, -1L }).GetValue();
                            }
                        }
                        IsRunningManualStep = false; 
                    }
                }
            }
            catch {}
        }
    }
}