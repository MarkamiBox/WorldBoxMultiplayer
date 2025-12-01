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
        private Vector3 _lastSentPos;
        private string _lastPowerID;

        void Awake() { Instance = this; }

        void Start()
        {
            _remoteCursorObj = new GameObject("RemoteCursor");
            _remoteCursorObj.transform.SetParent(this.transform);
            _cursorRenderer = _remoteCursorObj.AddComponent<SpriteRenderer>();
            
            SetRemotePower("god_finger");
            
            // Distinct MAGENTA color so you know it's the other player
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

            // Send my cursor position
            if (Time.time - _lastSendTime > 0.05f)
            {
                if (Camera.main != null) {
                    Vector3 pos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                    if (Vector3.Distance(pos, _lastSentPos) > 0.1f) 
                    {
                        NetworkManager.Instance.SendCursorPos(pos.x, pos.y);
                        _lastSentPos = pos;
                        _lastSendTime = Time.time;
                    }
                }
            }

            // Check current power (Simplified for now)
            string currentPower = "god_finger"; 
            // TODO: Find correct API for selected power. 
            // For now, default to finger so cursor is visible.
            
            if (currentPower != _lastPowerID)
            {
                _lastPowerID = currentPower;
                NetworkManager.Instance.SendPowerSelection(currentPower);
            }

            // Interpolate remote cursor
            if (_remoteCursorObj.activeSelf)
                _remoteCursorObj.transform.position = Vector3.Lerp(_remoteCursorObj.transform.position, _targetPos, Time.deltaTime * 20f);
        }

        public void UpdateRemoteCursor(float x, float y)
        {
            if (!_remoteCursorObj.activeSelf) _remoteCursorObj.SetActive(true);
            _targetPos = new Vector3(x, y, -10f); 
        }

        public void SetRemotePower(string powerID)
        {
            Sprite icon = null;
            GodPower power = AssetManager.powers.get(powerID);
            
            if (power != null) icon = power.getIconSprite();
            
            if (icon == null) 
            {
                GodPower finger = AssetManager.powers.get("god_finger");
                if (finger != null) icon = finger.getIconSprite();
            }

            if (icon != null) {
                _cursorRenderer.sprite = icon;
                _cursorRenderer.color = Color.white; // Reset color if we have a sprite
            }
            else {
                // Fallback if absolutely no sprites found
                Debug.LogWarning($"[Cursor] Could not load sprite for {powerID} or god_finger");
                // Create a simple square texture if needed, or just keep it magenta
                _cursorRenderer.sprite = null; 
            }
        }
    }
}