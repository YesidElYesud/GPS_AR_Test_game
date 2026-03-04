using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UIManager: Controla la interfaz de usuario AR.
/// Compatible con Unity 2022.3 — usa TextMeshProUGUI.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("Paneles")]
    public GameObject joystickPanel;

    [Header("Textos de estado (TMP)")]
    public TextMeshProUGUI gpsStatusText;
    public TextMeshProUGUI gyroStatusText;
    public TextMeshProUGUI displacementText;
    public TextMeshProUGUI modeText;

    [Header("Botones")]
    public Button toggleJoystickButton;
    public Button recalibrateButton;
    public Button permissionGrantButton;

    [Header("Referencia")]
    public ARCameraController cameraController;

    // ── Internos ──────────────────────────────────────────────────────────────
    private bool  _joystickActive    = false;
    private float _statusUpdateTimer = 0f;
    private const float STATUS_INTERVAL = 0.5f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Start()
    {
        if (toggleJoystickButton  != null) toggleJoystickButton.onClick.AddListener(OnToggleJoystick);
        if (recalibrateButton     != null) recalibrateButton.onClick.AddListener(OnRecalibrate);
        if (permissionGrantButton != null) permissionGrantButton.onClick.AddListener(OnPermissionGrant);
        UpdateModeText();
    }

    private void Update()
    {
        _statusUpdateTimer += Time.deltaTime;
        if (_statusUpdateTimer >= STATUS_INTERVAL)
        {
            _statusUpdateTimer = 0f;
            UpdateStatusDisplay();
        }
        AutoActivateJoystickIfNeeded();
    }

    // ── Estado ────────────────────────────────────────────────────────────────
    private void UpdateStatusDisplay()
    {
        if (gpsStatusText != null && GPSManager.Instance != null)
        {
            bool gpsOk = GPSManager.Instance.IsAvailable && GPSManager.Instance.HasOrigin;
            gpsStatusText.text = gpsOk
                ? "<color=#00FF88>GPS OK</color>"
                : GPSManager.Instance.IsAvailable
                    ? "<color=#FFAA00>GPS: esperando senal...</color>"
                    : "<color=#FF4444>GPS: no disponible</color>";
        }

        if (gyroStatusText != null && GyroscopeManager.Instance != null)
        {
            gyroStatusText.text = GyroscopeManager.Instance.IsAvailable
                ? "<color=#00FF88>Giroscopio OK</color>"
                : "<color=#FF4444>Giroscopio: no disponible</color>";
        }

        if (displacementText != null && GPSManager.Instance != null && GPSManager.Instance.HasOrigin)
        {
            Vector2 d = GPSManager.Instance.DisplacementMeters;
            displacementText.text = string.Format("E:{0:+0.0;-0.0}m  N:{1:+0.0;-0.0}m", d.x, d.y);
        }
        else if (displacementText != null)
        {
            displacementText.text = "";
        }
    }

    private void AutoActivateJoystickIfNeeded()
    {
        if (GPSManager.Instance != null && !GPSManager.Instance.IsAvailable && !_joystickActive)
            ActivateJoystick(true);
    }

    // ── Botones ───────────────────────────────────────────────────────────────
    private void OnToggleJoystick() => ActivateJoystick(!_joystickActive);

    private void ActivateJoystick(bool active)
    {
        _joystickActive = active;
        if (joystickPanel != null) joystickPanel.SetActive(active);
        if (cameraController != null) cameraController.SetForceJoystick(active);
        UpdateModeText();
        UpdateToggleButtonLabel();
    }

    private void OnRecalibrate()
    {
        if (cameraController != null) cameraController.Recalibrate();
    }

    private void OnPermissionGrant() => RequestGyroPermissionJS();

    private void UpdateModeText()
    {
        if (modeText != null)
            modeText.text = _joystickActive ? "Modo: Joystick" : "Modo: GPS";
    }

    private void UpdateToggleButtonLabel()
    {
        if (toggleJoystickButton == null) return;
        TextMeshProUGUI lbl = toggleJoystickButton.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl != null) lbl.text = _joystickActive ? "Usar GPS" : "Joystick ON/OFF";
    }

    public void ShowPermissionPanel() => RequestGyroPermissionJS();

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void RequestDeviceOrientationPermission();
#else
    private static void RequestDeviceOrientationPermission() { }
#endif

    private void RequestGyroPermissionJS() => RequestDeviceOrientationPermission();
}