using HarmonyLib;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

namespace WorldBoxMultiplayer
{
    // Patch 1: Gestione Loop di Gioco (Camera + Input)
    [HarmonyPatch(typeof(MapBox), "Update")]
    class MapBox_Update_Patch {
        
        static PowerButton _savedButton;

        static void Prefix() {
            // Se è un turno manuale del Lockstep, lascia fare tutto al gioco
            if (LockstepController.Instance != null && LockstepController.Instance.IsRunningManualStep)
                return;

            // Se siamo in Multiplayer...
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady)
            {
                // 1. Leggi l'Input e manda i pacchetti (mentre il bottone è ancora lì)
                InputHandler.CheckInput();

                // 2. TRUCCO GENIALE: Nascondiamo il potere selezionato al gioco!
                // Così MapBox.Update() girerà (muovendo la camera), ma se clicchi non spawnerà nulla.
                if (PowerButtonSelector.instance != null)
                {
                    _savedButton = PowerButtonSelector.instance.selectedButton;
                    PowerButtonSelector.instance.selectedButton = null;
                }

                // 3. Metti in pausa la simulazione (così le unità non si muovono da sole)
                Config.paused = true;
            }
        }

        static void Postfix() {
            // Rimettiamo il bottone al suo posto subito dopo l'Update
            // Così l'interfaccia grafica lo vede ancora selezionato
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady)
            {
                if (PowerButtonSelector.instance != null && _savedButton != null)
                {
                    PowerButtonSelector.instance.selectedButton = _savedButton;
                    _savedButton = null;
                }
            }
        }
    }

    // Patch 2: Tempo Fisso (Anti-Lag)
    [HarmonyPatch(typeof(Time), "deltaTime", MethodType.Getter)]
    class Time_DeltaTime_Patch {
        static void Postfix(ref float __result) {
            if (LockstepController.Instance?.IsRunningManualStep == true) 
                __result = LockstepController.Instance.CurrentDeltaTime;
        }
    }

    public static class InputHandler {
        private static float _nextActionTime = 0f;
        // Lista poteri vietati (Fisica non deterministica)
        private static HashSet<string> _bannedPowers = new HashSet<string>() { "god_finger", "force_push", "heat_ray", "tornado", "magnet" };
        
        public static void CheckInput() {
            if (Input.GetMouseButton(0) && Time.time >= _nextActionTime) {
                if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) {
                    HandleClick();
                    _nextActionTime = Time.time + 0.05f; // 20 click/sec
                }
            }
        }
        
        static void HandleClick() {
            var selector = PowerButtonSelector.instance;
            if (selector == null || selector.selectedButton == null) return;
            
            GodPower power = Traverse.Create(selector.selectedButton).Field("godPower").GetValue<GodPower>();
            if (power == null || _bannedPowers.Contains(power.id)) return;
            
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            WorldTile tile = MapBox.instance.GetTile((int)mousePos.x, (int)mousePos.y);
            
            if (tile != null) {
                string packet = $"POWER:{power.id}:{tile.x}:{tile.y}";
                Debug.Log($"[InputHandler] Sending Action: {packet}");
                NetworkManager.Instance.SendAction(packet);
            }
        }
    }
}