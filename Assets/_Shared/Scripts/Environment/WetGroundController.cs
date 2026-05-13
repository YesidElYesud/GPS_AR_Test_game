using System.Collections;
using UnityEngine;

/// <summary>
/// Simula pavimento mojado animando _Glossiness, _Metallic y _Color del Standard shader
/// en todos los materiales del Renderer asignado (p.ej. camino_1 con sus 3 slots).
///
/// Suscrito a StageManager.OnStageChanged. Expone ForceStage() para preview
/// desde SceneOverviewController.
///
/// Setup: adjuntar al GameObject de la malla 'camino_1'. Si groundRenderer
/// queda vacío se auto-detecta en el propio GO. Los defaults están baked en
/// código — el array stageConfigs se usa solo para sobreescribir en Inspector.
/// </summary>
public class WetGroundController : MonoBehaviour
{
    [System.Serializable]
    public struct WetGroundConfig
    {
        public StageManager.Stage stage;
        [Range(0f, 1f)] public float glossiness;
        [Range(0f, 1f)] public float metallic;
        [Range(0.5f, 1f)]
        [Tooltip("Factor de oscurecimiento del albedo (1 = sin cambio, < 1 = más oscuro)")]
        public float colorDarken;
    }

    [Header("Renderer")]
    [Tooltip("Renderer de la malla del camino. Se auto-detecta en el propio GO si está vacío.")]
    public Renderer groundRenderer;

    [Header("Configuración por etapa")]
    [Tooltip("Deja vacío para usar los defaults baked en código.")]
    public WetGroundConfig[] stageConfigs;

    [Header("Transición")]
    [Range(0.5f, 8f)]
    public float transitionDuration = 2.5f;

    // ── Defaults ──────────────────────────────────────────────────────────────
    private static readonly WetGroundConfig[] k_Defaults =
    {
        new WetGroundConfig { stage = StageManager.Stage.Intro,  glossiness = 0.00f, metallic = 0.00f, colorDarken = 1.00f },
        new WetGroundConfig { stage = StageManager.Stage.Etapa1, glossiness = 0.00f, metallic = 0.00f, colorDarken = 1.00f },
        new WetGroundConfig { stage = StageManager.Stage.Etapa2, glossiness = 0.15f, metallic = 0.00f, colorDarken = 0.96f },
        new WetGroundConfig { stage = StageManager.Stage.Etapa3, glossiness = 0.62f, metallic = 0.06f, colorDarken = 0.82f },
        new WetGroundConfig { stage = StageManager.Stage.Etapa4, glossiness = 0.80f, metallic = 0.10f, colorDarken = 0.72f },
        new WetGroundConfig { stage = StageManager.Stage.Etapa5, glossiness = 0.40f, metallic = 0.04f, colorDarken = 0.88f },
    };

    // ── Estado interno ────────────────────────────────────────────────────────
    private Material[] _mats;
    private float[]    _origGloss;
    private float[]    _origMetallic;
    private Color[]    _origColor;
    private int        _matCount;
    private Coroutine  _routine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (groundRenderer == null)
            groundRenderer = GetComponent<Renderer>();

