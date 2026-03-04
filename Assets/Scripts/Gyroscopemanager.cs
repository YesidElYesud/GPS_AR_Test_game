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

    [Range(1f, 20f)] public float smoothSpeed = 10f;

    private Quaternion _target       = Quaternion.identity;
    private bool       _hasFirstRead = false;

    // Offset de calibración: se fija en la primera lectura para que
    // "donde apunta el teléfono al inicio" sea el frente de la escena.
    private Quaternion _calibrationOffset = Quaternion.identity;
    private bool       _calibrated        = false;

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

    public void Recalibrate()
    {
        _calibrated = false;
        Debug.Log("[Gyro] Recalibrado.");
    }

    public void OnGyroUpdate(string data)
    {
        try
        {
            string[] p = data.Split(',');
            if (p.Length < 3) return;

            float alpha = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
            float beta  = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
            float gamma = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);

            // ── Conversión W3C DeviceOrientation → Unity ──────────────────────
            //
            // W3C define la orientación como rotaciones intrínsecas ZXY:
            //   1. Rotar alpha alrededor de Z (yaw en pantalla plana)
            //   2. Rotar beta  alrededor de X (pitch)
            //   3. Rotar gamma alrededor de Y (roll)
            //
            // Para un teléfono en portrait vertical mirando al frente:
            //   alpha = compass heading (0=Norte, 90=Este, CW desde arriba)
            //   beta  = 90° cuando la pantalla mira al frente horizontal
            //   gamma = 0° cuando está vertical sin inclinar
            //
            // Conversión a Unity (Y-up, Z-forward, mano derecha):
            //   - Construimos el quaternion desde los ángulos W3C directamente
            //   - Aplicamos corrección de ejes: W3C usa ENU, Unity usa Y-up Z-forward

            // Quaternion W3C en su sistema nativo
            Quaternion qAlpha = Quaternion.AngleAxis(-alpha,         Vector3.forward); // Z
            Quaternion qBeta  = Quaternion.AngleAxis(-beta,          Vector3.right);   // X
            Quaternion qGamma = Quaternion.AngleAxis( gamma,         Vector3.up);      // Y
            Quaternion qW3C   = qAlpha * qBeta * qGamma;

            // Corrección: rotar -90° en X para convertir de ENU a Unity
            Quaternion correction = Quaternion.Euler(-90f, 0f, 0f);
            Quaternion qUnity = correction * qW3C;

            // Calibración: en la primera lectura guardamos el offset inverso
            // para que la posición inicial del teléfono sea "mirando al frente"
            if (!_calibrated)
            {
                _calibrationOffset = Quaternion.Inverse(qUnity);
                _calibrated = true;
                Debug.Log($"[Gyro] Calibrado. a={alpha:F1} b={beta:F1} g={gamma:F1}");
            }

            // Aplicar offset de calibración
            _target = qUnity * _calibrationOffset;

            if (!_hasFirstRead)
            {
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
        if (msg == "PermissionGranted") { IsAvailable = true; Gyro_StartListening(); }
        else Debug.Log("[Gyro] " + msg);
    }
}