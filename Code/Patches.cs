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
            // 1. Se è il LockstepController che sta eseguendo un turno (ricevuto dalla rete),
            // LASCIAMO PASSARE l'aggiornamento normale.
            if (LockstepController.Instance != null && LockstepController.Instance.IsRunningManualStep)
            {
                return true; 
            }

            // 2. Se siamo in Multiplayer (e non è il turno manuale), INTERCETTIAMO L'INPUT
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsMultiplayerReady)
            {
                // Controlliamo se il giocatore clicca il tasto sinistro
                if (Input.GetMouseButtonDown(0))
                {
                    HandleClick();
                }

                // Blocchiamo l'aggiornamento normale di Unity per mantenere il gioco in pausa/sync
                return false; 
            }

            // 3. Offline: tutto normale
            return true;
        }

        static void HandleClick()
        {
            // Troviamo quale potere è selezionato
            var selector = PowerButtonSelector.instance;
            if (selector == null || selector.selectedButton == null) return;

            // Usiamo Reflection per prendere 'godPower' in modo sicuro (per evitare errori se il nome cambia)
            // Di solito è .godPower o .asset
            GodPower power = Traverse.Create(selector.selectedButton).Field("godPower").GetValue<GodPower>();
            
            if (power == null) return;

            // Troviamo dove abbiamo cliccato nel mondo
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            WorldTile tile = MapBox.instance.GetTile((int)mousePos.x, (int)mousePos.y);

            if (tile != null)
            {
                // Inviamo il pacchetto!
                string packet = $"POWER:{power.id}:{tile.x}:{tile.y}";
                NetworkManager.Instance.SendAction(packet);
                Debug.Log($"[Multiplayer] Input inviato: {power.id}");
            }
        }
    }
}