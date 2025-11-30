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
            if (LockstepController.Instance != null && LockstepController.Instance.IsRunningManualStep)
                return true; 
            
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady)
            {
                InputHandler.CheckInput();
                return false; // Blocca Unity
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PowerLibrary), "checkPower")]
    class PowerLibrary_CheckPower_Patch
    {
        static bool Prefix()
        {
            // Blocca l'uso locale dei poteri se siamo in multiplayer e non è un turno lockstep
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady)
            {
                if (LockstepController.Instance != null && LockstepController.Instance.IsRunningManualStep)
                    return true; // Lascia passare
                return false; // Blocca
            }
            return true;
        }
    }

    public static class InputHandler
    {
        private static float _nextActionTime = 0f;
        // 0.05s = 20 click al secondo (perfetto per trascinare)
        private const float ACTION_INTERVAL = 0.05f; 

        public static void CheckInput()
        {
            if (Input.GetMouseButton(0) && Time.time >= _nextActionTime)
            {
                HandleClick();
                _nextActionTime = Time.time + ACTION_INTERVAL;
            }
        }

        static void HandleClick()
        {
            var selector = PowerButtonSelector.instance;
            if (selector == null || selector.selectedButton == null) return;

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