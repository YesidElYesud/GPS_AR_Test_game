using UnityEngine;

[RequireComponent(typeof(Camera))]
public class ARCameraController : MonoBehaviour
{
    [Header("Referencias")]
    public JoystickController joystickController;
    public GameObject arObject;

    [Header("Movimiento GPS")]
    public float gpsToUnityScale = 1f;

    [Header("Movimiento Joystick")]
    public float joystickSpeed = 3f;

    [Header("Modo de control")]
    public bool forceJoystick = false;

    private Vector3 _originPosition;
    private bool    _arObjectPlaced = false;
    private Camera  _camera;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _originPosition = transform.position;

        // SolidColor con alpha=0: limpia el buffer cada frame (evita ghosting)
        // pero los pixeles son transparentes → se ve el video HTML debajo
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
    }

    private void Start() { PlaceARObject(); }

    private void LateUpdate()
    {
        ApplyGyroscopeRotation();
        ApplyMovement();
    }

    private void ApplyGyroscopeRotation()
    {
        if (GyroscopeManager.Instance == null || !GyroscopeManager.Instance.IsAvailable) return;
        transform.rotation = GyroscopeManager.Instance.DeviceRotation;
    }

    private void ApplyMovement()
    {
        bool useGPS = GPSManager.Instance != null &&
                      GPSManager.Instance.IsAvailable &&
                      GPSManager.Instance.HasOrigin &&
                      !forceJoystick;
        if (useGPS) ApplyGPSMovement();
        else        ApplyJoystickMovement();
    }

    private void ApplyGPSMovement()
    {
        Vector2 disp = GPSManager.Instance.DisplacementMeters * gpsToUnityScale;
        Vector3 newPos = new Vector3(
            _originPosition.x + disp.x,
            transform.position.y,
            _originPosition.z + disp.y
        );
        transform.position = Vector3.Lerp(transform.position, newPos, Time.deltaTime * 5f);
    }

    private void ApplyJoystickMovement()
    {
        if (joystickController == null) return;
        Vector2 input = joystickController.InputDirection;
        if (input.sqrMagnitude < 0.01f) return;
        float yaw = transform.eulerAngles.y;
        Vector3 moveDir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
        transform.position += moveDir * (joystickSpeed * Time.deltaTime);
    }

    private void PlaceARObject()
    {
        if (arObject == null || _arObjectPlaced) return;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();
        arObject.transform.position = transform.position + forward * 2f;
        _arObjectPlaced = true;
    }

    public void SetForceJoystick(bool value)
    {
        forceJoystick = value;
        if (joystickController != null)
            joystickController.gameObject.SetActive(
                value || (GPSManager.Instance != null && !GPSManager.Instance.IsAvailable));
    }

    public void Recalibrate()
    {
        if (GPSManager.Instance != null) GPSManager.Instance.ResetOrigin();
        _originPosition = transform.position;
        _arObjectPlaced = false;
        PlaceARObject();
    }

#if UNITY_EDITOR
    private float _editorYaw = 0f, _editorPitch = 0f;
    private void Update()
    {
        if (Input.GetMouseButton(1))
        {
            _editorYaw   += Input.GetAxis("Mouse X") * 3f;
            _editorPitch -= Input.GetAxis("Mouse Y") * 3f;
            _editorPitch  = Mathf.Clamp(_editorPitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(_editorPitch, _editorYaw, 0f);
        }
    }
#endif
}