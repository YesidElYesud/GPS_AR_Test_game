using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// GyroscopeManager: Lee la orientación del dispositivo desde el navegador.
/// Usa DeviceOrientationEvent (alpha/beta/gamma) y convierte a Quaternion
/// de Unity de forma robusta, evitando el gimbal lock y el salto 359→0.
///
/// CONVENCIÓN DE EJES:
///   alpha = rotación alrededor del eje Z del mundo (yaw / brújula)  0-360
///   beta  = rotación alrededor del eje X (pitch, frente/atrás)     -180..180
///   gamma = rotación alrededor del eje Y (roll, izquierda/derecha) -90..90
///
/// Orientación asumida: usuario mirando hacia adelante con el teléfono vertical.
/// </summary>
public class GyroscopeManager : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────────
    public static GyroscopeManager Instance { get; private set; }

    // ── Imports JS ──────────────────────────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void Gyro_StartListening();
    [DllImport("__Internal")] private static extern void Gyro_StopListening();
    [DllImport("__Internal")] private static extern bool Gyro_IsAvailable();
#else
    private static void Gyro_StartListening() { }
    private static void Gyro_StopListening()  { }
    private static bool Gyro_IsAvailable()   => false;
#endif

    // ── Propiedades públicas ─────────────────────────────────────────────────
    public bool IsAvailable { get; private set; }

    /// Quaternion listo para asignar a la cámara de Unity (suavizado)
    public Quaternion DeviceRotation { get; private set; } = Quaternion.identity;

    // ── Parámetros ───────────────────────────────────────────────────────────
    [Header("Suavizado")]
    [Tooltip("Velocidad de interpolación del quaternion. Más alto = más responsivo.")]
    [Range(1f, 30f)] public float smoothSpeed = 15f;

    // ── Internos ─────────────────────────────────────────────────────────────
    private Quaternion _rawTarget = Quaternion.identity;
    private bool _hasFirstReading = false;

    // ── Unity Lifecycle ──────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        IsAvailable = Gyro_IsAvailable();
        if (IsAvailable)
        {
            Debug.Log("[Gyro] Iniciando listener de orientación...");
            Gyro_StartListening();
        }
        else
        {
            Debug.LogWarning("[Gyro] DeviceOrientation no disponible.");
        }
    }

    private void Update()
    {
        // Slerp continuo para suavizar sin saltos
        DeviceRotation = Quaternion.Slerp(DeviceRotation, _rawTarget, Time.deltaTime * smoothSpeed);
    }

    private void OnDestroy()
    {
        if (IsAvailable) Gyro_StopListening();
    }

    // ── Callback desde JavaScript ────────────────────────────────────────────
    /// Llamado por jslib: "alpha,beta,gamma"
    /// alpha: 0-360 (yaw, brújula - N=0)
    /// beta:  -180..180 (pitch)
    /// gamma: -90..90 (roll)
    public void OnGyroUpdate(string data)
    {
        try
        {
            string[] parts = data.Split(',');
            if (parts.Length < 3) return;

            float alpha = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            float beta  = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            float gamma = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);

            // Convertir DeviceOrientation a Quaternion de Unity
            // Referencia: W3C DeviceOrientation → Unity Camera Space
            //
            // El dispositivo está vertical mirando hacia adelante:
            // - alpha  → yaw   en el plano horizontal (eje Y Unity)
            // - beta   → pitch hacia arriba/abajo      (eje X Unity, invertido)
            // - gamma  → roll  del teléfono            (eje Z Unity, afecta poco en modo vertical)
            //
            // Usamos multiplicación de Quaterniones para evitar gimbal lock.
            // Aplicamos la corrección de orientación "landscape/portrait" manualmente.

            // Paso 1: Construir quaternion desde ángulos de Euler del dispositivo
            // Orden de rotación WebAPI: Z(alpha) → X(beta) → Y(gamma)
            Quaternion qAlpha = Quaternion.AngleAxis(-alpha, Vector3.up);            // Yaw  (Y unity)
            Quaternion qBeta  = Quaternion.AngleAxis(beta - 90f, Vector3.right);    // Pitch (X unity) -90° porque beta=90 es "plano"
            Quaternion qGamma = Quaternion.AngleAxis(gamma, Vector3.forward);       // Roll  (Z unity)

            // Orden correcto para DeviceOrientation con pantalla portrait
            _rawTarget = qAlpha * qBeta * qGamma;

            if (!_hasFirstReading)
            {
                // Primer frame: no interpolar, asignar directo
                DeviceRotation = _rawTarget;
                _hasFirstReading = true;
                Debug.Log($"[Gyro] Primera lectura: α={alpha:F1} β={beta:F1} γ={gamma:F1}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Gyro] Error parseando datos: {e.Message}");
        }
    }

    public void OnGyroError(string errorMsg)
    {
        Debug.LogWarning($"[Gyro] Error: {errorMsg}");
        IsAvailable = false;
    }
}