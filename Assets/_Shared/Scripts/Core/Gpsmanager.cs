using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// GPSManager: Recibe coordenadas GPS desde el navegador vía jslib.
/// Calcula desplazamiento en metros desde la posición inicial usando
/// la fórmula de Haversine. Thread-safe con callbacks desde JS.
/// </summary>
public class GPSManager : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────────
    public static GPSManager Instance { get; private set; }

    // ── Imports JS (WebGL) ───────────────────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void GPS_StartWatching();
    [DllImport("__Internal")] private static extern void GPS_StopWatching();
    [DllImport("__Internal")] private static extern bool GPS_IsAvailable();
#else
    // Stubs para editor / otras plataformas
    private static void GPS_StartWatching() { }
    private static void GPS_StopWatching() { }
    private static bool GPS_IsAvailable() => false;
#endif

    // ── Propiedades públicas ─────────────────────────────────────────────────
    public bool IsAvailable  { get; private set; }
    public bool HasOrigin    { get; private set; }

    /// Desplazamiento acumulado en metros desde el punto de inicio (X = Este, Z = Norte)
    public Vector2 DisplacementMeters { get; private set; }

    public double LatitudeOrigin  { get; private set; }
    public double LongitudeOrigin { get; private set; }

    // ── Internos ─────────────────────────────────────────────────────────────
    private double _lastLat;
    private double _lastLon;
    private const double EARTH_RADIUS = 6_371_000.0; // metros

    // ── Unity Lifecycle ──────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        IsAvailable = GPS_IsAvailable();
        if (IsAvailable)
        {
            Debug.Log("[GPS] Iniciando watch...");
            GPS_StartWatching();
        }
        else
        {
            Debug.LogWarning("[GPS] GPS no disponible en esta plataforma/navegador.");
        }
    }

    private void OnDestroy()
    {
        if (IsAvailable) GPS_StopWatching();
    }

    // ── Callback desde JavaScript ────────────────────────────────────────────
    /// Llamado por el jslib: "lat,lon,accuracy"
    public void OnGPSUpdate(string data)
    {
        try
        {
            string[] parts = data.Split(',');
            if (parts.Length < 2) return;

            double lat = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            double lon = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);

            if (!HasOrigin)
            {
                LatitudeOrigin  = lat;
                LongitudeOrigin = lon;
                _lastLat = lat;
                _lastLon = lon;
                HasOrigin = true;
                Debug.Log($"[GPS] Origen establecido: {lat:F7}, {lon:F7}");
                return;
            }

            // Haversine → metros
            double dLat = (lat - LatitudeOrigin) * Mathf.Deg2Rad;
            double dLon = (lon - LongitudeOrigin) * Mathf.Deg2Rad;
            double latRad = LatitudeOrigin * Mathf.Deg2Rad;

            double northMeters = dLat * EARTH_RADIUS;
            double eastMeters  = dLon * EARTH_RADIUS * Math.Cos(latRad);

            DisplacementMeters = new Vector2((float)eastMeters, (float)northMeters);
            _lastLat = lat;
            _lastLon = lon;
        }
        catch (Exception e)
        {
            Debug.LogError($"[GPS] Error parseando datos: {e.Message}");
        }
    }

    /// Llamado por jslib ante error de GPS
    public void OnGPSError(string errorMsg)
    {
        Debug.LogWarning($"[GPS] Error del navegador: {errorMsg}");
        IsAvailable = false;
    }

    // ── Utilidad ─────────────────────────────────────────────────────────────
    /// Resetea el origen (útil para recalibrar)
    public void ResetOrigin()
    {
        HasOrigin = false;
        DisplacementMeters = Vector2.zero;
        Debug.Log("[GPS] Origen reseteado.");
    }
}