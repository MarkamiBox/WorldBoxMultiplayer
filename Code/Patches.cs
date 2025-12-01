using HarmonyLib;
using UnityEngine;
using System.Reflection;

namespace WorldBoxMultiplayer
{
    // Patch 1: Intercetta il cambio di velocità
    [HarmonyPatch(typeof(Config), "setWorldSpeed")]
    class Config_SetWorldSpeed_Patch
    {
        static void Postfix(string pID)
        {
            // Se cambiamo velocità e siamo online, avvisiamo l'altro
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady)
            {
                // Evitiamo loop infiniti controllando se è stato il Lockstep a cambiarla
                if (!LockstepController.Instance.IsRunningManualStep)
                {
                    NetworkManager.Instance.SendSpeedChange(pID);
                }
            }
        }
    }

    // Patch 3: Delta Time Fisso (Adattivo alla velocità)
    [HarmonyPatch(typeof(Time), "deltaTime", MethodType.Getter)]
    class Time_DeltaTime_Patch
    {
        static void Postfix(ref float __result)
        {
            if (LockstepController.Instance != null && LockstepController.Instance.IsRunningManualStep)
            {
                // Usiamo il deltaTime calcolato dal controller (che include la velocità 1x, 5x etc)
                __result = LockstepController.Instance.CurrentDeltaTime;
            }
        }
    }

    // Patch 4: Blocca Update
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
                return false; 
            }
            return true;
        }
    }

    // Patch 5: Blocca Poteri locali
    [HarmonyPatch(typeof(PowerLibrary), "checkPower")]
    class PowerLibrary_CheckPower_Patch
    {
        static bool Prefix()
        {
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady)
            {
                if (LockstepController.Instance != null && LockstepController.Instance.IsRunningManualStep)
                    return true;
                return false;
            }
            return true;
        }
    }

    public static class InputHandler
    {
        private static float _nextActionTime = 0f;
        private const float ACTION_INTERVAL = 0.03f; 

        public static void CheckInput()
        {
            if (Input.GetMouseButton(0) && Time.time >= _nextActionTime)
            {
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