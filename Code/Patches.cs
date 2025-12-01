using HarmonyLib;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

namespace WorldBoxMultiplayer
{
    [HarmonyPatch(typeof(MapBox), "Update")]
    class MapBox_Update_Patch {
        static bool Prefix() {
            if (LockstepController.Instance == null) return true;
            if (LockstepController.Instance.IsRunningManualStep) return true; 
            
            // If multiplayer is ready, we block the normal update and let LockstepController handle it
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady) { 
                InputHandler.CheckInput(); 
                return false; 
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Time), "deltaTime", MethodType.Getter)]
    class Time_DeltaTime_Patch {
        static void Postfix(ref float __result) {
            if (LockstepController.Instance?.IsRunningManualStep == true) __result = LockstepController.Instance.CurrentDeltaTime;
        }
    }

    public static class InputHandler {
        private static float _nextActionTime = 0f;
        private static HashSet<string> _bannedPowers = new HashSet<string>() { "god_finger", "force_push", "heat_ray", "tornado", "magnet" };
        
        public static void CheckInput() {
            if (Input.GetMouseButton(0) && Time.time >= _nextActionTime) {
                if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) {
                    HandleClick();
                    _nextActionTime = Time.time + 0.03f;
                }
            }
        }
        
        static void HandleClick() {
            try {
                if (PowerButtonSelector.instance == null || PowerButtonSelector.instance.selectedButton == null) return;
                
                GodPower power = Traverse.Create(PowerButtonSelector.instance.selectedButton).Field("godPower").GetValue<GodPower>();
                if (power == null || _bannedPowers.Contains(power.id)) return;
                
                if (Camera.main == null) return;
                Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                if (MapBox.instance == null) return;
                WorldTile tile = MapBox.instance.GetTile((int)mousePos.x, (int)mousePos.y);
                
                if (tile != null) 
                    NetworkManager.Instance.SendAction($"POWER:{power.id}:{tile.x}:{tile.y}");
            } catch (System.Exception e) {
                Debug.LogWarning($"[Input] Error handling click: {e.Message}");
            }
        }
    }
}