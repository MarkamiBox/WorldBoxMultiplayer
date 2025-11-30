using UnityEngine;

namespace WorldBoxMultiplayer
{
    public class CursorHandler : MonoBehaviour
    {
        public static CursorHandler Instance;

        private GameObject _remoteCursorObj;
        private SpriteRenderer _cursorRenderer;
        private Vector3 _targetPos;
        private float _lastSendTime;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            // Creiamo l'oggetto visivo per il cursore dell'amico
            _remoteCursorObj = new GameObject("RemoteCursor");
            _remoteCursorObj.transform.SetParent(this.transform);
            
            _cursorRenderer = _remoteCursorObj.AddComponent<SpriteRenderer>();
            
            // Usiamo uno sprite semplice dal gioco (es. il pennello circolare)
            // Se questo sprite non esiste, apparirà un quadratino bianco (che va bene lo stesso)
            _cursorRenderer.sprite = AssetManager.brush_library.get("circ_5").getSprite();
            
            // Colore diverso per distinguerlo (es. Magenta brillante)
            _cursorRenderer.color = new Color(1f, 0f, 1f, 0.8f); 
            
            // Lo mettiamo sopra a tutto (Sorting Layer)
            _cursorRenderer.sortingLayerName = "EffectsTop"; 
            _cursorRenderer.sortingOrder = 1000;

            // Scala un po' più piccola
            _remoteCursorObj.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            
            _remoteCursorObj.SetActive(false); // Nascondi all'inizio
        }

        void Update()
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsMultiplayerReady) 
            {
                if (_remoteCursorObj.activeSelf) _remoteCursorObj.SetActive(false);
                return;
            }

            // 1. Invia la MIA posizione del mouse (10 volte al secondo)
            if (Time.time - _lastSendTime > 0.1f)
            {
                Vector3 myMousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                NetworkManager.Instance.SendCursorPos(myMousePos.x, myMousePos.y);
                _lastSendTime = Time.time;
            }

            // 2. Aggiorna la posizione del cursore REMOTO (interpolazione fluida)
            if (_remoteCursorObj.activeSelf)
            {
                _remoteCursorObj.transform.position = Vector3.Lerp(_remoteCursorObj.transform.position, _targetPos, Time.deltaTime * 10f);
            }
        }

        // Chiamato dal NetworkManager quando arriva un pacchetto "C"
        public void UpdateRemoteCursor(float x, float y)
        {
            if (!_remoteCursorObj.activeSelf) _remoteCursorObj.SetActive(true);
            
            // Impostiamo la destinazione (Z deve essere corretta per essere visibile)
            _targetPos = new Vector3(x, y, -10f); 
        }
    }
}