using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GrassWindController — Sacudida de pasto según la etapa del evento.
///
/// Busca automáticamente todos los hijos de Terrain_V2 cuyo nombre comience
/// con "trasparent grass4" y les anima la rotación local con ondas seno
/// desfasadas (cada mata se mueve ligeramente diferente).
///
/// El pivote de cada mata se asume en la base del mesh (origen local Y=0).
/// Si el pivote está en el centro, el movimiento sigue siendo visualmente
/// aceptable pero la base flotará ligeramente — en ese caso usar el modo
/// ShaderWind (futuro) o ajustar pivotes en Blender.
///
/// Setup:
///   1. Adjuntar este script a cualquier GameObject de la escena (ej. Terrain_V2).
///   2. Asignar terrainRoot en el Inspector (arrastra Terrain_V2), o dejarlo
///      null para que lo busque por nombre automáticamente.
///   3. Play → el sistema se conecta a StageManager y responde a etapas.
///
/// Intensidades por etapa:
///   Intro / Etapa1 : brisa muy suave   (±1.2°)
///   Etapa2         : viento leve       (±3.0°)
///   Etapa3         : viento moderado   (±6.0°)
///   Etapa4         : viento fuerte     (±11.0°)
///   Etapa5         : viento residual   (±4.0°)
/// </summary>
public class GrassWindController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Tooltip("Raíz del terreno. Si es null se busca 'Terrain_V2' o 'Terrain_V4' en la escena.")]
    public Transform terrainRoot;

    [Tooltip("Prefijo de nombre de los objetos de pasto.")]
    public string grassNamePrefix = "trasparent grass4";

    [Tooltip("Duración del fundido al cambiar intensidad (seg).")]
    [Range(0f, 4f)]
    public float transitionDuration = 1.5f;

    // ── Configuración por etapa ────────────────────────────────────────────
    // (maxAngleDeg, swaySpeed, gustFrequency, gustAmplitudeMult)
    //   maxAngleDeg        : amplitud base del balanceo en grados
    //   swaySpeed          : velocidad del ciclo principal (Hz)
    //   gustFrequency      : frecuencia de ráfagas superpuestas
    //   gustAmplitudeMult  : multiplicador de amplitud en ráfaga (1=sin ráfaga)
    [System.Serializable]
    public struct WindPreset
    {
        public float maxAngleDeg;
        public float swaySpeed;
        public float gustFrequency;
        public float gustAmplitudeMult;
    }

    // Presets indexados igual que StageManager.Stage (0-5)
    [Header("Presets de viento por etapa (índice = Stage)")]
    public WindPreset[] windPresets = new WindPreset[]
    {
        new WindPreset { maxAngleDeg =  1.2f, swaySpeed = 0.6f, gustFrequency = 0.10f, gustAmplitudeMult = 1.2f },  // Intro
        new WindPreset { maxAngleDeg =  1.2f, swaySpeed = 0.7f, gustFrequency = 0.12f, gustAmplitudeMult = 1.3f },  // Etapa1 — día claro
        new WindPreset { maxAngleDeg =  3.0f, swaySpeed = 1.0f, gustFrequency = 0.25f, gustAmplitudeMult = 1.6f },  // Etapa2 — nubosidad
        new WindPreset { maxAngleDeg =  6.0f, swaySpeed = 1.5f, gustFrequency = 0.40f, gustAmplitudeMult = 2.0f },  // Etapa3 — lluvia
        new WindPreset { maxAngleDeg = 11.0f, swaySpeed = 2.0f, gustFrequency = 0.55f, gustAmplitudeMult = 2.5f },  // Etapa4 — tormenta
        new WindPreset { maxAngleDeg =  4.0f, swaySpeed = 1.1f, gustFrequency = 0.20f, gustAmplitudeMult = 1.4f },  // Etapa5 — post-tormenta
    };

    [Header("Rendimiento")]
    [Tooltip("Actualizar las matas cada N frames. 1=cada frame, 2=cada 2 frames (recomendado WebGL), 3=cada 3 frames.")]
    [Range(1, 4)]
    public int updateEveryNFrames = 2;

    // ── Internos ──────────────────────────────────────────────────────────────
    private struct GrassEntry
    {
        public Transform  tr;
        public Quaternion originalRot;
        public float      phaseX;      // desfase seno en X (adelante/atrás)
        public float      phaseZ;      // desfase seno en Z (lateral)
        public float      speedMult;   // variación de velocidad per-mata
    }

    private List<GrassEntry> _grasses = new List<GrassEntry>();
    private WindPreset        _currentPreset;
    private WindPreset        _targetPreset;
    private float             _blendT = 1f;    // 0=current→target, 1=reached target
    private Coroutine         _blendRoutine;
    private int               _frameCounter;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        CollectGrass();
    }

    private void Start()
    {
        // Preset inicial
        int stageIdx = 0;
        if (StageManager.Instance != null)
            stageIdx = (int)StageManager.Instance.CurrentStage;

        _currentPreset = SafePreset(stageIdx);
        _targetPreset  = _currentPreset;
        _blendT        = 1f;

        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged += OnStageChanged;
    }

    private void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;
    }

    private void Update()
    {
        if (_grasses.Count == 0) return;

        _frameCounter++;
        if (_frameCounter % updateEveryNFrames != 0) return;

        // Interpolar preset actual → target
        WindPreset p = LerpPreset(_currentPreset, _targetPreset, _blendT);

        float t = Time.time;

        foreach (var g in _grasses)
        {
            // Ráfaga: modulación lenta que sube y baja la amplitud
            float gust = 1f + (p.gustAmplitudeMult - 1f) *
                         Mathf.Clamp01(Mathf.Sin(t * p.gustFrequency * Mathf.PI * 2f + g.phaseX * 0.3f) * 0.5f + 0.5f);

            float speed  = p.swaySpeed * g.speedMult;
            float angleX = Mathf.Sin(t * speed       + g.phaseX) * p.maxAngleDeg * gust;
            float angleZ = Mathf.Sin(t * speed * 0.7f + g.phaseZ) * p.maxAngleDeg * 0.55f * gust;

            g.tr.localRotation = g.originalRot * Quaternion.Euler(angleX, 0f, angleZ);
        }
    }

    // ── Colección de matas ────────────────────────────────────────────────────
    private void CollectGrass()
    {
        if (terrainRoot == null)
        {
            var go = GameObject.Find("Terrain_V2");
            if (go == null) go = GameObject.Find("Terrain_V4");
            if (go != null) terrainRoot = go.transform;
        }

        if (terrainRoot == null)
        {
            Debug.LogWarning("[GrassWindController] No se encontró terrainRoot. Asigna Terrain_V2 o Terrain_V4 en el Inspector.");
            return;
        }

        _grasses.Clear();
        int seed = 0;
        foreach (Transform child in terrainRoot)
        {
            if (!child.name.StartsWith(grassNamePrefix)) continue;

            // Desfase basado en posición mundial para que cada mata sea única
            float wx = child.position.x;
            float wz = child.position.z;

            _grasses.Add(new GrassEntry
            {
                tr          = child,
                originalRot = child.localRotation,
                phaseX      = wx * 1.37f + seed * 0.91f,
                phaseZ      = wz * 1.13f + seed * 1.27f,
                speedMult   = 0.85f + (Mathf.Abs(Mathf.Sin(wx + wz)) * 0.30f),
            });
            seed++;
        }

        Debug.Log($"[GrassWindController] {_grasses.Count} matas de pasto registradas.");
    }

    // ── Cambio de etapa ───────────────────────────────────────────────────────
    private void OnStageChanged(StageManager.Stage prev, StageManager.Stage next)
    {
        WindPreset target = SafePreset((int)next);

        if (_blendRoutine != null) StopCoroutine(_blendRoutine);

        _currentPreset = LerpPreset(_currentPreset, _targetPreset, _blendT);
        _targetPreset  = target;

        if (transitionDuration > 0f)
            _blendRoutine = StartCoroutine(BlendRoutine());
        else
            _blendT = 1f;
    }

    private IEnumerator BlendRoutine()
    {
        _blendT = 0f;
        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            _blendT  = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / transitionDuration));
            yield return null;
        }
        _blendT       = 1f;
        _blendRoutine = null;
    }

    // ── Utilidades ────────────────────────────────────────────────────────────
    private WindPreset SafePreset(int idx)
    {
        if (windPresets == null || windPresets.Length == 0)
            return default;
        return windPresets[Mathf.Clamp(idx, 0, windPresets.Length - 1)];
    }

    private static WindPreset LerpPreset(WindPreset a, WindPreset b, float t)
    {
        return new WindPreset
        {
            maxAngleDeg       = Mathf.Lerp(a.maxAngleDeg,       b.maxAngleDeg,       t),
            swaySpeed         = Mathf.Lerp(a.swaySpeed,         b.swaySpeed,         t),
            gustFrequency     = Mathf.Lerp(a.gustFrequency,     b.gustFrequency,     t),
            gustAmplitudeMult = Mathf.Lerp(a.gustAmplitudeMult, b.gustAmplitudeMult, t),
        };
    }

    // ── API pública ────────────────────────────────────────────────────────────
    /// <summary>Fuerza un preset concreto sin cambiar la etapa (útil para debug).</summary>
    public void ForcePreset(int stageIndex)
    {
        _currentPreset = SafePreset(stageIndex);
        _targetPreset  = _currentPreset;
        _blendT        = 1f;
        if (_blendRoutine != null) { StopCoroutine(_blendRoutine); _blendRoutine = null; }
    }

    /// <summary>Recoge de nuevo las matas (útil si el terreno cambia en runtime).</summary>
    public void RefreshGrass() => CollectGrass();
}
