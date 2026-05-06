using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// HotspotUIPanel v2 — Panel informativo enriquecido (Sistema 5).
///
/// Nuevos elementos respecto a v1:
///   - headerImage   : imagen de cabecera en la parte superior del panel.
///   - riskLevelBadge: chip de color que indica el nivel de riesgo (N1–N4).
///   - riskLevelText : TextMeshProUGUI dentro del badge ("N1", "N2"…).
///   - descScrollRect: ScrollRect que envuelve la descripción para texto largo.
///
/// Retrocompatibilidad total: todos los campos nuevos son opcionales.
/// Si no se asignan en el Inspector, el panel funciona exactamente igual que v1.
///
/// SETUP EN EDITOR — Jerarquía sugerida:
///   HotspotPanel                    [HotspotUIPanel.cs]  (inactivo por defecto)
///   ├── HeaderImage                 [Image]              ← headerImage
///   ├── TitleRow
///   │   ├── TitleText               [TextMeshProUGUI]    ← titleText
///   │   └── RiskBadge              [Image]              ← riskLevelBadge
///   │       └── RiskLevelText       [TextMeshProUGUI]    ← riskLevelText
///   ├── IconImage                   [Image]              ← iconImage
///   ├── DescriptionScrollRect       [ScrollRect]         ← descScrollRect
///   │   └── Viewport                [Mask]
///   │       └── Content             [VerticalLayoutGroup + ContentSizeFitter]
///   │           └── DescText        [TextMeshProUGUI]    ← descText
///   └── CloseButton                 [Button]             ← closeButton
/// </summary>
public class HotspotUIPanel : MonoBehaviour
{
    // ── Campos heredados de v1 ────────────────────────────────────────────────
    [Header("UI base")]
    [Tooltip("Texto del título del hotspot")]
    public TextMeshProUGUI titleText;

    [Tooltip("Texto de la descripción (debe estar dentro de descScrollRect.Content si se usa scroll)")]
    public TextMeshProUGUI descText;

    [Tooltip("Imagen del ícono (se oculta si el hotspot no tiene ícono)")]
    public Image iconImage;

    [Tooltip("Botón para cerrar el panel manualmente")]
    public Button closeButton;

    [Tooltip("RectTransform raíz del panel. Si está vacío se usa el de este GameObject.")]
    public RectTransform panel;

    // ── Campos nuevos (Sistema 5) ─────────────────────────────────────────────
    [Header("Panel Enriquecido (opcional)")]
    [Tooltip("Imagen de cabecera del panel. Se oculta si HotspotData.headerImage es null.")]
    public Image headerImage;

    [Tooltip("ScrollRect que envuelve la descripción. Si se asigna, el scroll se reinicia\n" +
             "al principio cada vez que se abre el panel.")]
    public ScrollRect descScrollRect;

    [Tooltip("Fondo del badge de nivel de riesgo. Se oculta si HotspotData.riskLevel == None.")]
    public Image riskLevelBadge;

    [Tooltip("Texto dentro del badge (muestra 'N1', 'N2', etc.).")]
    public TextMeshProUGUI riskLevelText;

    // ── Colores por nivel de riesgo ───────────────────────────────────────────
    [Header("Colores de riesgo")]
    [Tooltip("Color del badge para Nivel 1 — Bajo")]
    public Color colorN1 = new Color(0.30f, 0.69f, 0.31f); // verde   #4CAF50

    [Tooltip("Color del badge para Nivel 2 — Moderado")]
    public Color colorN2 = new Color(1.00f, 0.76f, 0.03f); // amarillo #FFC107

    [Tooltip("Color del badge para Nivel 3 — Alto")]
    public Color colorN3 = new Color(1.00f, 0.60f, 0.00f); // naranja  #FF9800

    [Tooltip("Color del badge para Nivel 4 — Crítico")]
    public Color colorN4 = new Color(0.96f, 0.26f, 0.21f); // rojo     #F44336

    // ── Internos ──────────────────────────────────────────────────────────────
    private HotspotController _currentHotspot;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (panel == null)
            panel = GetComponent<RectTransform>();

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>Muestra el panel con los datos del hotspot indicado.</summary>
    public void Show(HotspotData data, HotspotController source)
    {
        if (data == null) return;

        _currentHotspot = source;

        PopulateBase(data);
        PopulateHeaderImage(data);
        PopulateRiskBadge(data);
        ResetScroll();

        SetPanelActive(true);
    }

    /// <summary>Oculta el panel.</summary>
    public void Hide()
    {
        _currentHotspot = null;
        SetPanelActive(false);
    }

    // ── Relleno de contenido ──────────────────────────────────────────────────

    private void PopulateBase(HotspotData data)
    {
        if (titleText != null)
            titleText.text = data.title;

        if (descText != null)
            descText.text = data.description;

        if (iconImage != null)
        {
            bool hasIcon = data.icon != null;
            iconImage.gameObject.SetActive(hasIcon);
            if (hasIcon) iconImage.sprite = data.icon;
        }
    }

    private void PopulateHeaderImage(HotspotData data)
    {
        if (headerImage == null) return;

        bool hasHeader = data.headerImage != null;
        headerImage.gameObject.SetActive(hasHeader);
        if (hasHeader) headerImage.sprite = data.headerImage;
    }

    private void PopulateRiskBadge(HotspotData data)
    {
        bool hasRisk = data.riskLevel != RiskLevel.None;

        if (riskLevelBadge != null)
        {
            riskLevelBadge.gameObject.SetActive(hasRisk);
            if (hasRisk) riskLevelBadge.color = GetRiskColor(data.riskLevel);
        }

        if (riskLevelText != null)
        {
            riskLevelText.gameObject.SetActive(hasRisk);
            if (hasRisk) riskLevelText.text = data.riskLevel.ToString(); // "N1", "N2", etc.
        }
    }

    private void ResetScroll()
    {
        if (descScrollRect != null && descScrollRect.content != null)
            descScrollRect.normalizedPosition = new Vector2(0f, 1f); // volver al inicio
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Color GetRiskColor(RiskLevel level)
    {
        return level switch
        {
            RiskLevel.N1 => colorN1,
            RiskLevel.N2 => colorN2,
            RiskLevel.N3 => colorN3,
            RiskLevel.N4 => colorN4,
            _            => Color.gray,
        };
    }

    private void OnCloseClicked()
    {
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
