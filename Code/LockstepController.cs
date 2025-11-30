using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WorldBoxMultiplayer
{
    public class LockstepController : MonoBehaviour
    {
        public static LockstepController Instance;
        public bool IsRunningManualStep = false; 
        public int CurrentTick = 0;
        
        private float _accumulatedTime = 0f;
        private const float MsPerTick = 0.1f; // 10 TPS (Più lento ma stabile per internet)

        private MethodInfo _mapBoxUpdateMethod;
        private bool _initFailed = false;
        
        // Lockstep Sync Variables
        private int _serverTickLimit = 0; // Fino a dove possiamo andare?

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
            // Il server ci dice che è arrivato al tick X. Noi possiamo andare fino a X.
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
                Debug.Log("[WorldBoxMultiplayer] Motore agganciato!");
            }

            _accumulatedTime += Time.deltaTime;

            // Loop: Eseguiamo i tick solo se abbiamo il permesso dal server (o se siamo l'host)
            while (_accumulatedTime >= MsPerTick)
            {
                // Se sono HOST: Comando io. Vado avanti sempre.
                // Se sono CLIENT: Posso andare avanti solo se CurrentTick < _serverTickLimit
                bool canAdvance = NetworkManager.Instance.IsHost() || CurrentTick < _serverTickLimit;

                if (canAdvance)
                {
                    RunGameTick();
                    _accumulatedTime -= MsPerTick;
                    
                    // Se sono HOST, dico al client che ho finito questo tick
                    if (NetworkManager.Instance.IsHost())
                    {
                        NetworkManager.Instance.SendTickSync(CurrentTick);
                    }
                }
                else
                {
                    // Sto aspettando il server... (Lag o desync)
                    // Non sottraggo il tempo, aspetto il prossimo frame
                    break; 
                }
            }
        }

        private void RunGameTick()
        {
            // 1. Esegui Azioni Remote per QUESTO tick
            if (PendingActions.ContainsKey(CurrentTick))
            {
                foreach (var actionJson in PendingActions[CurrentTick])
                {
                    ExecuteAction(actionJson);
                }
                PendingActions.Remove(CurrentTick);
            }

            // 2. Fai avanzare il mondo
            if (MapBox.instance != null && !MapBox.instance.isPaused())
            {
                try 
                {
                    IsRunningManualStep = true; 
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
                        else
                        {
                            World.world.powers.callAction(power, tile);
                        }
                        IsRunningManualStep = false; 
                    }
                }
            }
            catch {}
        }
    }
}