using UnityEngine;

[RequireComponent(typeof(Camera))]
public class ARCameraController : MonoBehaviour
{
    [Header("Referencias")]
    public JoystickController joystickController;
    public GameObject arObject;

    [Header("GPS (opcional)")]
    public float gpsToUnityScale = 1f;

    [Header("Joystick")]
    public float joystickSpeed = 5f;

    [Header("Modo")]
    public bool forceJoystick = false;

    private Camera  _camera;
    private Vector3 _cameraOrigin;
    private bool    _arObjectPlaced = false;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        // Fondo negro sólido — sin transparencia, sin video
        _camera.clearFlags = CameraClearFlags.Skybox;
        _camera.fieldOfView = 60f;
        _camera.depth = 0;
        _cameraOrigin = transform.position;
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

    // ── Rotacion: siempre desde el giroscopio ─────────────────────────────────
    private void ApplyRotation()
    {
        if (GyroscopeManager.Instance == null || !GyroscopeManager.Instance.IsAvailable) return;
        transform.rotation = GyroscopeManager.Instance.DeviceRotation;
    }

    // ── Movimiento: GPS o Joystick ────────────────────────────────────────────
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
        Vector2 disp = GPSManager.Instance.DisplacementMeters * gpsToUnityScale;
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

        // Movimiento horizontal basado en el yaw actual
        float   yaw     = transform.eulerAngles.y;
        float   rad     = yaw * Mathf.Deg2Rad;
        Vector3 forward = new Vector3( Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        Vector3 right   = new Vector3( Mathf.Cos(rad), 0f,-Mathf.Sin(rad));

        transform.position += (forward * input.y + right * input.x)
                             * joystickSpeed * Time.deltaTime;
    }

    private void PlaceARObject()
    {
        if (arObject == null || _arObjectPlaced) return;
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        arObject.transform.position = transform.position + fwd.normalized * 20f;
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
        if (GyroscopeManager.Instance != null) GyroscopeManager.Instance.Recalibrate();
        _cameraOrigin = transform.position;
        _arObjectPlaced = false;
        PlaceARObject();
        Debug.Log("[AR] Recalibrado.");
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