        if (stageConfigs == null || stageConfigs.Length == 0)
            stageConfigs = k_Defaults;
    }

    private void Start()
    {
        if (groundRenderer == null)
        {
            Debug.LogWarning("[WetGround] groundRenderer no asignado.", this);
            return;
        }

        // Capturar propiedades originales desde sharedMaterials (antes de instanciar)
        var shared = groundRenderer.sharedMaterials;
        _matCount    = shared.Length;
        _origGloss   = new float[_matCount];
        _origMetallic = new float[_matCount];
        _origColor   = new Color[_matCount];

        for (int i = 0; i < _matCount; i++)
        {
            _origGloss[i]    = shared[i] != null && shared[i].HasProperty("_Glossiness")
                                ? shared[i].GetFloat("_Glossiness") : 0f;
            _origMetallic[i] = shared[i] != null && shared[i].HasProperty("_Metallic")
                                ? shared[i].GetFloat("_Metallic")   : 0f;
            _origColor[i]    = shared[i] != null && shared[i].HasProperty("_Color")
                                ? shared[i].GetColor("_Color")      : Color.white;
        }

        // Instanciar para no modificar los assets compartidos
        _mats = groundRenderer.materials;

        // Aplicar la etapa inicial sin transición
        if (StageManager.Instance != null)
            ApplyImmediate(StageManager.Instance.CurrentStage);

        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged += OnStageChanged;
    }

    private void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;
    }

    // ── Eventos ───────────────────────────────────────────────────────────────
    private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
    {
        ForceStage(current, transitionDuration);
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Fuerza la apariencia de una etapa con transición. Si duration &lt; 0 usa
    /// transitionDuration. Llamado por SceneOverviewController durante preview.
    /// </summary>
    public void ForceStage(StageManager.Stage stage, float duration = -1f)
    {
        if (_mats == null) return;
        float dur = duration < 0f ? transitionDuration : duration;
        var cfg = GetConfig(stage);

        if (_routine != null) StopCoroutine(_routine);
        if (dur <= 0f)
            ApplyImmediate(stage);
        else
            _routine = StartCoroutine(TransitionRoutine(cfg, dur));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private WetGroundConfig GetConfig(StageManager.Stage stage)
    {
        // Búsqueda exacta
        foreach (var c in stageConfigs)
            if (c.stage == stage) return c;

        // Fallback: etapa inferior más cercana
        WetGroundConfig best = stageConfigs[0];
        foreach (var c in stageConfigs)
            if ((int)c.stage <= (int)stage) best = c;
        return best;
    }

    private void ApplyImmediate(StageManager.Stage stage)
    {
        if (_mats == null) return;
        var cfg = GetConfig(stage);
        PushToMaterials(cfg.glossiness, cfg.metallic, cfg.colorDarken);
    }

    private void PushToMaterials(float glossiness, float metallic, float colorDarken)
    {
        for (int i = 0; i < _matCount; i++)
        {
            if (_mats[i] == null) continue;
            if (_mats[i].HasProperty("_Glossiness"))
                _mats[i].SetFloat("_Glossiness", glossiness);
            if (_mats[i].HasProperty("_Metallic"))
                _mats[i].SetFloat("_Metallic", metallic);
            if (_mats[i].HasProperty("_Color"))
            {
                var c = new Color(
                    _origColor[i].r * colorDarken,
                    _origColor[i].g * colorDarken,
                    _origColor[i].b * colorDarken,
                    _origColor[i].a);
                _mats[i].SetColor("_Color", c);
            }
        }
    }

    private IEnumerator TransitionRoutine(WetGroundConfig target, float duration)
    {
        // Capturar estado actual de los materiales instanciados
        float[] fromGloss    = new float[_matCount];
        float[] fromMetallic = new float[_matCount];
        Color[] fromColor    = new Color[_matCount];
        Color[] toColor      = new Color[_matCount];

        for (int i = 0; i < _matCount; i++)
        {
            fromGloss[i]    = _mats[i].HasProperty("_Glossiness") ? _mats[i].GetFloat("_Glossiness") : 0f;
            fromMetallic[i] = _mats[i].HasProperty("_Metallic")   ? _mats[i].GetFloat("_Metallic")   : 0f;
            fromColor[i]    = _mats[i].HasProperty("_Color")      ? _mats[i].GetColor("_Color")      : Color.white;
            toColor[i] = new Color(
                _origColor[i].r * target.colorDarken,
                _origColor[i].g * target.colorDarken,
                _origColor[i].b * target.colorDarken,
                _origColor[i].a);
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = SmoothStep(Mathf.Clamp01(elapsed / duration));

            for (int i = 0; i < _matCount; i++)
            {
                if (_mats[i] == null) continue;
                if (_mats[i].HasProperty("_Glossiness"))
                    _mats[i].SetFloat("_Glossiness", Mathf.Lerp(fromGloss[i], target.glossiness, t));
                if (_mats[i].HasProperty("_Metallic"))
                    _mats[i].SetFloat("_Metallic", Mathf.Lerp(fromMetallic[i], target.metallic, t));
                if (_mats[i].HasProperty("_Color"))
                    _mats[i].SetColor("_Color", Color.Lerp(fromColor[i], toColor[i], t));
            }

            yield return null;
        }

        PushToMaterials(target.glossiness, target.metallic, target.colorDarken);
        _routine = null;
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);
}
