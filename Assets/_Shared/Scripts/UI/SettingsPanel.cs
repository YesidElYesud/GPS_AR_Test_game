using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Panel de ajustes compacto — sonido global e inversión de ejes del giroscopio.
///
/// Comportamiento:
///   - Abre/cierra desde un botón en HUD (no es fullscreen).
///   - Al abrir: oculta los elementos del HUD indicados en hudElementsToHide[].
///   - Al cerrar: restaura esos elementos a su estado previo.
///   - Persiste el estado en PlayerPrefs (sobrevive recargas de página).
///
/// Setup en Unity (ver sección D de IMPLEMENTATION_PLAN.md para convenciones):
///   1. Crear GO "SettingsPanel" como ÚLTIMO hijo de PhoneFrame.
///   2. RectTransform: Anchor = Top-Stretch (min 0,1 / max 1,1), pivot (0.5,1), PosY = 0.
///   3. Añadir VerticalLayoutGroup + ContentSizeFitter (Vertical Fit = Preferred Size).
///   4. El GO debe arrancar ACTIVO en escena — Start() lo desactiva.
///   5. Asignar todos los campos del Inspector.
///   6. En hudElementsToHide[]: arrastrar JoystickPanel, ListadoBotnes, StatusPanel,
///      RiskLevelIndicator, HotspotPromptBtn.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    public static SettingsPanel Instance { get; private set; }

    // ── Referencias de botones ─────────────────────────────────────────────────
    [Header("Botones del panel")]
    [SerializeField] private Button _closeButton;
    [SerializeField] private Button _soundOnButton;
    [SerializeField] private Button _soundOffButton;
    [SerializeField] private Button _invertPitchButton;
    [SerializeField] private Button _invertRollButton;
    [SerializeField] private Button _invertYawButton;

    // ── Colores de estado ──────────────────────────────────────────────────────
    [Header("Colores — estado activo / inactivo")]
    [Tooltip("Fondo del botón cuando su opción está ACTIVA (p. ej. ON de sonido activado).")]
    [SerializeField] private Color _colorFondoActivo   = new Color(1.00f, 0.80f, 0.00f, 1f); // amarillo
    [Tooltip("Fondo del botón cuando su opción está INACTIVA.")]
    [SerializeField] private Color _colorFondoInactivo = new Color(0.14f, 0.16f, 0.26f, 1f); // navy
    [Tooltip("Color del texto cuando el botón está ACTIVO.")]
    [SerializeField] private Color _colorTextoActivo   = new Color(0.14f, 0.16f, 0.26f, 1f); // navy oscuro
    [Tooltip("Color del texto cuando el botón está INACTIVO.")]
    [SerializeField] private Color _colorTextoInactivo = new Color(1.00f, 0.80f, 0.00f, 1f); // amarillo

    // ── Elementos HUD a ocultar ────────────────────────────────────────────────
    [Header("HUD — ocultar mientras el panel esté abierto")]
    [Tooltip("Arrastrar: JoystickPanel, ListadoBotnes, StatusPanel, RiskLevelIndicator, HotspotPromptBtn")]
    [SerializeField] private GameObject[] _hudElementsToHide;

    // ── PlayerPrefs keys ───────────────────────────────────────────────────────
    const string KEY_SOUND = "settings_sound_on";

    // ── Estado interno ─────────────────────────────────────────────────────────
    private bool   _soundOn;
    private bool[] _hudWasActive;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // NO DontDestroyOnLoad — panel de escena, no Singleton global
    }

    private void Start()
    {
        LoadPrefs();
        WireButtons();
        RefreshAllButtonStates();
        gameObject.SetActive(false);
    }

    // ── API pública ────────────────────────────────────────────────────────────

    /// <summary>Abre el panel y oculta los elementos del HUD.</summary>
    public void Open()
    {
        // Guardar estado previo del HUD y ocultar
        _hudWasActive = new bool[_hudElementsToHide.Length];
        for (int i = 0; i < _hudElementsToHide.Length; i++)
        {
            if (_hudElementsToHide[i] == null) continue;
            _hudWasActive[i] = _hudElementsToHide[i].activeSelf;
            _hudElementsToHide[i].SetActive(false);
        }

        RefreshAllButtonStates();
        gameObject.SetActive(true);
    }

    /// <summary>Cierra el panel y restaura el HUD.</summary>
    public void Close()
    {
        gameObject.SetActive(false);
        RestoreHUD();
    }

    // ── Sonido ─────────────────────────────────────────────────────────────────
    private void SetSound(bool on)
    {
        _soundOn             = on;
        AudioListener.volume = on ? 1f : 0f;
        PlayerPrefs.SetInt(KEY_SOUND, on ? 1 : 0);
        PlayerPrefs.Save();
        RefreshSoundButtons();
    }

    // ── Inversión de ejes ──────────────────────────────────────────────────────
    private void ToggleInvertPitch()
    {
        if (GyroscopeManager.Instance == null) return;
        GyroscopeManager.Instance.SetInvertPitch(!GyroscopeManager.Instance.InvertPitch);
        RefreshInvertButtons();
    }

    private void ToggleInvertRoll()
    {
        if (GyroscopeManager.Instance == null) return;
        GyroscopeManager.Instance.SetInvertRoll(!GyroscopeManager.Instance.InvertRoll);
        RefreshInvertButtons();
    }

    private void ToggleInvertYaw()
    {
        if (GyroscopeManager.Instance == null) return;
        GyroscopeManager.Instance.SetInvertYaw(!GyroscopeManager.Instance.InvertYaw);
        RefreshInvertButtons();
    }

    // ── Feedback visual ────────────────────────────────────────────────────────
    private void RefreshAllButtonStates()
    {
        RefreshSoundButtons();
        RefreshInvertButtons();
    }

    private void RefreshSoundButtons()
    {
        SetButtonState(_soundOnButton,  _soundOn);
        SetButtonState(_soundOffButton, !_soundOn);
    }

    private void RefreshInvertButtons()
    {
        if (GyroscopeManager.Instance == null) return;
        SetButtonState(_invertPitchButton, GyroscopeManager.Instance.InvertPitch);
        SetButtonState(_invertRollButton,  GyroscopeManager.Instance.InvertRoll);
        SetButtonState(_invertYawButton,   GyroscopeManager.Instance.InvertYaw);
    }

    /// <summary>Aplica color de fondo y texto al botón según si está "activo".</summary>
    private void SetButtonState(Button btn, bool active)
    {
        if (btn == null) return;

        var img = btn.GetComponent<Image>();
        if (img != null)
            img.color = active ? _colorFondoActivo : _colorFondoInactivo;

        var label = btn.GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.color = active ? _colorTextoActivo : _colorTextoInactivo;
    }

    // ── Init ───────────────────────────────────────────────────────────────────
    private void LoadPrefs()
    {
        _soundOn             = PlayerPrefs.GetInt(KEY_SOUND, 1) == 1;
        AudioListener.volume = _soundOn ? 1f : 0f;
        // Las preferencias de inversión de ejes las carga GyroscopeManager en LoadSavedOffset()
    }

    private void WireButtons()
    {
        _closeButton?.onClick.AddListener(Close);
        _soundOnButton?.onClick.AddListener(()  => SetSound(true));
        _soundOffButton?.onClick.AddListener(() => SetSound(false));
        _invertPitchButton?.onClick.AddListener(ToggleInvertPitch);
        _invertRollButton?.onClick.AddListener(ToggleInvertRoll);
        _invertYawButton?.onClick.AddListener(ToggleInvertYaw);
    }

    private void RestoreHUD()
    {
        if (_hudWasActive == null) return;
        for (int i = 0; i < _hudElementsToHide.Length; i++)
        {
            if (_hudElementsToHide[i] == null) continue;
            _hudElementsToHide[i].SetActive(_hudWasActive[i]);
        }
        _hudWasActive = null;
    }
}
