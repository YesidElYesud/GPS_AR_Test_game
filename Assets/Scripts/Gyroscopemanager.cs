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

    // Offset de calibración automática: se fija en la primera lectura para que
    // "donde apunta el teléfono al inicio" sea el frente de la escena.
    private Quaternion _calibrationOffset = Quaternion.identity;
    private bool       _calibrated        = false;

    // Offset manual (Euler) aplicado encima de la calibración automática.
    // Permite corregir desviaciones de Pitch / Yaw / Roll por dispositivo.
    // Persiste en PlayerPrefs entre sesiones.
    private Vector3 _eulerOffset = Vector3.zero;

    private const string PREF_PITCH = "Gyro_PitchOffset";
    private const string PREF_YAW   = "Gyro_YawOffset";
    private const string PREF_ROLL  = "Gyro_RollOffset";

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

        LoadSavedOffset();
    }

    private void Update()
    {
        float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        DeviceRotation = Quaternion.Slerp(DeviceRotation, _target, t);
    }

    private void OnDestroy() { if (IsAvailable) Gyro_StopListening(); }

    // ── Offset manual ─────────────────────────────────────────────────────────
    public Vector3 GetEulerOffset() => _eulerOffset;

    public void SetOffset(float pitch, float roll, float yaw)
    {
        _eulerOffset = new Vector3(pitch, yaw, roll);
        PlayerPrefs.SetFloat(PREF_PITCH, pitch);
        PlayerPrefs.SetFloat(PREF_YAW,   yaw);
        PlayerPrefs.SetFloat(PREF_ROLL,  roll);
        PlayerPrefs.Save();
    }

    private void LoadSavedOffset()
    {
        float pitch = PlayerPrefs.GetFloat(PREF_PITCH, 0f);
        float yaw   = PlayerPrefs.GetFloat(PREF_YAW,   0f);
        float roll  = PlayerPrefs.GetFloat(PREF_ROLL,  0f);
        _eulerOffset = new Vector3(pitch, yaw, roll);
    }

    public void Recalibrate()
    {
        _calibrated = false;
        Debug.Log("[Gyro] Recalibrado.");
    }

    public void OnGyroUpdate(string data)
    {
        try
        {
            Quaternion qUnity;

            if (data.StartsWith("Q:"))
            {
                // ── Ruta A: AbsoluteOrientationSensor (Android Chrome) ─────────
                // Recibe quaternion crudo [x,y,z,w] en espacio ENU (device→earth).
                // Conversión directa sin pasar por Euler; elimina el gimbal lock
                // que ocurría cuando beta ≈ 90° (teléfono apuntando al frente).
                //
                // Derivación validada:
                //   - Teléfono en portrait apuntando Norte → sensor q ≈ (0.707,0,0,0.707)
                //   - Euler(-90,0,0) * (0.707,0,0,0.707) = identity ✓ (cámara al frente)
                //   - Teléfono apuntando Este → resultado = (0,0.707,0,0.707)
                //     = 90° rotación Y en Unity = cámara mirando al Este ✓
                string[] p = data.Substring(2).Split(',');
                if (p.Length < 4) return;

                float qx = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
                float qy = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                float qz = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);
                float qw = float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture);

                qUnity = Quaternion.Euler(-90f, 0f, 0f) * new Quaternion(qx, qy, qz, qw);
            }
            else
            {
                // ── Ruta B: DeviceOrientationEvent fallback (iOS Safari / PC) ──
                // W3C define rotaciones intrínsecas ZXY:
                //   alpha = compass heading (0=Norte, 90=Este)
                //   beta  = 90° con pantalla mirando al frente
                //   gamma = 0° sin inclinar lateralmente
                string[] p = data.Split(',');
                if (p.Length < 3) return;

                float alpha = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
                float beta  = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                float gamma = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);

                Quaternion qAlpha = Quaternion.AngleAxis(-alpha, Vector3.forward); // Z
                Quaternion qBeta  = Quaternion.AngleAxis(-beta,  Vector3.right);   // X
                Quaternion qGamma = Quaternion.AngleAxis( gamma, Vector3.up);      // Y
                qUnity = Quaternion.Euler(-90f, 0f, 0f) * (qAlpha * qBeta * qGamma);
            }

            // ── Calibración y offsets (compartido por ambas rutas) ────────────
            if (!_calibrated)
            {
                _calibrationOffset = Quaternion.Inverse(qUnity);
                _calibrated = true;
                Debug.Log($"[Gyro] Calibrado. Ruta: {(data.StartsWith("Q:") ? "quaternion" : "euler")}");
            }

            _target = qUnity * _calibrationOffset;

            if (_eulerOffset != Vector3.zero)
                _target = _target * Quaternion.Euler(_eulerOffset);

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