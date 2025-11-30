using UnityEngine;
using UnityEngine.UI;

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
            
            // Usiamo l'icona "Mano Divina" (godFinger) che è presente nel gioco
            var godFinger = AssetManager.powers.get("god_finger");
            if (godFinger != null) rend.sprite = godFinger.getIconSprite();
            else rend.sprite = AssetManager.brush_library.get("circ_5").getSprite(); // Fallback
            
            // Colore: Ciano Brillante (molto visibile)
            rend.color = new Color(0f, 1f, 1f, 1f); 
            rend.sortingLayerName = "EffectsTop"; 
            rend.sortingOrder = 9999; // Sempre sopra tutto
            
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

            // Invia la mia posizione (20 volte al secondo per fluidità)
            if (Time.time - _lastSendTime > 0.05f)
            {
                Vector3 pos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                NetworkManager.Instance.SendCursorPos(pos.x, pos.y);
                _lastSendTime = Time.time;
            }

            // Muovi il cursore dell'amico
            if (_remoteCursorObj.activeSelf)
                _remoteCursorObj.transform.position = Vector3.Lerp(_remoteCursorObj.transform.position, _targetPos, Time.deltaTime * 20f);
        }

        public void UpdateRemoteCursor(float x, float y)
        {
            if (!_remoteCursorObj.activeSelf) _remoteCursorObj.SetActive(true);
            _targetPos = new Vector3(x, y, -10f); 
        }
    }
}