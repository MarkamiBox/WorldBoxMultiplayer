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
        private const float MsPerTick = 0.05f; 

        private MethodInfo _mapBoxUpdateMethod;
        private bool _initFailed = false;

        public Dictionary<int, List<string>> PendingActions = new Dictionary<int, List<string>>();

        void Awake()
        {
            Instance = this;
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
            while (_accumulatedTime >= MsPerTick)
            {
                RunGameTick();
                _accumulatedTime -= MsPerTick;
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

                        // Logica intelligente per eseguire il potere
                        if (power.click_action != null)
                        {
                            // Poteri standard (Fulmine, Spawn Unit)
                            power.click_action(tile, powerID);
                        }
                        else if (!string.IsNullOrEmpty(power.drop_id))
                        {
                            // Drop (Pioggia, Semi) - Usiamo Reflection per trovare il drop_manager
                            // così evitiamo l'errore CS1061 "Definition not found"
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
            catch (System.Exception e) { 
                IsRunningManualStep = false; 
                Debug.LogWarning("Err Exec: " + e.Message); 
            }
        }
    }
}