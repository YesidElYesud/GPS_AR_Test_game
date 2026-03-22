using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UIManager v3
/// Todos los campos se conectan desde el editor.
/// No depende de ARSceneSetup ni de ningún script de construcción de UI.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("Paneles")]
    [Tooltip("Panel del joystick virtual")]
    public GameObject joystickPanel;

    [Header("Textos de estado")]
    public TextMeshProUGUI gpsStatusText;
    public TextMeshProUGUI gyroStatusText;
    public TextMeshProUGUI displacementText;
    public TextMeshProUGUI modeText;

    [Header("Botones")]
    public Button toggleJoystickButton;
    public Button recalibrateButton;
    public Button permissionGrantButton;

    [Header("Referencia de cámara")]
    [Tooltip("Arrastra aquí la Main Camera (que tiene ARCameraController)")]
    public ARCameraController cameraController;

    // ── Internos ──────────────────────────────────────────────────────────────
    private bool  _joystickActive    = false;
    private float _statusUpdateTimer = 0f;
    private const float STATUS_INTERVAL = 0.5f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Start()
    {
        // Auto-buscar cámara si no se asignó
        if (cameraController == null)
            cameraController = FindObjectOfType<ARCameraController>();

        // Conectar botones
        if (toggleJoystickButton  != null) toggleJoystickButton.onClick.AddListener(OnToggleJoystick);
        if (recalibrateButton     != null) recalibrateButton.onClick.AddListener(OnRecalibrate);
        if (permissionGrantButton != null) permissionGrantButton.onClick.AddListener(OnPermissionGrant);

        // Suscribirse al StageManager para actualizar UI en cada cambio de etapa
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged += HandleStageChanged;

        // Estado inicial
        SetJoystickPanelVisible(false);
        UpdateModeText();
        UpdateToggleButtonLabel();
        CheckAutoJoystick();
    }

    private void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= HandleStageChanged;
    }

    private void Update()
    {
        _statusUpdateTimer += Time.deltaTime;
        if (_statusUpdateTimer >= STATUS_INTERVAL)
        {
            _statusUpdateTimer = 0f;
            UpdateStatusDisplay();
        }
        CheckAutoJoystick();
    }

    // ── StageManager ─────────────────────────────────────────────────────────
    private void HandleStageChanged(StageManager.Stage previous, StageManager.Stage current)
    {
        // Actualizar textos de estado inmediatamente al cambiar de etapa
        // (no esperar al siguiente tick del timer de 0.5s)
        UpdateStatusDisplay();
        UpdateModeText();
    }

    // ── Estado GPS / Giroscopio ───────────────────────────────────────────────
    private void UpdateStatusDisplay()
    {
        if (gpsStatusText != null && GPSManager.Instance != null)
        {
            bool gpsOk = GPSManager.Instance.IsAvailable && GPSManager.Instance.HasOrigin;
            gpsStatusText.text = gpsOk
                ? "<color=#00FF88>GPS OK</color>"
                : GPSManager.Instance.IsAvailable
                    ? "<color=#FFAA00>GPS: esperando señal...</color>"
                    : "<color=#FF4444>GPS: no disponible</color>";
        }

        if (gyroStatusText != null && GyroscopeManager.Instance != null)
        {
            gyroStatusText.text = GyroscopeManager.Instance.IsAvailable
                ? "<color=#00FF88>Giroscopio OK</color>"
                : "<color=#FF4444>Giroscopio: no disponible</color>";
        }

        if (displacementText != null)
        {
            bool hasDisp = GPSManager.Instance != null && GPSManager.Instance.HasOrigin;
            if (hasDisp)
            {
                Vector2 d = GPSManager.Instance.DisplacementMeters;
                displacementText.text = string.Format("E:{0:+0.0;-0.0}m  N:{1:+0.0;-0.0}m", d.x, d.y);
            }
            else
            {
                displacementText.text = "";
            }
        }
    }

    // Activa joystick automáticamente si no hay GPS (solo activa, nunca desactiva)
    private void CheckAutoJoystick()
    {
        if (_joystickActive) return;
        bool gpsUnavailable = GPSManager.Instance == null || !GPSManager.Instance.IsAvailable;
        if (gpsUnavailable)
            ActivateJoystick(true);
    }

    // ── Botones ───────────────────────────────────────────────────────────────
    private void OnToggleJoystick() => ActivateJoystick(!_joystickActive);

    private void ActivateJoystick(bool active)
    {
        _joystickActive = active;
        SetJoystickPanelVisible(active);
        if (cameraController != null)
            cameraController.SetForceJoystick(active);
        UpdateModeText();
        UpdateToggleButtonLabel();
    }

    private void SetJoystickPanelVisible(bool visible)
    {
        if (joystickPanel != null)
            joystickPanel.SetActive(visible);
    }

    private void OnRecalibrate()
    {
        if (cameraController != null)
            cameraController.Recalibrate();
    }

    private void OnPermissionGrant() => RequestGyroPermissionJS();

    // ── Textos ────────────────────────────────────────────────────────────────
    private void UpdateModeText()
    {
        if (modeText != null)
            modeText.text = _joystickActive ? "Modo: Joystick" : "Modo: GPS";
    }

    private void UpdateToggleButtonLabel()
    {
        if (toggleJoystickButton == null) return;
        TextMeshProUGUI lbl = toggleJoystickButton.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl != null)
            lbl.text = _joystickActive ? "Usar GPS" : "Joystick ON/OFF";
    }

    // ── Permiso giroscopio iOS ────────────────────────────────────────────────
    public void ShowPermissionPanel() => RequestGyroPermissionJS();

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void RequestDeviceOrientationPermission();
#else
    private static void RequestDeviceOrientationPermission() { }
#endif

    private void RequestGyroPermissionJS() => RequestDeviceOrientationPermission();
}