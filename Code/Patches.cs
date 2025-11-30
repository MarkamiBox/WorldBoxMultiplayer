using HarmonyLib;
using UnityEngine;
using System.Reflection;

namespace WorldBoxMultiplayer
{
    // Patch unica e sicura: Blocca il loop principale
    [HarmonyPatch(typeof(MapBox), "Update")]
    class MapBox_Update_Patch
    {
        static bool Prefix()
        {
            // Se è il LockstepController che sta simulando il turno, lasciamo passare
            if (LockstepController.Instance != null && LockstepController.Instance.IsRunningManualStep)
            {
                return true; 
            }

            // Se siamo in multiplayer, gestiamo noi l'input e blocchiamo Unity
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady)
            {
                InputHandler.CheckInput();
                return false; // Blocca l'aggiornamento normale
            }

            return true;
        }
    }

    // Gestore Input Ottimizzato
    public static class InputHandler
    {
        private static float _nextActionTime = 0f;
        // 0.05s = 20 azioni al secondo (ottimo per drag & drop fluido)
        private const float ACTION_INTERVAL = 0.05f; 

        public static void CheckInput()
        {
            // Se il mouse è premuto (Hold) e il timer è scaduto
            if (Input.GetMouseButton(0) && Time.time >= _nextActionTime)
            {
                // Evita di cliccare attraverso le finestre UI
                if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                {
                    HandleClick();
                    _nextActionTime = Time.time + ACTION_INTERVAL;
                }
            }
        }

        static void HandleClick()
        {
            var selector = PowerButtonSelector.instance;
            if (selector == null || selector.selectedButton == null) return;

            // Ottiene il potere selezionato in modo sicuro
            GodPower power = selector.selectedButton.godPower;
            if (power == null) return;

            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            WorldTile tile = World.world.GetTile((int)mousePos.x, (int)mousePos.y);

            if (tile != null)
            {
                // Invia il comando alla rete
                string packet = $"POWER:{power.id}:{tile.x}:{tile.y}";
                NetworkManager.Instance.SendAction(packet);
            }
        }
    }
}