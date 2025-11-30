using HarmonyLib;
using UnityEngine;
using System.Reflection;

namespace WorldBoxMultiplayer
{
    [HarmonyPatch(typeof(MapBox), "Update")]
    class MapBox_Update_Patch
    {
        static bool Prefix()
        {
            // 1. Turno Manuale del Lockstep -> Lascia passare
            if (LockstepController.Instance != null && LockstepController.Instance.IsRunningManualStep)
            {
                return true; 
            }

            // 2. Multiplayer Attivo -> Gestisci Input e Blocca Unity
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady)
            {
                // Gestiamo l'input qui invece di patchare bottoni specifici
                InputHandler.CheckInput();
                return false; 
            }

            return true;
        }
    }

    public static class InputHandler
    {
        private static float _nextActionTime = 0f;
        private const float ACTION_INTERVAL = 0.05f; 

        public static void CheckInput()
        {
            // Se il mouse è premuto e stiamo puntando la mappa
            if (Input.GetMouseButton(0) && Time.time >= _nextActionTime)
            {
                if (!IsMouseOverUI())
                {
                    HandleClick();
                    _nextActionTime = Time.time + ACTION_INTERVAL;
                }
            }
        }

        static bool IsMouseOverUI()
        {
            // Controllo basico se siamo sopra la UI (opzionale, ma utile)
            return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        }

        static void HandleClick()
        {
            var selector = PowerButtonSelector.instance;
            if (selector == null || selector.selectedButton == null) return;

            // Reflection per ottenere il potere selezionato in modo sicuro
            GodPower power = Traverse.Create(selector.selectedButton).Field("godPower").GetValue<GodPower>();
            if (power == null) return;

            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            WorldTile tile = MapBox.instance.GetTile((int)mousePos.x, (int)mousePos.y);

            if (tile != null)
            {
                string packet = $"POWER:{power.id}:{tile.x}:{tile.y}";
                NetworkManager.Instance.SendAction(packet);
            }
        }
    }
}