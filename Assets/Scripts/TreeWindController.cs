using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TreeWindController — Balanceo de árboles por etapa del evento.
///
/// Anima la rotación local de una lista de árboles con ondas seno desfasadas,
/// igual que GrassWindController pero con presets apropiados para árboles
/// (movimiento más lento, amplitudes mayores, ráfagas más pronunciadas).
///
/// IMPORTANTE — Pivote del mesh:
///   El pivote del transform debe estar en la BASE del tronco para que el árbol
///   se doble naturalmente. Si el pivote está en el centro del tronco, el árbol
///   rotará alrededor de su centro (aspecto irreal). Verificar en el Inspector:
///   el gizmo de posición debe coincidir con la base del árbol en la escena.
///   Si no es así, crear un GameObject vacío padre en la base y hacerlo hijo.
///
/// Setup:
///   1. Crear un GameObject vacío "TreeWindManager" en la escena.
///   2. Adjuntar este script.
///   3. Arrastrar cada árbol (Tree 7, Tree 3, etc.) al array "trees" en el Inspector.
///   4. Ajustar los presets de viento si hace falta.
///
/// Intensidades por etapa (por defecto):
///   Intro / Etapa1 : brisa muy suave   (±1.5°)
///   Etapa2         : viento leve       (±4.0°)
///   Etapa3         : viento moderado   (±9.0°)
///   Etapa4         : tormenta fuerte   (±18.0°)
///   Etapa5         : post-tormenta     (±5.0°)
/// </summary>
public class TreeWindController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Árboles a animar")]
    [Tooltip("Arrastra aquí todos los árboles (Tree 7, Tree 3, etc.).")]
    public Transform[] trees;

    [Tooltip("Duración del fundido al cambiar de etapa (seg).")]
    [Range(0f, 5f)]
    public float transitionDuration = 2.0f;

    // ── Presets por etapa ─────────────────────────────────────────────────────
    [System.Serializable]
    public struct WindPreset
    {
        [Tooltip("Ángulo máximo de balanceo en grados.")]
        public float maxAngleDeg;

        [Tooltip("Velocidad del ciclo de balanceo (Hz). Valores bajos = árbol pesado.")]
        public float swaySpeed;

        [Tooltip("Frecuencia de las ráfagas superpuestas (Hz).")]
        public float gustFrequency;

        [Tooltip("Multiplicador de amplitud en ráfaga (1 = sin ráfaga adicional).")]
        public float gustAmplitudeMult;
    }

    [Header("Presets de viento por etapa (índice = Stage enum)")]
    public WindPreset[] windPresets = new WindPreset[]
    {
        // Índice 0 — Intro
        new WindPreset { maxAngleDeg =  1.5f, swaySpeed = 0.35f, gustFrequency = 0.08f, gustAmplitudeMult = 1.2f },
        // Índice 1 — Etapa1: día claro
        new WindPreset { maxAngleDeg =  1.5f, swaySpeed = 0.40f, gustFrequency = 0.10f, gustAmplitudeMult = 1.3f },
        // Índice 2 — Etapa2: cielo nublado, lluvia leve
        new WindPreset { maxAngleDeg =  4.0f, swaySpeed = 0.60f, gustFrequency = 0.18f, gustAmplitudeMult = 1.7f },
        // Índice 3 — Etapa3: lluvia fuerte
        new WindPreset { maxAngleDeg =  9.0f, swaySpeed = 0.90f, gustFrequency = 0.30f, gustAmplitudeMult = 2.2f },
        // Índice 4 — Etapa4: tormenta / emergencia
        new WindPreset { maxAngleDeg = 18.0f, swaySpeed = 1.30f, gustFrequency = 0.50f, gustAmplitudeMult = 2.8f },
        // Índice 5 — Etapa5: post-tormenta
        new WindPreset { maxAngleDeg =  5.0f, swaySpeed = 0.55f, gustFrequency = 0.15f, gustAmplitudeMult = 1.4f },
    };

    // ── Internos ──────────────────────────────────────────────────────────────
    private struct TreeEntry
    {
        public Transform  tr;
        public Quaternion originalRot;
        public float      phaseX;       // desfase seno adelante/atrás
        public float      phaseZ;       // desfase seno lateral
        public float      speedMult;    // variación de velocidad por árbol
    }

    private List<TreeEntry> _entries = new List<TreeEntry>();
    private WindPreset _currentPreset;
    private WindPreset _targetPreset;
    private float      _blendT = 1f;
    private Coroutine  _blendRoutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        BuildEntries();
    }

    private void Start()
    {
        int stageIdx = StageManager.Instance != null ? (int)StageManager.Instance.CurrentStage : 0;
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
        if (_entries.Count == 0) return;

        WindPreset p = LerpPreset(_currentPreset, _targetPreset, _blendT);
        float t = Time.time;

        foreach (var e in _entries)
        {
            // Ráfaga: módulo lento que sube y baja la amplitud
            float gust = 1f + (p.gustAmplitudeMult - 1f) *
                         Mathf.Clamp01(Mathf.Sin(t * p.gustFrequency * Mathf.PI * 2f + e.phaseX * 0.2f) * 0.5f + 0.5f);

            float speed  = p.swaySpeed * e.speedMult;
            float angleX = Mathf.Sin(t * speed        + e.phaseX) * p.maxAngleDeg * gust;
            float angleZ = Mathf.Sin(t * speed * 0.6f + e.phaseZ) * p.maxAngleDeg * 0.45f * gust;

            e.tr.localRotation = e.originalRot * Quaternion.Euler(angleX, 0f, angleZ);
        }
    }

    // ── Construcción de entradas ──────────────────────────────────────────────
    private void BuildEntries()
    {
        _entries.Clear();

        if (trees == null || trees.Length == 0)
        {
            Debug.LogWarning("[TreeWindController] El array 'trees' está vacío. " +
                             "Arrastra los árboles al Inspector.");
            return;
        }

        for (int i = 0; i < trees.Length; i++)
        {
            if (trees[i] == null) continue;

            float wx = trees[i].position.x;
            float wz = trees[i].position.z;

            _entries.Add(new TreeEntry
            {
                tr          = trees[i],
                originalRot = trees[i].localRotation,
                // Desfase basado en posición mundial → cada árbol es único
                phaseX      = wx * 0.97f + i * 1.43f,
                phaseZ      = wz * 1.11f + i * 0.87f,
                // Variación de velocidad sutil para que no balanceen en sincronía
                speedMult   = 0.80f + Mathf.Abs(Mathf.Sin(wx * 0.5f + wz * 0.7f)) * 0.40f,
            });
        }

        Debug.Log($"[TreeWindController] {_entries.Count} árboles registrados.");
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

    // ── Helpers ───────────────────────────────────────────────────────────────
    private WindPreset SafePreset(int idx)
    {
        if (windPresets == null || windPresets.Length == 0) return default;
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

    // ── API pública ───────────────────────────────────────────────────────────
    /// <summary>Fuerza un preset concreto (útil para debug en Play Mode).</summary>
    public void ForcePreset(int stageIndex)
    {
        _currentPreset = SafePreset(stageIndex);
        _targetPreset  = _currentPreset;
        _blendT        = 1f;
        if (_blendRoutine != null) { StopCoroutine(_blendRoutine); _blendRoutine = null; }
    }

    /// <summary>Reconstruye la lista si se cambian los árboles en runtime.</summary>
    public void Refresh() => BuildEntries();
}
