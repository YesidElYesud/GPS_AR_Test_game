using UnityEngine;

[RequireComponent(typeof(Camera))]
public class ARCameraController : MonoBehaviour
{
    [Header("Referencias")]
    public JoystickController joystickController;
    public GameObject arObject;

    [Header("GPS")]
    public float gpsToUnityScale = 1f;

    [Header("Joystick")]
    [Tooltip("Velocidad de desplazamiento en unidades/segundo")]
    public float joystickSpeed = 3f;

    [Header("Modo")]
    public bool forceJoystick = false;

    private Camera  _camera;
    private Vector3 _cameraOrigin;   // posicion inicial de la CAMARA
    private Vector3 _arObjectOrigin; // posicion inicial del objeto AR
    private bool    _arObjectPlaced = false;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.fieldOfView = 60f;
        _cameraOrigin = transform.position;
    }

    private void Start()
    {
        PlaceARObject();
    }

    private void LateUpdate()
    {
        // 1. Rotacion: SIEMPRE desde el giroscopio
        ApplyRotation();

        // 2. Traslacion: GPS mueve la camara, Joystick mueve la camara
        ApplyMovement();
    }

    private void ApplyRotation()
    {
        if (GyroscopeManager.Instance == null || !GyroscopeManager.Instance.IsAvailable) return;
        transform.rotation = GyroscopeManager.Instance.DeviceRotation;
    }

    private void ApplyMovement()
    {
        bool gpsDisponible = GPSManager.Instance != null
                          && GPSManager.Instance.IsAvailable
                          && GPSManager.Instance.HasOrigin;

        if (!forceJoystick && gpsDisponible)
            MoveCameraByGPS();
        else
            MoveCameraByJoystick();
    }

    // GPS mueve la CAMARA (el mundo se queda fijo, el jugador se mueve)
    private void MoveCameraByGPS()
    {
        Vector2 disp = GPSManager.Instance.DisplacementMeters * gpsToUnityScale;
        Vector3 target = new Vector3(
            _cameraOrigin.x + disp.x,
            transform.position.y,
            _cameraOrigin.z + disp.y
        );
        transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * 5f);
    }

    // Joystick mueve la CAMARA — efecto VR
    // La direccion es relativa al YAW actual de la camara
    // Mover el joystick adelante = avanzar hacia donde miras
    private void MoveCameraByJoystick()
    {
        if (joystickController == null) return;
        Vector2 input = joystickController.InputDirection;
        if (input.sqrMagnitude < 0.01f) return;

        // Extraer solo el yaw (ignorar pitch/roll para que el movimiento
        // sea siempre horizontal, como un FPS/VR)
        float yaw = transform.eulerAngles.y;
        Vector3 forward = new Vector3(
            Mathf.Sin(yaw * Mathf.Deg2Rad), 0f,
            Mathf.Cos(yaw * Mathf.Deg2Rad));
        Vector3 right = new Vector3(
            Mathf.Cos(yaw * Mathf.Deg2Rad), 0f,
            -Mathf.Sin(yaw * Mathf.Deg2Rad));

        // Mover la CAMARA, no el objeto
        transform.position += (forward * input.y + right * input.x)
                              * joystickSpeed * Time.deltaTime;
    }

    private void PlaceARObject()
    {
        if (arObject == null || _arObjectPlaced) return;
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        arObject.transform.position = transform.position + fwd.normalized * 2f;
        _arObjectOrigin = arObject.transform.position;
        _arObjectPlaced = true;
    }

    public void SetForceJoystick(bool value)
    {
        forceJoystick = value;
        if (joystickController != null)
            joystickController.gameObject.SetActive(value);
    }

    public void Recalibrate()
    {
        if (GPSManager.Instance != null) GPSManager.Instance.ResetOrigin();
        _cameraOrigin = transform.position;
        _arObjectPlaced = false;
        PlaceARObject();
    }

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