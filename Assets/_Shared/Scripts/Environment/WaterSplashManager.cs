using UnityEngine;

/// <summary>
/// Controlador global de todos los efectos de salpicadura de la escena.
/// Suscrito a StageManager.OnStageChanged para escalar la intensidad con cada etapa.
///
/// SETUP:
///   1. Agregar a cualquier GameObject (ej: Terrain_V2 o un vacío "WaterFX").
///   2. Asignar las referencias de StageManager (o dejar que las encuentre automáticamente).
///   3. Configurar stageIntensities[]: qué intensidad usar en cada etapa (Intro→Etapa5).
/// </summary>
public class WaterSplashManager : MonoBehaviour
{
    [Header("Intensidades por etapa (Intro → Etapa5)")]
    [Tooltip("6 entradas: Intro, Etapa1, Etapa2, Etapa3, Etapa4, Etapa5")]
    [SerializeField] private WaterSplashEffect.SplashIntensity[] stageIntensities =
    {
        WaterSplashEffect.SplashIntensity.Off,      // Intro
        WaterSplashEffect.SplashIntensity.Off,      // Etapa1  ← N1: día soleado, sin agua en suelo
        WaterSplashEffect.SplashIntensity.Light,    // Etapa2
        WaterSplashEffect.SplashIntensity.Normal,   // Etapa3
        WaterSplashEffect.SplashIntensity.Heavy,    // Etapa4
        WaterSplashEffect.SplashIntensity.Heavy,    // Etapa5
    };

    [Header("Dirección de flujo (compartida con WaterFlowController)")]
    [SerializeField] private Vector3 globalFlowDirection = Vector3.forward;

    // Cache de todos los efectos en escena
    private WaterSplashEffect[] _effects;

    // ──────────────────────────────────────────────────────────────────────
    void Start()
    {
        _effects = GetComponentsInChildren<WaterSplashEffect>(includeInactive: true);
        SetFlowDirection(globalFlowDirection);

        if (StageManager.Instance != null)
        {
            StageManager.Instance.OnStageChanged += OnStageChanged;

            int idx = (int)StageManager.Instance.CurrentStage;
            if (idx >= 0 && idx < stageIntensities.Length)
                SetIntensity(stageIntensities[idx]);
        }
        else
        {
            Debug.LogWarning("[WaterSplashManager] StageManager.Instance es null en Start. " +
                             "Las chispas no responderán a cambios de etapa.", this);
        }
    }

    void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;
    }

    // ─── Respuesta al cambio de etapa ─────────────────────────────────────

    private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
    {
        int idx = (int)current;
        if (idx < 0 || idx >= stageIntensities.Length) return;

        WaterSplashEffect.SplashIntensity target = stageIntensities[idx];
        SetIntensity(target);
    }

    // ─── API pública ──────────────────────────────────────────────────────

    /// <summary>Aplica la intensidad correspondiente a una etapa del StageManager (para preview).</summary>
    public void ApplyStageIntensity(StageManager.Stage stage)
    {
        int idx = (int)stage;
        if (idx < 0 || idx >= stageIntensities.Length) return;
        SetIntensity(stageIntensities[idx]);
    }

    public void SetIntensity(WaterSplashEffect.SplashIntensity intensity)
    {
        if (_effects == null) return;
        foreach (var e in _effects)
            if (e != null) e.SetIntensity(intensity);
    }

    public void SetFlowDirection(Vector3 dir)
    {
        globalFlowDirection = dir;
        if (_effects == null) return;
        foreach (var e in _effects)
            if (e != null) e.SetFlowDirection(dir);
    }

    /// <summary>
    /// Cambia el color de todas las salpicaduras.
    /// Útil para Etapa4 cuando el agua se vuelve marrón.
    /// </summary>
    public void SetColor(Color color)
    {
        if (_effects == null) return;
        foreach (var e in _effects)
            if (e != null) e.SetColor(color);
    }
}
