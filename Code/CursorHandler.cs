using UnityEngine;

namespace WorldBoxMultiplayer
{
    public class CursorHandler : MonoBehaviour
    {
        public static CursorHandler Instance;
        private GameObject _remoteCursorObj;
        private Vector3 _targetPos;
        private float _lastSendTime;

        void Awake() { Instance = this; }

        void Start()
        {
            _remoteCursorObj = new GameObject("RemoteCursor");
            _remoteCursorObj.transform.SetParent(this.transform);
            var rend = _remoteCursorObj.AddComponent<SpriteRenderer>();
            
            // Cerchiamo uno sprite valido
            var brush = AssetManager.brush_library.get("circ_5");
            if (brush != null) rend.sprite = brush.getSprite();
            
            // VERDE BRILLANTE
            rend.color = new Color(0f, 1f, 0f, 0.9f); 
            rend.sortingLayerName = "EffectsTop"; 
            rend.sortingOrder = 2000;
            _remoteCursorObj.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            _remoteCursorObj.SetActive(false);
        }

        void Update()
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsMultiplayerReady) return;

            // Invia la mia posizione (15 volte al secondo)
            if (Time.time - _lastSendTime > 0.06f)
            {
                Vector3 pos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                NetworkManager.Instance.SendCursorPos(pos.x, pos.y);
                _lastSendTime = Time.time;
            }

            // Interpola il cursore nemico
            if (_remoteCursorObj.activeSelf)
                _remoteCursorObj.transform.position = Vector3.Lerp(_remoteCursorObj.transform.position, _targetPos, Time.deltaTime * 15f);
        }

        public void UpdateRemoteCursor(float x, float y)
        {
            if (!_remoteCursorObj.activeSelf) _remoteCursorObj.SetActive(true);
            _targetPos = new Vector3(x, y, -10f); 
        }
    }
}