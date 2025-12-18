using HarmonyLib;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

namespace WorldBoxMultiplayer
{
    // Block standard Update to enforce Lockstep
    [HarmonyPatch(typeof(MapBox), "Update")]
    class MapBox_Update_Patch {
        static PowerButton _savedButton;
        static void Prefix() {
            if (LockstepController.Instance != null && LockstepController.Instance.IsRunningManualStep) return;

            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady) {
                // 1. Capture Input
                InputHandler.CheckInput();
                
                // 2. Hide selected power from the game engine so it doesn't execute locally
                if (PowerButtonSelector.instance != null) {
                    _savedButton = PowerButtonSelector.instance.selectedButton;
                    PowerButtonSelector.instance.selectedButton = null;
                }
                
                // 3. Pause Simulation (Camera/UI still works)
                Config.paused = true;
            }
        }
        static void Postfix() {
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady) {
                // Restore button for UI rendering
                if (PowerButtonSelector.instance != null && _savedButton != null) {
                    PowerButtonSelector.instance.selectedButton = _savedButton;
                    _savedButton = null;
                }
            }
        }
    }

    // Fix DeltaTime for deterministic physics
    [HarmonyPatch(typeof(Time), "deltaTime", MethodType.Getter)]
    class Time_DeltaTime_Patch {
        static void Postfix(ref float __result) {
            if (LockstepController.Instance?.IsRunningManualStep == true) 
                __result = LockstepController.Instance.CurrentDeltaTime;
        }
    }

    // Sync Naming
    [HarmonyPatch(typeof(NameInput), "applyInput")]
    class NameInput_Apply_Patch {
        static void Postfix(NameInput __instance) {
            if (NetworkManager.Instance?.IsMultiplayerReady == true) {
                var trav = Traverse.Create(__instance);
                var tActor = trav.Field("target_actor").GetValue<Actor>() ?? trav.Field("actor").GetValue<Actor>();
                var tCity = trav.Field("target_city").GetValue<City>() ?? trav.Field("city").GetValue<City>();
                var tKingdom = trav.Field("target_kingdom").GetValue<Kingdom>() ?? trav.Field("kingdom").GetValue<Kingdom>();

                if (tActor != null) NetworkManager.Instance.SendNameChange("Actor", tActor.id, __instance.inputField.text);
                else if (tCity != null) NetworkManager.Instance.SendNameChange("City", tCity.id, __instance.inputField.text);
                else if (tKingdom != null) NetworkManager.Instance.SendNameChange("Kingdom", tKingdom.id, __instance.inputField.text);
            }
        }
    }

    public static class InputHandler {
        private static float _nextActionTime = 0f;
        // Banned powers that break determinism or are too heavy
        private static HashSet<string> _bannedPowers = new HashSet<string>() { "force_push", "magnet" };
        
        public static void CheckInput() {
            if (Input.GetMouseButton(0) && Time.time >= _nextActionTime) {
                if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) {
                    HandleClick();
                    _nextActionTime = Time.time + 0.05f; // Cap at 20 actions/sec
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
            
            if (tile != null) 
                NetworkManager.Instance.SendAction($"POWER:{power.id}:{tile.x}:{tile.y}");
        }
    }
}