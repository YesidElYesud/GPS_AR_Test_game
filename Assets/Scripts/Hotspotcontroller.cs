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

    // ── Internos ──────────────────────────────────────────────────────────────
    private Transform _playerCamera;
    private bool      _isNearby    = false;
    private bool      _isPanelOpen = false;
    private Vector3   _baseScale;

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
        _baseScale = transform.localScale;

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

    // ── Efecto de pulso visual ────────────────────────────────────────────────
    /// <summary>
    /// Oscila la escala del objeto para indicar que es interactuable.
    /// Se detiene cuando el jugador está cerca o el panel está abierto.
    /// </summary>
    private void ApplyBlinkEffect()
    {
        if (data == null || !data.isBlinking) return;

        if (_isNearby || _isPanelOpen)
        {
            // Restaurar escala base cuando está activo
            transform.localScale = _baseScale;
            return;
        }

        float pulse = 1f + 0.12f * Mathf.Sin(Time.time * data.blinkSpeed * Mathf.PI * 2f);
        transform.localScale = _baseScale * pulse;
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

        switch (data.actionType)
        {
            case HotspotActionType.InfoPanel:
                OpenInfoPanel();
                break;

            case HotspotActionType.Cinematic:
                // ── Sistema 6: CinematicManager (pendiente de implementación) ──
                // Cuando esté implementado, sustituir estas líneas por:
                //   CinematicManager.Instance.Play(data.cinematicClip);
                Debug.Log($"[Hotspot] '{data.title}' → Cinematic (Sistema 6 pendiente). Usando InfoPanel.");
                OpenInfoPanel();
                break;

            case HotspotActionType.NpcConversation:
                if (NpcDialoguePanel.Instance != null)
                {
                    _isPanelOpen = true;
                    NpcDialoguePanel.Instance.Show(data.dialogueData, this);
                }
                else
                {
                    Debug.LogWarning($"[Hotspot] '{data.title}' → NpcDialoguePanel no encontrado en escena. Usando InfoPanel.");
                    OpenInfoPanel();
                }
                break;

            case HotspotActionType.SiataCall:
                if (NpcDialoguePanel.Instance != null)
                {
                    _isPanelOpen = true;
                    NpcDialoguePanel.Instance.Show(data.dialogueData, this);
                }
                else
                {
                    Debug.LogWarning($"[Hotspot] '{data.title}' → NpcDialoguePanel no encontrado (SiataCall fallback). Usando InfoPanel.");
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
