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

    // Suavizado: 3 = muy suave/lento, 8 = directo/rápido
    // Ajustable en Inspector sin rebuild
    [Range(1f, 15f)] public float smoothSpeed = 8f;

    private Quaternion _target      = Quaternion.identity;
    private bool       _hasFirstRead = false;

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
        // Interpolación exponencial — framerate-independent, sin overshoot
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

            // Conversión W3C → Unity para teléfono vertical:
            // beta=90  → mirando al frente → pitch = 0
            // alpha    → yaw (brújula), negado porque W3C es CW, Unity CCW
            // gamma    → roll lateral
            float pitch = -(beta - 90f);
            float yaw   = -alpha;
            float roll  =  gamma;

            // Clamp para evitar flips en ángulos extremos
            pitch = Mathf.Clamp(pitch, -85f, 85f);
            roll  = Mathf.Clamp(roll,  -85f, 85f);

            _target = Quaternion.Euler(pitch, yaw, roll);

            if (!_hasFirstRead)
            {
                // Primera lectura: aplicar sin interpolación para evitar
                // el "vuelo" inicial desde Quaternion.identity
                DeviceRotation = _target;
                _hasFirstRead  = true;
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