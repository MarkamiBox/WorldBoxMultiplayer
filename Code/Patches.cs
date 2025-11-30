using HarmonyLib;
using UnityEngine;
using System.Reflection;

namespace WorldBoxMultiplayer
{
    // Patch 1: Blocca il motore di gioco originale
    [HarmonyPatch(typeof(MapBox), "Update")]
    class MapBox_Update_Patch
    {
        static bool Prefix()
        {
            // Se è il LockstepController a chiamare, lasciamo passare
            if (LockstepController.Instance != null && LockstepController.Instance.IsRunningManualStep)
                return true; 
            
            // Se siamo in Multiplayer, blocchiamo l'update automatico di Unity
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady)
            {
                InputHandler.CheckInput();
                return false; 
            }
            return true;
        }
    }

    // Patch 2: Intercetta l'uso dei poteri
    [HarmonyPatch(typeof(PowerLibrary), "checkPower")]
    class PowerLibrary_CheckPower_Patch
    {
        static bool Prefix(string pPowerID)
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsMultiplayerReady)
                return true;

            if (LockstepController.Instance != null && LockstepController.Instance.IsRunningManualStep)
                return true;

            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            WorldTile tile = MapBox.instance.GetTile((int)mousePos.x, (int)mousePos.y);

            if (tile != null)
            {
                // Inviamo il pacchetto al server
                string packet = $"POWER:{pPowerID}:{tile.x}:{tile.y}";
                NetworkManager.Instance.SendAction(packet);
            }
            return false; // Blocca esecuzione locale immediata
        }
    }

    // PATCH 3 (NUOVA): FORZA IL TEMPO FISSO
    // Questo costringe WorldBox a calcolare la fisica sempre allo stesso modo
    [HarmonyPatch(typeof(Time), "deltaTime", MethodType.Getter)]
    class Time_DeltaTime_Patch
    {
        static void Postfix(ref float __result)
        {
            // Se il nostro controller sta eseguendo un passo manuale,
            // mentiamo al gioco dicendo che è passato un tempo fisso (0.05s)
            if (LockstepController.Instance != null && LockstepController.Instance.IsRunningManualStep)
            {
                __result = LockstepController.FixedDeltaTime;
            }
        }
    }

    public static class InputHandler
    {
        private static float _nextActionTime = 0f;
        private const float ACTION_INTERVAL = 0.05f; // 20 input al secondo (fluido per disegnare)

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
                // Chiamiamo direttamente la patch per validare
                string packet = $"POWER:{power.id}:{tile.x}:{tile.y}";
                NetworkManager.Instance.SendAction(packet);
            }
        }
    }
}