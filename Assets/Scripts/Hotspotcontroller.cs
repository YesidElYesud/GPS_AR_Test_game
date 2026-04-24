using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// HotspotController v2 — Se adjunta a cada objeto hotspot en la escena.
///
/// Novedades respecto a v1:
///   - Filtro de etapa: solo activo cuando la etapa actual coincide con data.requiredStage.
///   - Efecto de pulso visual cuando data.isBlinking = true.
///   - Dispatch por tipo de acción (InfoPanel / Cinematic / NpcConversation / SiataCall).
///   - Fallback a InfoPanel mientras los sistemas externos no estén implementados.
///
/// Requiere un Collider en el GameObject para recibir raycasts de clic.
/// </summary>
[RequireComponent(typeof(Collider))]
public class HotspotController : MonoBehaviour
{
    [Header("Datos del Hotspot")]
    [Tooltip("ScriptableObject con el contenido de este hotspot")]
    public HotspotData data;

    [Header("Referencias (auto-detectadas si están en escena)")]
    [Tooltip("Panel UI de información. Se busca automáticamente si está vacío.")]
    public HotspotUIPanel uiPanel;

    [Header("Secuencia de Cámara")]
    [Tooltip("Componente CinematicSequencer en la escena que se ejecutará cuando actionType = CameraSequence.\n" +
             "Arrastra aquí el GameObject que contiene el CinematicSequencer y sus anchors hijos.")]
    public CinematicSequencer cameraSequencer;

    [Header("NPC Walker (opcional)")]
    [Tooltip("Si este hotspot abre un diálogo NPC, arrastra aquí el NPCWaypointWalker del NPC " +
             "para que empiece a caminar automáticamente al responder correctamente.")]
    [SerializeField] private NPCWaypointWalker linkedWalker;

    [Header("Malla (rotación)")]
    [Tooltip("Transform hijo con la malla. Se detecta automáticamente si está vacío.")]
    public Transform meshTransform;

    [Tooltip("Grados por segundo de rotación sobre el eje Y")]
    public float rotationSpeed = 60f;

    // ── Internos ──────────────────────────────────────────────────────────────
    private Transform _playerCamera;
    private bool      _isNearby    = false;
    private bool      _isPanelOpen = false;
    private int       _proximityTriggerFrame = -1; // frame en que la proximidad disparó DispatchAction

