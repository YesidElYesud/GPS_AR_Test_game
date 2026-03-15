using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// HotspotController: Se adjunta a cada objeto hotspot en la escena.
/// Detecta activación por:
///   1. Proximidad — la cámara entra en el triggerRadius definido en HotspotData.
///   2. Clic / Tap — el usuario toca el objeto en pantalla.
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
    [Tooltip("Panel UI que muestra la información. Se busca automáticamente si está vacío.")]
    public HotspotUIPanel uiPanel;

    // ── Internos ──────────────────────────────────────────────────────────────
    private Transform _playerCamera;
    private bool      _isNearby      = false;
    private bool      _isPanelOpen   = false;

    // Gizmo visual en editor
    private void OnDrawGizmosSelected()
    {
        if (data == null) return;
        Gizmos.color = data.markerColor;
        Gizmos.DrawWireSphere(transform.position, data.triggerRadius);
    }

    private void OnDrawGizmos()
    {
        if (data == null) return;
        // Ícono siempre visible en escena
        Gizmos.color = new Color(data.markerColor.r, data.markerColor.g, data.markerColor.b, 0.4f);
        Gizmos.DrawSphere(transform.position, 0.3f);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Start()
    {
        // Buscar la cámara principal
        if (Camera.main != null)
            _playerCamera = Camera.main.transform;

        // Buscar el panel UI automáticamente si no está asignado
        if (uiPanel == null)
            uiPanel = FindObjectOfType<HotspotUIPanel>();

        if (data == null)
            Debug.LogWarning($"[Hotspot] '{gameObject.name}' no tiene HotspotData asignado.", this);
    }

    private void Update()
    {
        if (data == null || _playerCamera == null) return;

        CheckProximity();
        if (data.allowClick) CheckClick();
    }

    // ── Proximidad ────────────────────────────────────────────────────────────
    private void CheckProximity()
    {
        float dist = Vector3.Distance(_playerCamera.position, transform.position);
        bool  nowNearby = dist <= data.triggerRadius;

        if (nowNearby && !_isNearby)
        {
            _isNearby = true;
            OnEnterRange();
        }
        else if (!nowNearby && _isNearby)
        {
            _isNearby = false;
            OnExitRange();
        }
    }

    private void OnEnterRange()
    {
        Debug.Log($"[Hotspot] Entrando en rango de: {data.title}");
        OpenPanel();
    }

    private void OnExitRange()
    {
        Debug.Log($"[Hotspot] Saliendo del rango de: {data.title}");
        // Cerrar el panel solo si este hotspot lo abrió
        if (_isPanelOpen) ClosePanel();
    }

    // ── Clic / Tap ────────────────────────────────────────────────────────────
    private void CheckClick()
    {
        // Ignorar clics sobre UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        bool clicked = false;

        // Mouse (editor / desktop)
        if (Input.GetMouseButtonDown(0))
            clicked = IsPointerOverThisObject(Input.mousePosition);

        // Touch (móvil)
        if (!clicked && Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            clicked = IsPointerOverThisObject(Input.GetTouch(0).position);

        if (clicked)
        {
            if (_isPanelOpen) ClosePanel();
            else              OpenPanel();
        }
    }

    private bool IsPointerOverThisObject(Vector2 screenPos)
    {
        if (Camera.main == null) return false;
        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out RaycastHit hit) && hit.collider.gameObject == gameObject;
    }

    // ── Panel ─────────────────────────────────────────────────────────────────
    private void OpenPanel()
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