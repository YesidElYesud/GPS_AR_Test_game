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

    [Tooltip("Velocidad de suavizado del giroscopio. Valores bajos = más suave (menos ruido). " +
             "Android ruidoso: 5–7. iOS / PC: 8–12.")]
    [Range(1f, 20f)] public float smoothSpeed = 6f;

    [Tooltip("Elimina el roll (inclinación lateral del teléfono) de la rotación de cámara.\n" +
             "Corrige el efecto de 'giro lateral' al mirar hacia abajo en Android.\n" +
             "Recomendado: activado para navegación AR en portrait.")]
    [SerializeField] private bool suppressRoll = true;

    private Quaternion _target       = Quaternion.identity;
    private bool       _hasFirstRead = false;
    private float      _lastYaw      = 0f;   // yaw preservado cuando pitch → ±90°

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
        // IsAvailable se confirma solo cuando llega el primer dato real (OnGyroUpdate).
        // Evita falsos positivos en PC donde DeviceOrientationEvent existe pero no hay hardware.
        bool supported = Gyro_IsAvailable() == 1;
        if (supported) Gyro_StartListening();
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

                qUnity = Quaternion.Euler(-90f, 0f, 0f) * new Quaternion(qx, qy, -qz, qw);
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

            // ── Supresión de roll ────────────────────────────────────────────
            // Extrae yaw y pitch desde el vector forward para evitar que el
            // roll del dispositivo contamine la rotación lateral de la cámara
            // cuando el pitch se acerca a ±90° (mirar hacia abajo/arriba).
            if (suppressRoll)
            {
                Vector3 fwd     = _target * Vector3.forward;
                Vector3 flatFwd = new Vector3(fwd.x, 0f, fwd.z);
                float   flatLen = flatFwd.magnitude;

                // Pitch: ángulo entre el forward y el plano horizontal
                float pitch = Mathf.Atan2(-fwd.y, flatLen) * Mathf.Rad2Deg;

                // Yaw: dirección horizontal del forward; preservar si flatFwd ≈ 0
                float yaw;
                if (flatLen > 0.01f)
                {
                    yaw      = Mathf.Atan2(flatFwd.x, flatFwd.z) * Mathf.Rad2Deg;
                    _lastYaw = yaw;
                }
                else
                {
                    yaw = _lastYaw;   // mirando casi recto arriba/abajo: no hay yaw definido
                }

                _target = Quaternion.Euler(pitch, yaw, 0f);
            }

            if (!_hasFirstRead)
            {
                DeviceRotation = _target;
                _hasFirstRead  = true;
                IsAvailable    = true; // confirmar solo cuando hay datos reales del sensor
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