    // ── Gizmos en editor ──────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        if (data == null) return;
        Gizmos.color = data.markerColor;
        Gizmos.DrawWireSphere(transform.position, data.triggerRadius);
    }

    private void OnDrawGizmos()
    {
        if (data == null) return;
        Gizmos.color = new Color(data.markerColor.r, data.markerColor.g, data.markerColor.b, 0.4f);
        Gizmos.DrawSphere(transform.position, 0.3f);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Start()
    {
        if (meshTransform == null && transform.childCount > 0)
            meshTransform = transform.GetChild(0);

        ApplyHotspotMaterial();

        if (Camera.main != null)
            _playerCamera = Camera.main.transform;

        if (uiPanel == null)
            uiPanel = FindObjectOfType<HotspotUIPanel>();

        if (data == null)
        {
            Debug.LogWarning($"[Hotspot] '{gameObject.name}' no tiene HotspotData asignado.", this);
            return;
        }

        // Suscribirse al StageManager para filtrar visibilidad por etapa
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged += OnStageChanged;

        // Aplicar visibilidad inicial
        RefreshStageVisibility();
    }

    private void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;
    }

    private void Update()
    {
        if (data == null || _playerCamera == null) return;

        ApplyBlinkEffect();
        CheckProximity();
        if (data.allowClick) CheckClick();
    }

    // ── Filtro de etapa ───────────────────────────────────────────────────────
    private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
    {
        RefreshStageVisibility();
    }

    /// <summary>
    /// Activa o desactiva el hotspot según la etapa actual.
    /// requiredStage = -1 → siempre visible (retrocompatible con hotspots existentes).
    /// </summary>
    private void RefreshStageVisibility()
    {
        if (data == null) return;

        // -1 significa sin restricción de etapa
        if (data.requiredStage < 0)
        {
            gameObject.SetActive(true);
            return;
        }

        bool stageMatch = StageManager.Instance != null &&
                          (int)StageManager.Instance.CurrentStage == data.requiredStage;

        gameObject.SetActive(stageMatch);
    }

    // ── Material ──────────────────────────────────────────────────────────────
    private void ApplyHotspotMaterial()
    {
        if (data == null || data.hotspotMaterial == null || meshTransform == null) return;

        Renderer rend = meshTransform.GetComponent<Renderer>();
        if (rend == null) return;

        Material[] mats = rend.sharedMaterials;
        if (mats.Length < 2)
        {
            Debug.LogWarning($"[Hotspot] '{gameObject.name}': la malla tiene menos de 2 slots de material.", this);
            return;
        }

        mats[1] = data.hotspotMaterial;
        rend.sharedMaterials = mats;
    }

    // ── Rotación de malla ─────────────────────────────────────────────────────
    /// <summary>
    /// Gira el hijo de malla sobre su eje Y a velocidad constante.
    /// Se pausa cuando el jugador está cerca o el panel está abierto.
    /// </summary>
    private void ApplyBlinkEffect()
    {
        if (meshTransform == null) return;
        if (_isNearby || _isPanelOpen) return;

        meshTransform.Rotate(Vector3.right, rotationSpeed * Time.deltaTime, Space.Self);
    }

    // ── Proximidad ────────────────────────────────────────────────────────────
    private void CheckProximity()
    {
        float dist     = Vector3.Distance(_playerCamera.position, transform.position);
        bool  nowNearby = dist <= data.triggerRadius;

        if (nowNearby && !_isNearby)
        {
            _isNearby = true;
            _proximityTriggerFrame = Time.frameCount;
            Debug.Log($"[Hotspot] Entrando en rango de: {data.title}");
            DispatchAction();
        }
        else if (!nowNearby && _isNearby)
        {
            _isNearby = false;
            Debug.Log($"[Hotspot] Saliendo del rango de: {data.title}");
            if (_isPanelOpen) ClosePanel();
        }
    }

    // ── Clic / Tap ────────────────────────────────────────────────────────────
    private void CheckClick()
    {
        // Ignorar clicks en el mismo frame que la proximidad disparó el panel,
        // para evitar que el click de rotación de cámara cierre el panel recién abierto.
        if (Time.frameCount == _proximityTriggerFrame) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        bool clicked = false;

        if (Input.GetMouseButtonDown(0))
            clicked = IsPointerOverThisObject(Input.mousePosition);

        if (!clicked && Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            clicked = IsPointerOverThisObject(Input.GetTouch(0).position);

        if (clicked)
        {
            if (_isPanelOpen) ClosePanel();
            else              DispatchAction();
        }
    }

    private bool IsPointerOverThisObject(Vector2 screenPos)
    {
        if (Camera.main == null) return false;
        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out RaycastHit hit) && hit.collider.gameObject == gameObject;
    }

    // ── Dispatch por tipo de acción ───────────────────────────────────────────
    /// <summary>
    /// Punto central de activación. Ramifica según data.actionType.
    /// Los sistemas externos (CinematicManager, NpcDialoguePanel, SiataCallPanel)
    /// se conectan aquí cuando sean implementados (Sistemas 6, 9, 10).
    /// Hasta entonces hacen fallback a InfoPanel para no romper la escena.
    /// </summary>
    private void DispatchAction()
    {
        if (data == null) return;

        // Actualizar el indicador de nivel de riesgo si el hotspot tiene nivel asignado
        if (data.riskLevel != RiskLevel.None && RiskLevelIndicator.Instance != null)
            RiskLevelIndicator.Instance.SetLevel(data.riskLevel);

        switch (data.actionType)
        {
            case HotspotActionType.InfoPanel:
                OpenInfoPanel();
                break;

            case HotspotActionType.Cinematic:
                if (CinematicManager.Instance != null)
                {
                    _isPanelOpen = true;
                    CinematicManager.Instance.Play(data, this);
                }
                else
                {
                    Debug.LogWarning($"[Hotspot] '{data.title}' → CinematicManager no encontrado en escena. Usando InfoPanel.");
                    OpenInfoPanel();
                }
                break;

            case HotspotActionType.NpcConversation:
                if (NpcDialoguePanel.Instance != null)
                {
                    _isPanelOpen = true;
                    System.Action walkerCb = linkedWalker != null ? (System.Action)linkedWalker.StartWalking : null;
                    NpcDialoguePanel.Instance.Show(data.dialogueData, this, walkerCb);
                }
                else
                {
                    Debug.LogWarning($"[Hotspot] '{data.title}' → NpcDialoguePanel no encontrado en escena. Usando InfoPanel.");
                    OpenInfoPanel();
                }
                break;

            case HotspotActionType.SiataCall:
                if (SiataCallPanel.Instance != null)
                {
                    _isPanelOpen = true;
                    SiataCallPanel.Instance.Show(data.dialogueData, this);
                }
                else if (NpcDialoguePanel.Instance != null)
                {
                    // Fallback: NpcDialoguePanel mientras SiataCallPanel no esté en escena
                    Debug.LogWarning($"[Hotspot] '{data.title}' → SiataCallPanel no encontrado. Usando NpcDialoguePanel.");
                    _isPanelOpen = true;
                    NpcDialoguePanel.Instance.Show(data.dialogueData, this);
                }
                else
                {
                    Debug.LogWarning($"[Hotspot] '{data.title}' → SiataCallPanel y NpcDialoguePanel no encontrados. Usando InfoPanel.");
                    OpenInfoPanel();
                }
                break;

            case HotspotActionType.InfoSlidePanel:
                if (InfoSlidePanel.Instance != null)
                {
                    _isPanelOpen = true;
                    InfoSlidePanel.Instance.Show(data.infoSlides, this, data.infoSlideAdvancesStage);
                }
                else
                {
                    Debug.LogWarning($"[Hotspot] '{data.title}' → InfoSlidePanel no encontrado en escena. Usando InfoPanel.");
                    OpenInfoPanel();
                }
                break;

            case HotspotActionType.CameraSequence:
                if (cameraSequencer != null)
                {
                    _isPanelOpen = true;
                    cameraSequencer.Play(this, data.sequenceAdvancesStage);
                }
                else
                {
                    Debug.LogWarning($"[Hotspot] '{data.title}' → cameraSequencer no asignado en el HotspotController. " +
                                     "Arrastra el CinematicSequencer al campo 'Camera Sequencer' del Inspector.");
                    OpenInfoPanel();
                }
                break;
        }
    }

    // ── Panel informativo (InfoPanel) ─────────────────────────────────────────
    private void OpenInfoPanel()
    {
        if (uiPanel == null) return;
        _isPanelOpen = true;
        uiPanel.Show(data, this);
    }

    public void ClosePanel()
    {
        _isPanelOpen = false;
        if (uiPanel != null) uiPanel.Hide();
    }
}
