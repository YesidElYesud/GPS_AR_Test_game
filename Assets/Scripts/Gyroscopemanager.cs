using System.Runtime.InteropServices;
using UnityEngine;

public class GyroscopeManager : MonoBehaviour
{
    public static GyroscopeManager Instance { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void Gyro_StartListening();
    [DllImport("__Internal")] private static extern void Gyro_StopListening();
    [DllImport("__Internal")] private static extern int  Gyro_IsAvailable();
#else
    private static void Gyro_StartListening() { }
    private static void Gyro_StopListening()  { }
    private static int  Gyro_IsAvailable()    => 0;
#endif

    public bool       IsAvailable    { get; private set; }
    public Quaternion DeviceRotation { get; private set; } = Quaternion.identity;

    [Range(1f, 15f)] public float smoothSpeed = 5f;

    private Quaternion _target      = Quaternion.identity;
    private bool _hasFirstRead      = false;
    private float _lastAlpha        = -1f;
    private int _stableFrames       = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        IsAvailable = Gyro_IsAvailable() == 1;
        if (IsAvailable) Gyro_StartListening();
        else Debug.LogWarning("[Gyro] No disponible.");
    }

    private void Update()
    {
        float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        DeviceRotation = Quaternion.Slerp(DeviceRotation, _target, t);
    }

    private void OnDestroy() { if (IsAvailable) Gyro_StopListening(); }

    public void OnGyroUpdate(string data)
    {
        try
        {
            string[] p = data.Split(',');
            if (p.Length < 3) return;

            float alpha = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
            float beta  = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
            float gamma = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);

            // Detectar si el sensor está mandando basura (alpha siempre 0 = sin brújula)
            // En ese caso ignoramos alpha y solo usamos beta/gamma
            bool alphaValid = !(alpha == 0f && _lastAlpha == 0f && _stableFrames > 5);
            _stableFrames = (alpha == _lastAlpha) ? _stableFrames + 1 : 0;
            _lastAlpha = alpha;

            float yaw = alphaValid ? -alpha : 0f;

            // Conversión W3C → Unity para teléfono vertical:
            // beta=90  → mirando al frente (pitch=0 en Unity)
            // beta=0   → mirando al techo  (pitch=-90 en Unity)  
            // beta=180 → mirando al suelo  (pitch=90 en Unity)
            // gamma=0  → vertical (roll=0)
            float pitch = -(beta - 90f);
            float roll  = gamma;

            // Clamp para evitar valores extremos que causen flips
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            roll  = Mathf.Clamp(roll,  -89f, 89f);

            _target = Quaternion.Euler(pitch, yaw, roll);

            if (!_hasFirstRead)
            {
                DeviceRotation = _target;
                _hasFirstRead  = true;
                Debug.Log($"[Gyro] Primera lectura: a={alpha:F1} b={beta:F1} g={gamma:F1}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Gyro] Error: " + e.Message);
        }
    }

    public void OnGyroError(string msg)
    {
        Debug.Log("[Gyro] " + msg);
        if (msg == "PermissionGranted") { IsAvailable = true; Gyro_StartListening(); }
    }
}