using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// JoystickController: Joystick virtual en pantalla para movimiento
/// cuando el GPS no está disponible o el usuario prefiere control manual.
///
/// Uso: Adjuntar a un GameObject con RectTransform que actúe como zona
/// táctil del joystick. El "knob" es el círculo interior que se mueve.
///
/// Completamente independiente de la rotación: solo devuelve Vector2.
/// </summary>
public class JoystickController : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    // ── Inspector ────────────────────────────────────────────────────────────
    [Header("Componentes UI")]
    [Tooltip("El círculo interior (knob) del joystick")]
    public RectTransform knob;

    [Tooltip("Radio máximo de desplazamiento del knob en píxeles")]
    public float knobRadius = 60f;

    [Header("Zona muerta")]
    [Range(0f, 0.3f)]
    [Tooltip("Fracción del radio que se ignora (evita drift)")]
    public float deadZone = 0.1f;

    // ── Propiedades públicas ─────────────────────────────────────────────────
    /// Dirección de entrada normalizada. X = strafe, Y = adelante/atrás.
    public Vector2 InputDirection { get; private set; }

    /// True si el joystick está siendo presionado
    public bool IsPressed { get; private set; }

    // ── Internos ─────────────────────────────────────────────────────────────
    private RectTransform _baseRect;
    private Vector2 _startPos;

    // ── Unity Lifecycle ──────────────────────────────────────────────────────
    private void Awake()
    {
        _baseRect = GetComponent<RectTransform>();
    }

    private void OnDisable()
    {
        // Resetear al desactivar para no dejar input residual
        InputDirection = Vector2.zero;
        IsPressed = false;
        if (knob != null) knob.anchoredPosition = Vector2.zero;
    }

    // ── IPointerDownHandler ──────────────────────────────────────────────────
    public void OnPointerDown(PointerEventData eventData)
    {
        IsPressed = true;
        _startPos = GetLocalPoint(eventData);
        // Knob empieza centrado al hacer tap; se moverá en OnDrag
        if (knob != null)
            knob.anchoredPosition = Vector2.zero;
    }

    // ── IDragHandler ─────────────────────────────────────────────────────────
    public void OnDrag(PointerEventData eventData)
    {
        Vector2 currentPos = GetLocalPoint(eventData);
        Vector2 delta = currentPos - _startPos;

        // Limitar al radio
        if (delta.magnitude > knobRadius)
            delta = delta.normalized * knobRadius;

        // Actualizar knob visual
        if (knob != null)
            knob.anchoredPosition = delta;

        // Calcular input normalizado con zona muerta
        Vector2 normalized = delta / knobRadius;
        float mag = normalized.magnitude;

        if (mag < deadZone)
        {
            InputDirection = Vector2.zero;
        }
        else
        {
            // Remap: [deadZone, 1] → [0, 1]
            float remapped = (mag - deadZone) / (1f - deadZone);
            InputDirection = normalized.normalized * Mathf.Clamp01(remapped);
        }
    }

    // ── IPointerUpHandler ────────────────────────────────────────────────────
    public void OnPointerUp(PointerEventData eventData)
    {
        IsPressed = false;
        InputDirection = Vector2.zero;
        if (knob != null)
            knob.anchoredPosition = Vector2.zero;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private Vector2 GetLocalPoint(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _baseRect,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 local
        );
        return local;
    }
}