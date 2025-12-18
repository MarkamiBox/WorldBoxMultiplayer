using UnityEngine;
using HarmonyLib;

namespace WorldBoxMultiplayer
{
    public class CursorHandler : MonoBehaviour
    {
        public static CursorHandler Instance;
        
        private GameObject _remoteCursorObj;
        private SpriteRenderer _cursorRenderer;
        private Vector3 _targetPos;
        
        private float _lastSendTime;
        private string _lastPowerID;

        void Awake() { Instance = this; }

        void Start()
        {
            // Crea il cursore remoto
            _remoteCursorObj = new GameObject("RemoteCursor");
            _remoteCursorObj.transform.SetParent(this.transform);
            _cursorRenderer = _remoteCursorObj.AddComponent<SpriteRenderer>();
            
            SetRemotePower("god_finger");
            
            // Colore MAGENTA per distinguere il cursore avversario
            _cursorRenderer.color = new Color(1f, 0f, 1f, 1f); 
            _cursorRenderer.sortingLayerName = "EffectsTop"; 
            _cursorRenderer.sortingOrder = 9999;
            
            _remoteCursorObj.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
            _remoteCursorObj.SetActive(false);
        }

        void Update()
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsMultiplayerReady) 
            {
                if (_remoteCursorObj.activeSelf) _remoteCursorObj.SetActive(false);
                return;
            }

            // 1. Invia la mia posizione (Limitato a 20 volte al secondo per salvare banda)
            if (Time.time - _lastSendTime > 0.05f) 
            {
                _lastSendTime = Time.time;
                Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                NetworkManager.Instance.SendCursorPos(mousePos.x, mousePos.y);
            }

            // 2. Invia il potere selezionato (solo se cambia)
            string currentPower = "god_finger";
            if (PowerButtonSelector.instance != null && PowerButtonSelector.instance.selectedButton != null)
            {
                GodPower power = Traverse.Create(PowerButtonSelector.instance.selectedButton).Field("godPower").GetValue<GodPower>();
                if (power != null) currentPower = power.id;
            }

            if (currentPower != _lastPowerID)
            {
                _lastPowerID = currentPower;
                NetworkManager.Instance.SendPowerSelection(currentPower);
            }

            // 3. Interpolazione visuale (muove il cursore fluido verso la destinazione)
            if (_remoteCursorObj.activeSelf)
                _remoteCursorObj.transform.position = Vector3.Lerp(_remoteCursorObj.transform.position, _targetPos, Time.deltaTime * 15f);
        }

        public void UpdateRemoteCursor(float x, float y)
        {
            if (!_remoteCursorObj.activeSelf) _remoteCursorObj.SetActive(true);
            _targetPos = new Vector3(x, y, -10f); // Z -10 per stare sopra tutto
        }

        public void SetRemotePower(string powerID)
        {
            Sprite icon = null;
            GodPower power = AssetManager.powers.get(powerID);
            
            if (power != null) icon = power.getIconSprite();
            
            // Fallback se l'icona è nulla
            if (icon == null) 
            {
                GodPower finger = AssetManager.powers.get("god_finger");
                if (finger != null) icon = finger.getIconSprite();
            }

            if (icon != null) {
                _cursorRenderer.sprite = icon;
                // Resetta il colore se è un'icona vera, altrimenti lascia magenta
                _cursorRenderer.color = Color.white; 
            }
        }
    }
}