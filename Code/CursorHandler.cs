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
            
            _remoteCursorObj.transform.localScale = new Vector3(3f, 3f, 1f);
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

            // Check current power
            string currentPower = "god_finger";
            if (PowerButtonSelector.instance != null && PowerButtonSelector.instance.selectedButton != null) {
                var godPower = Traverse.Create(PowerButtonSelector.instance.selectedButton).Field("godPower").GetValue<GodPower>();
                if (godPower != null) currentPower = godPower.id;
            }
            
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
            _targetPos = new Vector3(x, y, 0f); // Z=0 is safer for 2D 
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
                _cursorRenderer.color = Color.white; 
            }
            else {
                // Fallback: Create a simple white square
                if (_cursorRenderer.sprite == null) {
                    Texture2D tex = new Texture2D(64, 64);
                    Color[] colors = new Color[64 * 64];
                    for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
                    tex.SetPixels(colors);
                    tex.Apply();
                    _cursorRenderer.sprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
                    _cursorRenderer.color = Color.magenta; // Make it visible
                }
            }
        }
    }
}