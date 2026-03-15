using UnityEngine;

/// <summary>
/// ARCameraController v2
/// - Si joystickController no está asignado en el editor, lo busca en la escena.
/// - forceJoystick = true desde el editor para forzar modo joystick sin GPS.
/// - En editor: clic derecho del mouse para rotar la cámara.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ARCameraController : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Arrastra aquí el JoystickPanel desde la Hierarchy")]
    public JoystickController joystickController;

    [Tooltip("Objeto AR que se coloca frente a la cámara al inicio")]
    public GameObject arObject;

    [Header("GPS (opcional)")]
    [Tooltip("Escala de metros GPS a unidades Unity")]
    public float gpsToUnityScale = 1f;

    [Header("Joystick")]
    [Tooltip("Velocidad de movimiento con joystick (unidades/segundo)")]
    public float joystickSpeed = 5f;

    [Header("Modo")]
    [Tooltip("Forzar modo joystick aunque haya GPS. Útil para pruebas en editor.")]
    public bool forceJoystick = false;

    // ── Internos ──────────────────────────────────────────────────────────────
    private Camera  _camera;
    private Vector3 _cameraOrigin;
    private bool    _arObjectPlaced = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _camera.clearFlags  = CameraClearFlags.Skybox;
        _camera.fieldOfView = 60f;
        _camera.depth       = 0;
        _cameraOrigin       = transform.position;

        // Auto-conectar joystick si no se asignó en el editor
        if (joystickController == null)
        {
            joystickController = FindObjectOfType<JoystickController>(true); // incluye inactivos
            if (joystickController != null)
                Debug.Log("[AR] JoystickController encontrado automáticamente.");
            else
                Debug.LogWarning("[AR] JoystickController no encontrado. Muévelo manualmente al campo en el Inspector.");
        }
    }

    private void Start()
    {
        PlaceARObject();
    }

    private void LateUpdate()
    {
        ApplyRotation();
        ApplyMovement();
    }

    // ── Rotación: giroscopio en dispositivo, mouse en editor ──────────────────
    private void ApplyRotation()
    {
        if (GyroscopeManager.Instance == null || !GyroscopeManager.Instance.IsAvailable) return;
        transform.rotation = GyroscopeManager.Instance.DeviceRotation;
    }

    // ── Movimiento ────────────────────────────────────────────────────────────
    private void ApplyMovement()
    {
        bool gpsOk = !forceJoystick
                  && GPSManager.Instance != null
                  && GPSManager.Instance.IsAvailable
                  && GPSManager.Instance.HasOrigin;

        if (gpsOk) MoveCameraByGPS();
        else        MoveCameraByJoystick();
    }

    private void MoveCameraByGPS()
    {
        Vector2 disp   = GPSManager.Instance.DisplacementMeters * gpsToUnityScale;
        Vector3 target = new Vector3(
            _cameraOrigin.x + disp.x,
            transform.position.y,
            _cameraOrigin.z + disp.y);
        transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * 2f);
    }

    private void MoveCameraByJoystick()
    {
        if (joystickController == null) return;

        Vector2 input = joystickController.InputDirection;
        if (input.sqrMagnitude < 0.01f) return;

        // Dirección basada en el yaw actual de la cámara (ignora pitch/roll)
        float   yaw     = transform.eulerAngles.y;
        float   rad     = yaw * Mathf.Deg2Rad;
        Vector3 forward = new Vector3( Mathf.Sin(rad), 0f,  Mathf.Cos(rad));
        Vector3 right   = new Vector3( Mathf.Cos(rad), 0f, -Mathf.Sin(rad));

        transform.position += (forward * input.y + right * input.x)
                            * joystickSpeed * Time.deltaTime;
    }

    // ── Objeto AR ─────────────────────────────────────────────────────────────
    private void PlaceARObject()
    {
        if (arObject == null || _arObjectPlaced) return;
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        arObject.transform.position = transform.position + fwd.normalized * 20f;
        _arObjectPlaced = true;
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>Activa o desactiva el modo joystick manualmente.</summary>
    public void SetForceJoystick(bool value)
    {
        forceJoystick = value;
        // No tocamos el panel aquí — el UIManager lo controla
    }

    /// <summary>Recalibra GPS, giroscopio y reposiciona el objeto AR.</summary>
    public void Recalibrate()
    {
        if (GPSManager.Instance     != null) GPSManager.Instance.ResetOrigin();
        if (GyroscopeManager.Instance != null) GyroscopeManager.Instance.Recalibrate();
        _cameraOrigin   = transform.position;
        _arObjectPlaced = false;
        PlaceARObject();
        Debug.Log("[AR] Recalibrado.");
    }

    // ── Editor: rotar con botón derecho del mouse ─────────────────────────────
#if UNITY_EDITOR
    private float _eYaw, _ePitch;
    private void Update()
    {
        if (!Input.GetMouseButton(1)) return;
        _eYaw   += Input.GetAxis("Mouse X") * 3f;
        _ePitch -= Input.GetAxis("Mouse Y") * 3f;
        _ePitch  = Mathf.Clamp(_ePitch, -89f, 89f);
        transform.rotation = Quaternion.Euler(_ePitch, _eYaw, 0f);
    }
#endif
}