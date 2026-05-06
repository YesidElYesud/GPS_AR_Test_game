using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SensorCalibrationPanel — Panel flotante de ajuste fino del giroscopio.
///
/// Permite al usuario corregir la orientación si el giroscopio no coincide
/// exactamente con la realidad. Los ajustes persisten en PlayerPrefs.
///
/// Jerarquía sugerida en Canvas:
///   SensorCalibrationPanel  (inactivo por defecto)
///   ├── Header
///   │   └── TitleText          (TextMeshProUGUI)
///   ├── SliderRow_Pitch
///   │   ├── LabelPitch         (TextMeshProUGUI)  "Pitch"
///   │   ├── SliderPitch        (Slider, -45 → 45)
///   │   └── ValuePitch         (TextMeshProUGUI)  "-12.0°"
///   ├── SliderRow_Roll
///   │   ├── LabelRoll          (TextMeshProUGUI)  "Roll"
///   │   ├── SliderRoll         (Slider, -45 → 45)
///   │   └── ValueRoll          (TextMeshProUGUI)
///   ├── SliderRow_Yaw
///   │   ├── LabelYaw           (TextMeshProUGUI)  "Yaw"
///   │   ├── SliderYaw          (Slider, -180 → 180)
///   │   └── ValueYaw           (TextMeshProUGUI)
///   └── ButtonRow
///       ├── ResetButton        (Button)  "Restablecer"
///       ├── RecalibrateButton  (Button)  "Recalibrar"
///       └── CloseButton        (Button)  "Cerrar"
/// </summary>
public class SensorCalibrationPanel : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static SensorCalibrationPanel Instance { get; private set; }

    // ── Inspector: Sliders ────────────────────────────────────────────────────
    [Header("Sliders")]
    public Slider sliderPitch;
    public Slider sliderRoll;
    public Slider sliderYaw;

    // ── Inspector: Value labels ────────────────────────────────────────────────
    [Header("Etiquetas de valor")]
    public TextMeshProUGUI valuePitchText;
    public TextMeshProUGUI valueRollText;
    public TextMeshProUGUI valueYawText;

    // ── Inspector: Buttons ────────────────────────────────────────────────────
    [Header("Botones")]
    public Button resetButton;
    public Button recalibrateButton;
    public Button closeButton;

    // ── Inspector: Rango de sliders ───────────────────────────────────────────
    [Header("Rango angular")]
    [Tooltip("Límite máximo de ajuste para Pitch y Roll (en grados).")]
    [Range(10f, 90f)] public float pitchRollRange = 45f;
    [Tooltip("Límite máximo de ajuste para Yaw (en grados).")]
    [Range(45f, 180f)] public float yawRange = 180f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        gameObject.SetActive(false);
    }

    private void Start()
    {
        ConfigureSliders();
        ConnectButtons();
    }

    // ── Configuración inicial ─────────────────────────────────────────────────
    private void ConfigureSliders()
    {
        if (sliderPitch != null) { sliderPitch.minValue = -pitchRollRange; sliderPitch.maxValue = pitchRollRange; }
        if (sliderRoll  != null) { sliderRoll.minValue  = -pitchRollRange; sliderRoll.maxValue  = pitchRollRange; }
        if (sliderYaw   != null) { sliderYaw.minValue   = -yawRange;       sliderYaw.maxValue   = yawRange; }

        if (sliderPitch != null) sliderPitch.onValueChanged.AddListener(v => { UpdateLabel(valuePitchText, v); ApplyCurrentOffsets(); });
        if (sliderRoll  != null) sliderRoll.onValueChanged.AddListener(v  => { UpdateLabel(valueRollText,  v); ApplyCurrentOffsets(); });
        if (sliderYaw   != null) sliderYaw.onValueChanged.AddListener(v   => { UpdateLabel(valueYawText,   v); ApplyCurrentOffsets(); });
    }

    private void ConnectButtons()
    {
        if (resetButton       != null) resetButton.onClick.AddListener(OnResetClicked);
        if (recalibrateButton != null) recalibrateButton.onClick.AddListener(OnRecalibrateClicked);
        if (closeButton       != null) closeButton.onClick.AddListener(Hide);
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>Muestra el panel y carga los valores guardados en los sliders.</summary>
    public void Show()
    {
        LoadSlidersFromGyro();
        gameObject.SetActive(true);
    }

    /// <summary>Guarda los valores actuales y cierra el panel.</summary>
    public void Hide()
    {
        ApplyCurrentOffsets();
        gameObject.SetActive(false);
    }

    // ── Carga de valores ──────────────────────────────────────────────────────
    private void LoadSlidersFromGyro()
    {
        if (GyroscopeManager.Instance == null) return;

        Vector3 saved = GyroscopeManager.Instance.GetEulerOffset();
        // _eulerOffset está guardado como (pitch, yaw, roll) en GyroscopeManager
        SetSliderSilent(sliderPitch, saved.x);
        SetSliderSilent(sliderRoll,  saved.z);
        SetSliderSilent(sliderYaw,   saved.y);

        UpdateLabel(valuePitchText, saved.x);
        UpdateLabel(valueRollText,  saved.z);
        UpdateLabel(valueYawText,   saved.y);
    }

    // ── Aplicación ────────────────────────────────────────────────────────────
    private void ApplyCurrentOffsets()
    {
        if (GyroscopeManager.Instance == null) return;

        float pitch = sliderPitch != null ? sliderPitch.value : 0f;
        float roll  = sliderRoll  != null ? sliderRoll.value  : 0f;
        float yaw   = sliderYaw   != null ? sliderYaw.value   : 0f;

        GyroscopeManager.Instance.SetOffset(pitch, roll, yaw);
    }

    // ── Botones ───────────────────────────────────────────────────────────────
    private void OnResetClicked()
    {
        SetSliderSilent(sliderPitch, 0f);
        SetSliderSilent(sliderRoll,  0f);
        SetSliderSilent(sliderYaw,   0f);

        UpdateLabel(valuePitchText, 0f);
        UpdateLabel(valueRollText,  0f);
        UpdateLabel(valueYawText,   0f);

        if (GyroscopeManager.Instance != null)
            GyroscopeManager.Instance.SetOffset(0f, 0f, 0f);

        Debug.Log("[CalibPanel] Offsets restablecidos a cero.");
    }

    private void OnRecalibrateClicked()
    {
        if (GyroscopeManager.Instance != null)
        {
            GyroscopeManager.Instance.Recalibrate();
            Debug.Log("[CalibPanel] Recalibración solicitada.");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Asigna el valor al slider sin disparar onValueChanged.</summary>
    private static void SetSliderSilent(Slider slider, float value)
    {
        if (slider == null) return;
        slider.SetValueWithoutNotify(value);
    }

    private static void UpdateLabel(TextMeshProUGUI label, float value)
    {
        if (label != null) label.text = $"{value:+0.0;-0.0;0.0}°";
    }
}
