using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// HotspotController v3 — Interacción mediada por HUD.
///
/// Al entrar en el radio del jugador muestra el HotspotPromptButton en el HUD.
/// El panel se abre SOLO cuando el jugador pulsa ese botón, no automáticamente.
/// Al salir del radio el botón se oculta; si había un panel abierto se cierra.
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

    [Header("Botón de repetición (opcional — solo CameraSequence)")]
    [Tooltip("GameObject de un botón oculto en el HUD. Se activa la primera vez que el jugador " +
             "entra en contacto con este hotspot. El OnClick del botón debe llamar a ReplaySequence().")]
    [SerializeField] private GameObject replayButton;

    [Header("Malla (rotación)")]
    [Tooltip("Transform hijo con la malla. Se detecta automáticamente si está vacío.")]
    public Transform meshTransform;

    [Tooltip("Grados por segundo de rotación sobre el eje Y")]
    public float rotationSpeed = 60f;

    // ── Internos ──────────────────────────────────────────────────────────────
    private Transform _playerCamera;
    private bool      _isNearby    = false;
    private bool      _isPanelOpen = false;
    private bool      _replayButtonActivated = false;

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

        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged += OnStageChanged;

        RefreshStageVisibility();
    }

    private void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;

        HotspotPromptButton.Instance?.UnregisterHotspot(this);
    }

    private void OnDisable()
    {
        // Al desactivarse (p.ej. StageManager lo oculta) limpiar el botón de prompt
        HotspotPromptButton.Instance?.UnregisterHotspot(this);
        _isNearby = false;
    }

    private void Update()
    {
        if (data == null || _playerCamera == null) return;
        if (SceneOverviewController.Instance != null && SceneOverviewController.Instance.IsActive) return;

        ApplyRotationEffect();
        CheckProximity();
    }

    // ── Filtro de etapa ───────────────────────────────────────────────────────
    private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
    {
        RefreshStageVisibility();
    }

    /// <summary>
    /// requiredStage = -1 → sin restricción propia, StageManager tiene autoridad total.
    /// requiredStage >= 0 → gestiona su propia visibilidad.
    /// </summary>
    private void RefreshStageVisibility()
    {
        if (data == null) return;
        if (data.requiredStage < 0) return;

        bool stageMatch = StageManager.Instance != null &&
                          (int)StageManager.Instance.CurrentStage == data.requiredStage;

        gameObject.SetActive(stageMatch);
        // OnDisable se encarga de limpiar el prompt button si stageMatch es false
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
    private void ApplyRotationEffect()
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
            Debug.Log($"[Hotspot] Entrando en rango de: {data.title}");
            // Solo mostrar botón si no hay un panel abierto de este hotspot
            if (!_isPanelOpen)
                HotspotPromptButton.Instance?.RegisterHotspot(this);
        }
        else if (!nowNearby && _isNearby)
        {
            _isNearby = false;
            Debug.Log($"[Hotspot] Saliendo del rango de: {data.title}");
            HotspotPromptButton.Instance?.UnregisterHotspot(this);
            if (_isPanelOpen) ClosePanel();
        }
    }

    // ── Dispatch por tipo de acción ───────────────────────────────────────────
    /// <summary>
    /// Punto central de activación. Llamado por HotspotPromptButton al pulsar el botón HUD.
    /// </summary>
    public void DispatchAction()
    {
        if (data == null) return;

        // Ocultar el botón de prompt mientras el panel está abierto
        HotspotPromptButton.Instance?.UnregisterHotspot(this);

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
                    Debug.LogWarning($"[Hotspot] '{data.title}' → CinematicManager no encontrado. Usando InfoPanel.");
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
                    Debug.LogWarning($"[Hotspot] '{data.title}' → NpcDialoguePanel no encontrado. Usando InfoPanel.");
                    OpenInfoPanel();
                }
                break;

            case HotspotActionType.SiataCall:
                if (SiataCallPanel.Instance != null)
                {
                    _isPanelOpen = true;
                    if (data.siataSequence != null)
                        SiataCallPanel.Instance.Show(data.siataSequence, this);
                    else
                        SiataCallPanel.Instance.Show(data.dialogueData, this);
                }
                else if (NpcDialoguePanel.Instance != null)
                {
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
                    Debug.LogWarning($"[Hotspot] '{data.title}' → InfoSlidePanel no encontrado. Usando InfoPanel.");
                    OpenInfoPanel();
                }
                break;

            case HotspotActionType.CameraSequence:
                if (cameraSequencer != null)
                {
                    _isPanelOpen = true;
                    if (SceneOverviewController.Instance != null &&
                        SceneOverviewController.Instance.overviewSequencer == cameraSequencer)
                    {
                        if (data.sequenceAdvancesStage)
                            StageManager.Instance?.NextStage();
                        SceneOverviewController.Instance.Enter(false, this);
                    }
                    else
                    {
                        cameraSequencer.Play(this, data.sequenceAdvancesStage);
                    }
                    ActivateReplayButton();
                }
                else
                {
                    Debug.LogWarning($"[Hotspot] '{data.title}' → cameraSequencer no asignado. Usando InfoPanel.");
                    OpenInfoPanel();
                }
                break;
        }
    }

    // ── Panel informativo ─────────────────────────────────────────────────────
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

        // Si el jugador sigue en rango, volver a mostrar el botón de prompt
        if (_isNearby)
            HotspotPromptButton.Instance?.RegisterHotspot(this);
    }

    // ── Repetición de secuencia ───────────────────────────────────────────────
    private void ActivateReplayButton()
    {
        if (replayButton == null || _replayButtonActivated) return;
        replayButton.SetActive(true);
        _replayButtonActivated = true;
    }

    public void ReplaySequence()
    {
        if (cameraSequencer == null) return;
        if (SceneOverviewController.Instance != null &&
            SceneOverviewController.Instance.overviewSequencer == cameraSequencer)
        {
            if (!SceneOverviewController.Instance.IsActive)
                SceneOverviewController.Instance.Enter(false, null);
            return;
        }
        if (!cameraSequencer.IsPlaying)
            cameraSequencer.Play(null, false);
    }
}
