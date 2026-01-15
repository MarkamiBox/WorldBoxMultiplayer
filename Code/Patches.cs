using HarmonyLib;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

namespace WorldBoxMultiplayer
{
    [HarmonyPatch(typeof(MapBox), "Update")]
    class MapBox_Update_Patch
    {
        static bool Prefix()
        {
            if (ClientController.Instance != null && ClientController.Instance.IsClientMode)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ActorManager), "update")]
    class ActorManager_Update_Patch
    {
        static bool Prefix()
        {
            if (ClientController.Instance != null && ClientController.Instance.IsClientMode)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(CityManager), "update")]
    class CityManager_Update_Patch
    {
        static bool Prefix()
        {
            if (ClientController.Instance != null && ClientController.Instance.IsClientMode)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(KingdomManager), "update")]
    class KingdomManager_Update_Patch
    {
        static bool Prefix()
        {
            if (ClientController.Instance != null && ClientController.Instance.IsClientMode)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(NameInput), "applyInput")]
    class NameInput_Apply_Patch
    {
        static void Postfix(NameInput __instance)
        {
            if (NetworkManager.Instance?.IsMultiplayerReady == true)
            {
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

    public static class InputHandler
    {
        private static float _nextActionTime = 0f;
        private static HashSet<string> _bannedPowers = new HashSet<string>() { "force_push", "magnet" };

        public static void CheckInput()
        {
            if (!NetworkManager.Instance.IsMultiplayerReady) return;
            
            if (Input.GetMouseButton(0) && Time.time >= _nextActionTime)
            {
                if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                {
                    HandleClick();
                    _nextActionTime = Time.time + 0.05f;
                }
            }
        }

        static void HandleClick()
        {
            var selector = PowerButtonSelector.instance;
            if (selector == null || selector.selectedButton == null) return;

            GodPower power = Traverse.Create(selector.selectedButton).Field("godPower").GetValue<GodPower>();
            if (power == null || _bannedPowers.Contains(power.id)) return;

            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            int x = (int)mousePos.x;
            int y = (int)mousePos.y;
            
            if (ClientController.Instance != null && ClientController.Instance.IsClientMode)
            {
                ClientController.Instance.SendInputAction(power.id, x, y);
            }
            else if (NetworkManager.Instance.IsHost())
            {
                WorldTile tile = MapBox.instance.GetTile(x, y);
                if (tile != null)
                {
                    power.click_action?.Invoke(tile, power.id);
                }
            }
        }
    }
}