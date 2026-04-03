using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// VisualEffectsStageController — Efectos visuales por etapa (Sistema 15).
/// Controla: partículas, skybox, iluminación ambiental y niebla.
///
/// Patrón idéntico a AudioStageManager:
///   - Singleton + DontDestroyOnLoad
///   - Se suscribe a StageManager.OnStageChanged
///   - Configuración serializada por etapa (6 entradas: Intro → Etapa5)
///
/// Setup en escena:
///   1. Crear GameObject vacío "VisualEffectsStageController" en la raíz.
///   2. Adjuntar este script.
///   3. Asignar stageConfigs con 6 entradas en el Inspector.
///   4. (Opcional) Arrastrar la Directional Light a sunLight.
/// </summary>
public class VisualEffectsStageController : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static VisualEffectsStageController Instance { get; private set; }

    // ── Datos visuales por etapa ──────────────────────────────────────────────
    [Serializable]
    public class StageVisualConfig
    {
        [Tooltip("Nombre descriptivo (solo visual en el Inspector)")]
        public string stageName;

        [Header("Skybox")]
        [Tooltip("Material de skybox para esta etapa. Null = mantener el actual.")]
        public Material skyboxMaterial;

        [Header("Iluminación Ambiental")]
        [Tooltip("Color de luz ambiental para esta etapa.")]
        public Color ambientColor = new Color(0.2f, 0.2f, 0.2f);

        [Tooltip("Intensidad de la luz ambiental (0–8).")]
        [Range(0f, 8f)]
        public float ambientIntensity = 1f;

        [Header("Sol (Directional Light)")]
        [Tooltip("Color del sol para esta etapa. Alpha ignorado.")]
        public Color sunColor = Color.white;

        [Tooltip("Intensidad del sol (0–2).")]
        [Range(0f, 2f)]
        public float sunIntensity = 1f;

        [Header("Niebla")]
        [Tooltip("Activar niebla en esta etapa.")]
        public bool enableFog = false;

        [Tooltip("Color de la niebla.")]
        public Color fogColor = new Color(0.5f, 0.5f, 0.5f);

        [Tooltip("Densidad de la niebla (modo Exponential).")]
        [Range(0f, 0.1f)]
        public float fogDensity = 0.02f;

        [Header("Partículas")]
        [Tooltip("Sistemas de partículas a ACTIVAR en esta etapa (p.ej. lluvia, chispas).")]
        public ParticleSystem[] particlesToPlay;

        [Tooltip("Sistemas de partículas a DETENER al entrar a esta etapa.")]
        public ParticleSystem[] particlesToStop;

        [Header("Transición")]
        [Tooltip("Duración del fade de iluminación y niebla al entrar a esta etapa (segundos).")]
        [Range(0f, 5f)]
        public float transitionDuration = 1.5f;
    }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Configuración por etapa")]
    [Tooltip("6 entradas: índice 0=Intro, 1=Etapa1 … 5=Etapa5.")]
    public StageVisualConfig[] stageConfigs = new StageVisualConfig[]
    {
        new StageVisualConfig { stageName = "Intro",  ambientIntensity = 1.0f, sunIntensity = 1.0f, transitionDuration = 1.0f },
        new StageVisualConfig { stageName = "Etapa1", ambientIntensity = 1.0f, sunIntensity = 1.0f, transitionDuration = 1.5f },
        new StageVisualConfig { stageName = "Etapa2", ambientIntensity = 0.8f, sunIntensity = 0.8f, enableFog = true,  fogDensity = 0.01f, transitionDuration = 2.0f },
        new StageVisualConfig { stageName = "Etapa3", ambientIntensity = 0.5f, sunIntensity = 0.5f, enableFog = true,  fogDensity = 0.03f, transitionDuration = 1.0f },
        new StageVisualConfig { stageName = "Etapa4", ambientIntensity = 0.3f, sunIntensity = 0.3f, enableFog = true,  fogDensity = 0.05f, transitionDuration = 0.8f },
        new StageVisualConfig { stageName = "Etapa5", ambientIntensity = 1.0f, sunIntensity = 1.0f, enableFog = false, transitionDuration = 2.5f },
    };

    [Header("Referencias")]
    [Tooltip("Directional Light de la escena. Se busca automáticamente si queda vacío.")]
    public Light sunLight;

    [Header("Debug")]
    public bool debugLogs = true;

    // ── Internos ──────────────────────────────────────────────────────────────
    private Coroutine _transitionRoutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (sunLight == null)
            sunLight = FindObjectOfType<Light>();

        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged += OnStageChanged;

        // Aplicar visuals de la etapa inicial sin transición
        if (StageManager.Instance != null)
            ApplyVisuals((int)StageManager.Instance.CurrentStage, fade: false);
    }

    private void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;
    }

    // ── Reacción al cambio de etapa ───────────────────────────────────────────
    private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
    {
        ApplyVisuals((int)current, fade: true);
    }

    // ── Lógica principal ──────────────────────────────────────────────────────
    private void ApplyVisuals(int stageIndex, bool fade)
    {
        if (stageConfigs == null || stageIndex >= stageConfigs.Length) return;

        StageVisualConfig config = stageConfigs[stageIndex];
        if (config == null) return;

        if (debugLogs)
            Debug.Log($"[VisualFX] Aplicando etapa {stageIndex}: {config.stageName}");

        // Skybox: cambio inmediato (no interpolable de forma simple)
        if (config.skyboxMaterial != null)
        {
            RenderSettings.skybox = config.skyboxMaterial;
            DynamicGI.UpdateEnvironment();
        }

        // Partículas: inmediatas
        ApplyParticles(config);

        // Iluminación y niebla: con o sin fade
        if (_transitionRoutine != null)
            StopCoroutine(_transitionRoutine);

        if (fade && config.transitionDuration > 0f)
            _transitionRoutine = StartCoroutine(TransitionRoutine(config));
        else
            ApplyImmediateValues(config);
    }

    private void ApplyParticles(StageVisualConfig config)
    {
        if (config.particlesToPlay != null)
        {
            foreach (var ps in config.particlesToPlay)
            {
                if (ps == null) continue;
                ps.gameObject.SetActive(true);
                if (!ps.isPlaying) ps.Play();
            }
        }

        if (config.particlesToStop != null)
        {
            foreach (var ps in config.particlesToStop)
            {
                if (ps == null) continue;
                ps.Stop();
                ps.gameObject.SetActive(false);
            }
        }
    }

    private void ApplyImmediateValues(StageVisualConfig config)
    {
        RenderSettings.ambientLight     = config.ambientColor * config.ambientIntensity;
        RenderSettings.fog              = config.enableFog;
        RenderSettings.fogColor         = config.fogColor;
        RenderSettings.fogDensity       = config.fogDensity;
        RenderSettings.fogMode          = FogMode.ExponentialSquared;

        if (sunLight != null)
        {
            sunLight.color     = config.sunColor;
            sunLight.intensity = config.sunIntensity;
        }
    }

    private IEnumerator TransitionRoutine(StageVisualConfig target)
    {
        float duration = target.transitionDuration;
        float elapsed  = 0f;

        // Capturar valores de inicio
        Color  startAmbient      = RenderSettings.ambientLight;
        Color  startFogColor     = RenderSettings.fogColor;
        float  startFogDensity   = RenderSettings.fogDensity;
        Color  startSunColor     = sunLight != null ? sunLight.color     : Color.white;
        float  startSunIntensity = sunLight != null ? sunLight.intensity : 1f;

        Color  endAmbient   = target.ambientColor * target.ambientIntensity;
        float  endFogDensity = target.enableFog ? target.fogDensity : 0f;

        // Activar la niebla inmediatamente si la etapa la requiere,
        // o desactivarla al final si no.
        if (target.enableFog)
            RenderSettings.fog = true;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

            RenderSettings.ambientLight = Color.Lerp(startAmbient,    endAmbient,          t);
            RenderSettings.fogColor     = Color.Lerp(startFogColor,    target.fogColor,     t);
            RenderSettings.fogDensity   = Mathf.Lerp(startFogDensity, endFogDensity,        t);

            if (sunLight != null)
            {
                sunLight.color     = Color.Lerp(startSunColor,     target.sunColor,     t);
                sunLight.intensity = Mathf.Lerp(startSunIntensity, target.sunIntensity, t);
            }

            yield return null;
        }

        // Asegurar valores finales exactos
        ApplyImmediateValues(target);

        // Desactivar niebla después del fade si la etapa no la usa
        if (!target.enableFog)
            RenderSettings.fog = false;

        _transitionRoutine = null;
    }
}
