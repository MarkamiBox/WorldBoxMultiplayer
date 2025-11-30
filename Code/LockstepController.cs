using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WorldBoxMultiplayer
{
    public class LockstepController : MonoBehaviour
    {
        public static LockstepController Instance;
        
        // Tempo tra i turni: 0.1s = 10 azioni al secondo (molto più stabile per internet)
        public const float FixedDeltaTime = 0.1f; 
        
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

            // Inizializzazione sicura (evita crash se MapBox non è pronto)
            if (_mapBoxUpdateMethod == null)
            {
                if (World.world == null) return;
                _mapBoxUpdateMethod = AccessTools.Method(typeof(MapBox), "Update");
                if (_mapBoxUpdateMethod == null) { _initFailed = true; return; }
            }

            _accumulatedTime += Time.deltaTime;

            // ANTI-FREEZE: Se accumuliamo troppi tick (lag), limitiamo a 5 per frame per non bloccare il PC
            int loops = 0;
            while (_accumulatedTime >= FixedDeltaTime && loops < 5)
            {
                // Se sono CLIENT, posso andare avanti solo se il server mi ha dato l'OK (_serverTickLimit)
                bool canAdvance = NetworkManager.Instance.IsHost() || CurrentTick < _serverTickLimit;

                if (canAdvance)
                {
                    RunGameTick();
                    _accumulatedTime -= FixedDeltaTime;
                    
                    // Se sono HOST, notifico il progresso
                    if (NetworkManager.Instance.IsHost())
                    {
                        NetworkManager.Instance.SendTickSync(CurrentTick);
                    }
                    loops++;
                }
                else
                {
                    // Waiting for server...
                    break; 
                }
            }
        }

        private void RunGameTick()
        {
            // 1. Esegui i comandi ricevuti per questo turno
            if (PendingActions.ContainsKey(CurrentTick))
            {
                foreach (var actionJson in PendingActions[CurrentTick])
                {
                    ExecuteAction(actionJson);
                }
                PendingActions.Remove(CurrentTick);
            }

            // 2. Esegui il turno di gioco vero e proprio
            if (World.world != null && !World.world.isPaused())
            {
                try 
                {
                    IsRunningManualStep = true; 
                    _mapBoxUpdateMethod.Invoke(World.world, null);
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
                    WorldTile tile = World.world.GetTile(x, y);
                    
                    GodPower power = AssetManager.powers.get(powerID);
                    if (power != null && tile != null) 
                    {
                        IsRunningManualStep = true; // Permette alla patch di far passare l'azione

                        if (power.click_action != null)
                        {
                            power.click_action(tile, powerID);
                        }
                        else if (!string.IsNullOrEmpty(power.drop_id))
                        {
                            // Usa il metodo sicuro per i drop (pioggia, semi, bombe)
                            World.world.drop_manager.spawn(tile, power.drop_id);
                        }
                        
                        IsRunningManualStep = false; 
                    }
                }
            }
            catch {}
        }
    }
}