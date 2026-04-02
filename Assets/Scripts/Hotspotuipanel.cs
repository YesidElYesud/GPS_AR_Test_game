using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// HotspotUIPanel: Panel UI que muestra la información de un hotspot.
///
/// SETUP EN EDITOR:
///   1. Crear un Panel en el Canvas (ej: "HotspotPanel")
///   2. Adjuntar este script al Panel
///   3. Asignar los campos desde el Inspector:
///      - titleText    → TextMeshProUGUI con el título
///      - descText     → TextMeshProUGUI con la descripción
///      - iconImage    → Image para el ícono (opcional)
///      - closeButton  → Button para cerrar
///      - panel        → El propio RectTransform del panel (o dejarlo vacío para auto-detectar)
///
/// El panel arranca desactivado. Se activa/desactiva via Show() / Hide().
/// </summary>
public class HotspotUIPanel : MonoBehaviour
{
    [Header("Elementos UI (asignar desde el Inspector)")]
    [Tooltip("Texto del título del hotspot")]
    public TextMeshProUGUI titleText;

    [Tooltip("Texto de la descripción / información")]
    public TextMeshProUGUI descText;

    [Tooltip("Imagen del ícono (se oculta si el hotspot no tiene ícono)")]
    public Image iconImage;

    [Tooltip("Botón para cerrar el panel manualmente")]
    public Button closeButton;

    [Tooltip("RectTransform raíz del panel. Si está vacío se usa el de este GameObject.")]
    public RectTransform panel;

    // ── Internos ──────────────────────────────────────────────────────────────
    private HotspotController _currentHotspot;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        // Auto-referenciar panel si no está asignado
        if (panel == null)
            panel = GetComponent<RectTransform>();

        // Conectar botón de cierre
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);

        // No llamar SetPanelActive(false) aquí: si Show() activa el GameObject,
        // Unity dispara Awake en ese mismo frame y lo volvería a desactivar.
        // El panel arranca inactivo desde el prefab/escena.
    }

    // ── API pública ───────────────────────────────────────────────────────────
    /// <summary>Muestra el panel con los datos del hotspot indicado.</summary>
    public void Show(HotspotData data, HotspotController source)
    {
        if (data == null) return;

        _currentHotspot = source;

        // Título
        if (titleText != null)
            titleText.text = data.title;

        // Descripción
        if (descText != null)
            descText.text = data.description;

        // Ícono
        if (iconImage != null)
        {
            if (data.icon != null)
            {
                iconImage.sprite  = data.icon;
                iconImage.gameObject.SetActive(true);
            }
            else
            {
                iconImage.gameObject.SetActive(false);
            }
        }

        SetPanelActive(true);
    }

    /// <summary>Oculta el panel.</summary>
    public void Hide()
    {
        _currentHotspot = null;
        SetPanelActive(false);
    }

    // ── Internos ──────────────────────────────────────────────────────────────
    private void OnCloseClicked()
    {
        // Notificar al hotspot que el usuario cerró el panel
        if (_currentHotspot != null)
            _currentHotspot.ClosePanel();
        else
            Hide();
    }

    private void SetPanelActive(bool active)
    {
        if (panel != null)
            panel.gameObject.SetActive(active);
    }
}