using UnityEngine;

/// <summary>
/// ARCameraController: Controlador principal de la cámara AR.
///
/// RESPONSABILIDADES:
///   1. Aplicar rotación del giroscopio a la cámara (siempre activo).
///   2. Traducir desplazamiento GPS a posición en el mundo Unity.
///   3. Cuando GPS no está disponible o el usuario prefiere joystick,
///      usar el JoystickController para el movimiento.
///
/// IMPORTANTE - Sin gimbal lock ni salto de ángulos:
///   Toda la rotación se hace con Quaternion.Slerp sobre el quaternion
///   del giroscopio. NUNCA se usan euler angles directamente en la cámara.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ARCameraController : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────
    [Header("Referencias")]
    [Tooltip("Joystick UI (se activa si GPS falla o usuario lo prefiere)")]
    public JoystickController joystickController;

    [Tooltip("Objeto 3D que aparece 1 metro al frente al inicio")]
    public GameObject arObject;

    [Header("Movimiento GPS")]
    [Tooltip("Escala de metros GPS → unidades Unity. 1:1 por defecto.")]
    public float gpsToUnityScale = 1f;

    [Header("Movimiento Joystick")]
    [Tooltip("Velocidad de desplazamiento con joystick (unidades/segundo)")]
    public float joystickSpeed = 3f;

    [Header("Modo de control")]
    [Tooltip("Fuerza el uso del joystick aunque el GPS esté disponible")]
    public bool forceJoystick = false;

    // ── Internos ─────────────────────────────────────────────────────────────
    private Vector3 _originPosition;       // posición en Unity cuando se establece origen GPS
    private bool    _arObjectPlaced = false;
    private Camera  _camera;

    // ── Unity Lifecycle ──────────────────────────────────────────────────────
    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _originPosition = transform.position;
    }

    private void Start()
    {
        // Colocar objeto AR 1 metro adelante de la cámara inicial
        PlaceARObject();
    }

    private void LateUpdate()
    {
        ApplyGyroscopeRotation();
        ApplyMovement();
    }

    // ── Rotación (Giroscopio) ─────────────────────────────────────────────────
    /// Aplica la rotación del GyroscopeManager a la cámara.
    /// Usa el Quaternion ya suavizado del manager → sin saltos de ángulo.
    private void ApplyGyroscopeRotation()
    {
        if (GyroscopeManager.Instance == null) return;

        if (GyroscopeManager.Instance.IsAvailable)
        {
            transform.rotation = GyroscopeManager.Instance.DeviceRotation;
        }
        // Si no hay giroscopio, la rotación queda en la última posición conocida
        // (el usuario puede rotar con el mouse en editor para debug)
    }

    // ── Movimiento ────────────────────────────────────────────────────────────
    private void ApplyMovement()
    {
        bool useGPS = GPSManager.Instance != null &&
                      GPSManager.Instance.IsAvailable &&
                      GPSManager.Instance.HasOrigin &&
                      !forceJoystick;

        if (useGPS)
        {
            ApplyGPSMovement();
        }
        else
        {
            ApplyJoystickMovement();
        }
    }

    /// Traduce el desplazamiento GPS (metros) a posición Unity.
    /// Solo afecta X y Z; Y permanece constante (no subimos ni bajamos con GPS).
    private void ApplyGPSMovement()
    {
        Vector2 disp = GPSManager.Instance.DisplacementMeters * gpsToUnityScale;
        Vector3 newPos = new Vector3(
            _originPosition.x + disp.x,  // Este  → X Unity
            transform.position.y,          // Y constante
            _originPosition.z + disp.y    // Norte → Z Unity
        );
        transform.position = Vector3.Lerp(transform.position, newPos, Time.deltaTime * 5f);
    }

    /// Movimiento con joystick en el plano horizontal.
    /// La dirección se calcula respecto al yaw actual de la cámara
    /// para que "adelante" siempre sea la dirección a la que mira el jugador.
    private void ApplyJoystickMovement()
    {
        if (joystickController == null) return;

        Vector2 input = joystickController.InputDirection;
        if (input.sqrMagnitude < 0.01f) return;

        // Extraer sólo el yaw de la cámara (ignorar pitch/roll) para el movimiento
        float yaw = transform.eulerAngles.y;
        Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);

        // Movimiento en el plano XZ
        Vector3 moveDir = yawOnly * new Vector3(input.x, 0f, input.y);
        transform.position += moveDir * (joystickSpeed * Time.deltaTime);
    }

    // ── Objeto AR ─────────────────────────────────────────────────────────────
    private void PlaceARObject()
    {
        if (arObject == null || _arObjectPlaced) return;

        // 1 metro frente a la cámara, mismo plano horizontal
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();

        arObject.transform.position = transform.position + forward * 1f;
        _arObjectPlaced = true;
        Debug.Log($"[AR] Objeto colocado en: {arObject.transform.position}");
    }

    // ── API Pública ───────────────────────────────────────────────────────────
    /// Llamado desde UIManager cuando el usuario activa/desactiva joystick
    public void SetForceJoystick(bool value)
    {
        forceJoystick = value;
        if (joystickController != null)
            joystickController.gameObject.SetActive(value || (GPSManager.Instance != null && !GPSManager.Instance.IsAvailable));
        Debug.Log($"[AR] Modo joystick: {value}");
    }

    /// Recalibra el origen GPS y reposiciona el objeto AR
    public void Recalibrate()
    {
        if (GPSManager.Instance != null)
            GPSManager.Instance.ResetOrigin();

        _originPosition = transform.position;
        _arObjectPlaced = false;
        PlaceARObject();
        Debug.Log("[AR] Recalibrado.");
    }

#if UNITY_EDITOR
    // Rotación con mouse en el editor para poder testear sin giroscopio
    private float _editorYaw   = 0f;
    private float _editorPitch = 0f;

    private void Update()
    {
        if (Input.GetMouseButton(1)) // Botón derecho
        {
            _editorYaw   += Input.GetAxis("Mouse X") * 3f;
            _editorPitch -= Input.GetAxis("Mouse Y") * 3f;
            _editorPitch  = Mathf.Clamp(_editorPitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(_editorPitch, _editorYaw, 0f);
        }
    }
#endif
}