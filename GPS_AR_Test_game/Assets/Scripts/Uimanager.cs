using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UIManager: Controla toda la interfaz de usuario.
///   - Estado del GPS e indicadores de sensores
///   - Toggle entre GPS y Joystick
///   - Botón de recalibración
///   - Avisos de permisos al usuario
/// </summary>
public class UIManager : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Paneles")]
    public GameObject joystickPanel;
    public GameObject permissionPanel;    // "Por favor acepta los permisos"
    public GameObject statusPanel;

    [Header("Textos de estado")]
    public TextMeshProUGUI gpsStatusText;
    public TextMeshProUGUI gyroStatusText;
    public TextMeshProUGUI displacementText;
    public TextMeshProUGUI modeText;

    [Header("Botones")]
    public Button toggleJoystickButton;
    public Button recalibrateButton;
    public Button permissionGrantButton;  // En iOS requiere gesto del usuario

    [Header("Referencia")]
    public ARCameraController cameraController;

    // ── Internos ─────────────────────────────────────────────────────────────
    private bool _joystickActive = false;
    private float _statusUpdateTimer = 0f;
    private const float STATUS_UPDATE_INTERVAL = 0.5f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Start()
    {
        // Botones
        if (toggleJoystickButton != null)
            toggleJoystickButton.onClick.AddListener(OnToggleJoystick);

        if (recalibrateButton != null)
            recalibrateButton.onClick.AddListener(OnRecalibrate);

        if (permissionGrantButton != null)
            permissionGrantButton.onClick.AddListener(OnPermissionGrant);

        // Estado inicial
        UpdateModeText();
    }

    private void Update()
    {
        _statusUpdateTimer += Time.deltaTime;
        if (_statusUpdateTimer >= STATUS_UPDATE_INTERVAL)
        {
            _statusUpdateTimer = 0f;
            UpdateStatusDisplay();
        }

        // Mostrar joystick si GPS no está disponible
        AutoActivateJoystickIfNeeded();
    }

    // ── Actualización de estado ───────────────────────────────────────────────
    private void UpdateStatusDisplay()
    {
        // GPS
        if (gpsStatusText != null && GPSManager.Instance != null)
        {
            bool gpsOk = GPSManager.Instance.IsAvailable && GPSManager.Instance.HasOrigin;
            gpsStatusText.text = gpsOk
                ? $"<color=#00FF88>GPS ✓</color>"
                : GPSManager.Instance.IsAvailable
                    ? "<color=#FFAA00>GPS esperando señal...</color>"
                    : "<color=#FF4444>GPS no disponible</color>";
        }

        // Giroscopio
        if (gyroStatusText != null && GyroscopeManager.Instance != null)
        {
            gyroStatusText.text = GyroscopeManager.Instance.IsAvailable
                ? "<color=#00FF88>Giroscopio ✓</color>"
                : "<color=#FF4444>Giroscopio no disponible</color>";
        }

        // Desplazamiento
        if (displacementText != null && GPSManager.Instance != null && GPSManager.Instance.HasOrigin)
        {
            Vector2 d = GPSManager.Instance.DisplacementMeters;
            displacementText.text = $"Δ E:{d.x:+0.0;-0.0}m  N:{d.y:+0.0;-0.0}m";
        }
        else if (displacementText != null)
        {
            displacementText.text = "";
        }
    }

    private void AutoActivateJoystickIfNeeded()
    {
        // Si el GPS falló y el joystick no está activo, activarlo automáticamente
        if (GPSManager.Instance != null && !GPSManager.Instance.IsAvailable && !_joystickActive)
        {
            ActivateJoystick(true);
        }
    }

    // ── Botones ───────────────────────────────────────────────────────────────
    private void OnToggleJoystick()
    {
        ActivateJoystick(!_joystickActive);
    }

    private void ActivateJoystick(bool active)
    {
        _joystickActive = active;

        if (joystickPanel != null)
            joystickPanel.SetActive(active);

        if (cameraController != null)
            cameraController.SetForceJoystick(active);

        UpdateModeText();
        UpdateToggleButtonLabel();
    }

    private void OnRecalibrate()
    {
        if (cameraController != null)
            cameraController.Recalibrate();
    }

    private void OnPermissionGrant()
    {
        // En iOS 13+ se necesita un gesto del usuario para pedir permiso de giroscopio
        // Este botón dispara la solicitud via JS
        RequestGyroPermissionJS();
        if (permissionPanel != null)
            permissionPanel.SetActive(false);
    }

    private void UpdateModeText()
    {
        if (modeText == null) return;
        modeText.text = _joystickActive ? "Modo: Joystick" : "Modo: GPS";
    }

    private void UpdateToggleButtonLabel()
    {
        if (toggleJoystickButton == null) return;
        var label = toggleJoystickButton.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
            label.text = _joystickActive ? "Usar GPS" : "Usar Joystick";
    }

    // ── Notificación desde managers ──────────────────────────────────────────
    /// Llamado cuando se detecta que el GPS necesita permiso (desde JS)
    public void ShowPermissionPanel()
    {
        if (permissionPanel != null)
            permissionPanel.SetActive(true);
    }

    // ── JS Interop ────────────────────────────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void RequestDeviceOrientationPermission();
#else
    private static void RequestDeviceOrientationPermission() { }
#endif

    private void RequestGyroPermissionJS()
    {
        RequestDeviceOrientationPermission();
    }
}