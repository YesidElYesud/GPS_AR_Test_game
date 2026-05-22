using UnityEngine;

/// <summary>
/// ARCameraController v3
/// - Si joystickController no está asignado en el editor, lo busca en la escena.
/// - forceJoystick = true desde el editor para forzar modo joystick sin GPS.
/// - En editor: clic derecho del mouse para rotar la cámara.
/// - SetInputBlocked(true) congela rotación y movimiento (usado por StageManager).
/// </summary>
[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(CharacterController))]
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

    [Header("Teclado PC (WASD)")]
    [Tooltip("Velocidad de movimiento con teclado WASD (unidades/segundo)")]
    public float wasdSpeed = 5f;

    [Tooltip("Sensibilidad del mouse para rotar cámara en PC (cuando no hay giroscopio)")]
    public float mouseLookSensitivity = 3f;

    [Header("Modo")]
    [Tooltip("Forzar modo joystick aunque haya GPS. Útil para pruebas en editor.")]
    public bool forceJoystick = false;

    [Header("Física / Terreno")]
    [Tooltip("Gravedad aplicada al jugador (valor negativo). -20 es más responsivo que -9.8.")]
    public float gravity = -20f;

    // ── Internos ──────────────────────────────────────────────────────────────
    private Camera            _camera;
    private CharacterController _cc;
    private Vector3           _cameraOrigin;
    private bool              _arObjectPlaced   = false;
    private bool              _inputBlocked     = false;
    private bool              _aerialMode       = false;
    private float             _mlYaw, _mlPitch; // mouse look acumulado
    private float             _verticalVelocity = 0f;

    // ── Giro suave hacia NPC ──────────────────────────────────────────────────
    [Header("Giro suave al interactuar con NPC")]
    [Tooltip("Velocidad (°/s) del giro hacia el NPC al iniciar un diálogo.")]
    [SerializeField] private float npcLookSpeed = 120f;
    [Tooltip("Ángulo máximo (°) que la cámara se desviará para centrar al NPC. " +
             "Evita giros bruscos cuando el NPC está muy de lado.")]
    [SerializeField] private float npcLookMaxAngle = 50f;

    private float _yawNudge       = 0f; // offset activo (world-space °)
    private float _yawNudgeTarget = 0f; // offset destino

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _cc     = GetComponent<CharacterController>();
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

    // ── Rotación: giroscopio en dispositivo, mouse look en PC ─────────────────
    private void ApplyRotation()
    {
        if (_inputBlocked || _aerialMode) return;

        // Animar el offset de yaw hacia el NPC (o de vuelta a 0)
        _yawNudge = Mathf.MoveTowardsAngle(_yawNudge, _yawNudgeTarget, npcLookSpeed * Time.deltaTime);
        Quaternion nudgeRot = Quaternion.AngleAxis(_yawNudge, Vector3.up);

        bool gyroOk = GyroscopeManager.Instance != null && GyroscopeManager.Instance.IsAvailable;
        if (gyroOk)
        {
            transform.rotation = nudgeRot * GyroscopeManager.Instance.DeviceRotation;
            return;
        }

        // Sin giroscopio (PC): mouse look con botón derecho o arrastre
        if (Input.GetMouseButton(1) || Input.GetMouseButton(0) && !IsPointerOverUI())
        {
            _mlYaw   += Input.GetAxis("Mouse X") * mouseLookSensitivity;
            _mlPitch -= Input.GetAxis("Mouse Y") * mouseLookSensitivity;
            _mlPitch  = Mathf.Clamp(_mlPitch, -89f, 89f);
            transform.rotation = nudgeRot * Quaternion.Euler(_mlPitch, _mlYaw, 0f);
        }
    }

    // ── API: giro suave hacia un yaw mundo ───────────────────────────────────
    /// <summary>
    /// Gira suavemente la cámara para centrar el punto que está en worldYaw (grados, espacio mundo).
    /// El giro está limitado a npcLookMaxAngle para evitar desorientar al jugador.
    /// </summary>
    public void LookTowardWorldYaw(float worldYaw)
    {
        // Rotación base sin el nudge (giroscopio o mouse-look)
        Quaternion baseRot = (GyroscopeManager.Instance != null && GyroscopeManager.Instance.IsAvailable)
            ? GyroscopeManager.Instance.DeviceRotation
            : Quaternion.Euler(_mlPitch, _mlYaw, 0f);

        Vector3 baseFwd = baseRot * Vector3.forward;
        float   baseYaw = Mathf.Atan2(baseFwd.x, baseFwd.z) * Mathf.Rad2Deg;

        float delta = Mathf.DeltaAngle(baseYaw + _yawNudge, worldYaw);
        _yawNudgeTarget = Mathf.Clamp(_yawNudge + delta, -npcLookMaxAngle, npcLookMaxAngle);
    }

    /// <summary>Devuelve la cámara suavemente a su orientación natural (sin offset NPC).</summary>
    public void ResetYawNudge() => _yawNudgeTarget = 0f;

    private bool IsPointerOverUI()
    {
        return UnityEngine.EventSystems.EventSystem.current != null
            && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    }

    // ── Movimiento ────────────────────────────────────────────────────────────
    private void ApplyMovement()
    {
        if (_aerialMode) return; // AerialViewController controla posición directamente

        bool gpsOk = !forceJoystick
                  && GPSManager.Instance != null
                  && GPSManager.Instance.IsAvailable
                  && GPSManager.Instance.HasOrigin;

        if (_inputBlocked)
        {
            // Sin input horizontal pero con gravedad activa para no flotar
            ApplyGravityAndMove(Vector3.zero);
            return;
        }

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

        // Delta horizontal hacia el target GPS (la Y la maneja la gravedad)
        Vector3 delta = Vector3.Lerp(transform.position, target, Time.deltaTime * 2f)
                      - transform.position;
        delta.y = 0f;

        ApplyGravityAndMove(delta);
    }

    private void MoveCameraByJoystick()
    {
        // Joystick táctil
        Vector2 joystickInput = joystickController != null
            ? joystickController.InputDirection
            : Vector2.zero;

        // Teclado WASD / flechas (funciona en editor y WebGL)
        Vector2 wasdInput = new Vector2(
            Input.GetAxis("Horizontal"),
            Input.GetAxis("Vertical"));

        // Combinar y limitar magnitud a 1 para que no se sumen velocidades
        Vector2 input = Vector2.ClampMagnitude(joystickInput + wasdInput, 1f);

        // Velocidad: joystick si viene del táctil, wasd si viene del teclado
        float speed = joystickInput.sqrMagnitude > wasdInput.sqrMagnitude
            ? joystickSpeed
            : wasdSpeed;

        // Dirección basada en el yaw actual de la cámara (ignora pitch/roll)
        float   yaw     = transform.eulerAngles.y;
        float   rad     = yaw * Mathf.Deg2Rad;
        Vector3 forward = new Vector3( Mathf.Sin(rad), 0f,  Mathf.Cos(rad));
        Vector3 right   = new Vector3( Mathf.Cos(rad), 0f, -Mathf.Sin(rad));

        Vector3 horizontal = (forward * input.y + right * input.x) * speed * Time.deltaTime;
        ApplyGravityAndMove(horizontal);
    }

    /// <summary>
    /// Aplica el movimiento horizontal más gravedad acumulada usando CharacterController.
    /// Maneja pendientes y desniveles automáticamente.
    /// </summary>
    private void ApplyGravityAndMove(Vector3 horizontalDelta)
    {
        if (_cc == null) return;

        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;   // pequeña fuerza constante para mantenerse anclado al suelo
        else
            _verticalVelocity += gravity * Time.deltaTime;

        horizontalDelta.y = _verticalVelocity * Time.deltaTime;
        _cc.Move(horizontalDelta);
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

    /// <summary>
    /// Activa o desactiva el modo aéreo: cede el control de posición/rotación
    /// al AerialViewController y desactiva gravedad y CharacterController.
    /// </summary>
    public void SetAerialMode(bool value)
    {
        _aerialMode = value;
        if (_cc != null) _cc.enabled = !value;
    }

    /// <summary>
    /// Congela o descongela toda la entrada del jugador (rotación + movimiento).
    /// Llamado por StageManager al mostrar panels, cinemáticas o diálogos.
    /// </summary>
    public void SetInputBlocked(bool blocked)
    {
        _inputBlocked = blocked;
    }

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

}