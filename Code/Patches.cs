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
                // Gestiamo l'input continuo (DRAG) qui
                InputHandler.CheckInput();
                return false; 
            }
            return true;
        }
    }

    // Questa classe gestisce l'input del mouse col "rate limiting"
    public static class InputHandler
    {
        private static float _nextActionTime = 0f;
        private const float ACTION_INTERVAL = 0.05f; // 20 volte al secondo (buono per disegnare/spawnare)

        public static void CheckInput()
        {
            // Se teniamo premuto il mouse E è passato abbastanza tempo
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

    // Rimuoviamo le patch sui singoli bottoni/poteri perché ora gestiamo tutto dall'Update centrale
    // Questo evita errori "Method not found" e conflitti
}