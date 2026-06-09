using System.Collections;
using UnityEngine;

/// <summary>
/// Controla animación de flujo y apariencia del agua sobre una malla con shader Custom/WaterFlow.
/// Al cambiar de etapa interpola suavemente: color del agua, color de espuma,
/// intensidad de espuma, brillo, fuerza de ondas y amplitud de desplazamiento
/// geométrico — consiguiendo el efecto de crecida con olas visibles a medida
/// que sube el nivel de riesgo.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class WaterFlowController : MonoBehaviour
{
    // ── Corriente ─────────────────────────────────────────────────────────────
    [Header("Corriente")]
    [SerializeField] private Vector2 flowDirection = new Vector2(1f, 0f);
    [SerializeField] [Range(0f, 1f)] private float flowSpeed = 0.08f;

    // ── Config visual por etapa ───────────────────────────────────────────────
    [System.Serializable]
    public class StageWaterConfig
    {
        public StageManager.Stage stage;

        [Tooltip("Color base del agua (RGB = tinte, A = opacidad).")]
        public Color waterColor = new Color(0.18f, 0.48f, 0.75f, 0.75f);

        [Tooltip("Color de la espuma superficial. En crecidas usar tonos arena/barro.")]
        public Color foamColor  = new Color(0.85f, 0.92f, 1.00f, 0.50f);

        [Tooltip("Intensidad de la espuma (0 = sin espuma, 1 = espuma total).")]
        [Range(0f, 1f)] public float foamBlend    = 0.28f;

        [Tooltip("Brillo especular. Agua lodosa = valores bajos (≈0.2).")]
        [Range(0f, 1f)] public float glossiness   = 0.85f;

        [Tooltip("Intensidad de las ondas del normal map. Crecida = valores altos.")]
        [Range(0f, 3f)] public float bumpStrength = 0.65f;

        [Header("Desplazamiento geométrico")]
        [Tooltip("Altura máxima de las olas en metros. 0 = agua plana (N1).")]
        [Range(0f, 0.5f)] public float waveAmplitude = 0f;

        [Tooltip("Frecuencia espacial de las olas. Más alto = olas más cortas y numerosas.")]
        [Range(0f, 20f)]  public float waveFrequency = 6f;

        [Tooltip("Multiplicador de velocidad de animación de las olas.")]
        [Range(0f, 5f)]   public float waveSpeed     = 1f;
    }

    [Header("Apariencia por etapa")]
    public StageWaterConfig[] stageConfigs = new StageWaterConfig[]
    {
        // ── Intro / Etapa 1 — quebrada normal, agua clara, superficie plana ────
        new StageWaterConfig
        {
            stage         = StageManager.Stage.Intro,
            waterColor    = new Color(0.15f, 0.40f, 0.65f, 0.75f),
            foamColor     = new Color(0.85f, 0.92f, 1.00f, 0.50f),
            foamBlend     = 0.22f, glossiness = 0.88f, bumpStrength = 0.55f,
            waveAmplitude = 0.00f, waveFrequency = 4f, waveSpeed = 0.5f
        },
        new StageWaterConfig
        {
            stage         = StageManager.Stage.Etapa1,
            waterColor    = new Color(0.15f, 0.40f, 0.65f, 0.75f),
            foamColor     = new Color(0.85f, 0.92f, 1.00f, 0.50f),
            foamBlend     = 0.22f, glossiness = 0.88f, bumpStrength = 0.55f,
            waveAmplitude = 0.00f, waveFrequency = 4f, waveSpeed = 0.5f
        },
        // ── Etapa 2 — primeras lluvias, ondas leves ───────────────────────────
        new StageWaterConfig
        {
            stage         = StageManager.Stage.Etapa2,
            waterColor    = new Color(0.20f, 0.38f, 0.54f, 0.82f),
            foamColor     = new Color(0.80f, 0.84f, 0.88f, 0.55f),
            foamBlend     = 0.28f, glossiness = 0.68f, bumpStrength = 0.90f,
            waveAmplitude = 0.04f, waveFrequency = 6f, waveSpeed = 0.8f
        },
        // ── Etapa 3 — crecida activa, agua lodosa, olas medianas ─────────────
        new StageWaterConfig
        {
            stage         = StageManager.Stage.Etapa3,
            waterColor    = new Color(0.50f, 0.30f, 0.07f, 0.90f),
            foamColor     = new Color(0.80f, 0.68f, 0.38f, 0.68f),
            foamBlend     = 0.44f, glossiness = 0.38f, bumpStrength = 1.60f,
            waveAmplitude = 0.11f, waveFrequency = 8f, waveSpeed = 1.4f
        },
        // ── Etapa 4 — crecida máxima, barro oscuro, olas agresivas ───────────
        new StageWaterConfig
        {
            stage         = StageManager.Stage.Etapa4,
            waterColor    = new Color(0.36f, 0.18f, 0.03f, 0.96f),
            foamColor     = new Color(0.64f, 0.48f, 0.20f, 0.78f),
            foamBlend     = 0.55f, glossiness = 0.20f, bumpStrength = 2.10f,
            waveAmplitude = 0.20f, waveFrequency = 10f, waveSpeed = 2.0f
        },
        // ── Etapa 5 — post-evento, barro residual, olas residuales ───────────
        new StageWaterConfig
        {
            stage         = StageManager.Stage.Etapa5,
            waterColor    = new Color(0.40f, 0.26f, 0.08f, 0.88f),
            foamColor     = new Color(0.72f, 0.60f, 0.32f, 0.60f),
            foamBlend     = 0.32f, glossiness = 0.34f, bumpStrength = 1.20f,
            waveAmplitude = 0.07f, waveFrequency = 6f, waveSpeed = 0.9f
        },
    };

    [Tooltip("Segundos de transición de apariencia al cambiar de etapa.")]
    [Range(0f, 8f)] public float transitionDuration = 3.0f;

    // ── Compatibilidad ────────────────────────────────────────────────────────
    [Header("Compatibilidad")]
    [Tooltip("TRUE: solo hace scroll de _MainTex (cualquier shader).\n" +
             "FALSE: usa todas las propiedades de Custom/WaterFlow.")]
    [SerializeField] private bool fallbackMode = false;

    // ── Estado interno ────────────────────────────────────────────────────────
    private Material  _mat;
    private Coroutine _transitionRoutine;

    private Color _curWater;
    private Color _curFoam;
    private float _curFoamBlend;
    private float _curGlossiness;
    private float _curBumpStrength;
    private float _curWaveAmplitude;
    private float _curWaveFrequency;
    private float _curWaveSpeed;

    // IDs de propiedades del shader
    private static readonly int PropFlowDir       = Shader.PropertyToID("_FlowDirection");
    private static readonly int PropFlowSpeed     = Shader.PropertyToID("_FlowSpeed");
    private static readonly int PropColor         = Shader.PropertyToID("_Color");
    private static readonly int PropFoamColor     = Shader.PropertyToID("_FoamColor");
    private static readonly int PropFoamBlend     = Shader.PropertyToID("_FoamBlend");
    private static readonly int PropGlossiness    = Shader.PropertyToID("_Glossiness");
    private static readonly int PropBumpStrength  = Shader.PropertyToID("_BumpStrength");
    private static readonly int PropWaveAmplitude = Shader.PropertyToID("_WaveAmplitude");
    private static readonly int PropWaveFrequency = Shader.PropertyToID("_WaveFrequency");
    private static readonly int PropWaveSpeed     = Shader.PropertyToID("_WaveSpeed");

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        _mat = GetComponent<MeshRenderer>().material;

        var init = FindConfig(StageManager.Stage.Etapa1);
        _curWater         = init.waterColor;
        _curFoam          = init.foamColor;
        _curFoamBlend     = init.foamBlend;
        _curGlossiness    = init.glossiness;
        _curBumpStrength  = init.bumpStrength;
        _curWaveAmplitude = init.waveAmplitude;
        _curWaveFrequency = init.waveFrequency;
        _curWaveSpeed     = init.waveSpeed;

        PushToMaterial();
    }

    void Start()
    {
        if (StageManager.Instance != null)
        {
            StageManager.Instance.OnStageChanged += OnStageChanged;
            ApplyStageConfig(StageManager.Instance.CurrentStage, instant: true);
        }
    }

    void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;
    }

    void Update()
    {
        if (!fallbackMode) return;
        _mat.SetTextureOffset("_MainTex", NormalizedDir() * (flowSpeed * Time.time));
    }

    // ── Reacción a etapas ─────────────────────────────────────────────────────
    private void OnStageChanged(StageManager.Stage prev, StageManager.Stage next)
        => ApplyStageConfig(next, instant: false);

    private void ApplyStageConfig(StageManager.Stage stage, bool instant)
    {
        var cfg = FindConfig(stage);

        if (_transitionRoutine != null)
            StopCoroutine(_transitionRoutine);

        if (instant || transitionDuration <= 0f)
            SnapToConfig(cfg);
        else
            _transitionRoutine = StartCoroutine(TransitionRoutine(cfg));
    }

    private StageWaterConfig FindConfig(StageManager.Stage stage)
    {
        if (stageConfigs != null)
            foreach (var c in stageConfigs)
                if (c.stage == stage) return c;

        return new StageWaterConfig { stage = stage };
    }

    // ── Aplicación de apariencia ──────────────────────────────────────────────
    private void SnapToConfig(StageWaterConfig cfg)
    {
        _curWater         = cfg.waterColor;
        _curFoam          = cfg.foamColor;
        _curFoamBlend     = cfg.foamBlend;
        _curGlossiness    = cfg.glossiness;
        _curBumpStrength  = cfg.bumpStrength;
        _curWaveAmplitude = cfg.waveAmplitude;
        _curWaveFrequency = cfg.waveFrequency;
        _curWaveSpeed     = cfg.waveSpeed;
        PushToMaterial();
    }

    private IEnumerator TransitionRoutine(StageWaterConfig to)
    {
        Color fromWater    = _curWater;
        Color fromFoam     = _curFoam;
        float fromFoamB    = _curFoamBlend;
        float fromGloss    = _curGlossiness;
        float fromBump     = _curBumpStrength;
        float fromWaveAmp  = _curWaveAmplitude;
        float fromWaveFreq = _curWaveFrequency;
        float fromWaveSpd  = _curWaveSpeed;

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / transitionDuration));

            _curWater         = Color.Lerp(fromWater,    to.waterColor,    t);
            _curFoam          = Color.Lerp(fromFoam,     to.foamColor,     t);
            _curFoamBlend     = Mathf.Lerp(fromFoamB,   to.foamBlend,     t);
            _curGlossiness    = Mathf.Lerp(fromGloss,   to.glossiness,    t);
            _curBumpStrength  = Mathf.Lerp(fromBump,    to.bumpStrength,  t);
            _curWaveAmplitude = Mathf.Lerp(fromWaveAmp, to.waveAmplitude, t);
            _curWaveFrequency = Mathf.Lerp(fromWaveFreq,to.waveFrequency, t);
            _curWaveSpeed     = Mathf.Lerp(fromWaveSpd, to.waveSpeed,     t);
            PushToMaterial();
            yield return null;
        }
        SnapToConfig(to);
        _transitionRoutine = null;
    }

    private void PushToMaterial()
    {
        if (_mat == null || fallbackMode) return;

        Vector2 dir = NormalizedDir();
        _mat.SetVector(PropFlowDir,       new Vector4(dir.x, dir.y, 0f, 0f));
        _mat.SetFloat (PropFlowSpeed,     flowSpeed);
        _mat.SetColor (PropColor,         _curWater);
        _mat.SetColor (PropFoamColor,     _curFoam);
        _mat.SetFloat (PropFoamBlend,     _curFoamBlend);
        _mat.SetFloat (PropGlossiness,    _curGlossiness);
        _mat.SetFloat (PropBumpStrength,  _curBumpStrength);
        _mat.SetFloat (PropWaveAmplitude, _curWaveAmplitude);
        _mat.SetFloat (PropWaveFrequency, _curWaveFrequency);
        _mat.SetFloat (PropWaveSpeed,     _curWaveSpeed);
    }

    // ── API pública ───────────────────────────────────────────────────────────
    public void SetFlowDirection(Vector2 dir) { flowDirection = dir; PushToMaterial(); }
    public void SetSpeed(float speed)         { flowSpeed = Mathf.Max(0f, speed); PushToMaterial(); }

    /// <summary>
    /// Fuerza la apariencia de una etapa concreta sin necesitar OnStageChanged.
    /// Útil para previsualización en el hub panorámico.
    /// </summary>
    public void ForceStage(StageManager.Stage stage, float overrideDuration = -1f)
    {
        var cfg = FindConfig(stage);

        if (_transitionRoutine != null)
            StopCoroutine(_transitionRoutine);

        float dur = overrideDuration >= 0f ? overrideDuration : transitionDuration;
        if (dur <= 0f)
            SnapToConfig(cfg);
        else
            _transitionRoutine = StartCoroutine(TransitionRoutine(cfg));
    }

    public void SetColor(Color color)
    {
        _curWater = color;
        if (!fallbackMode && _mat != null) _mat.SetColor(PropColor, color);
    }

    public void LerpColor(Color from, Color to, float t)
        => SetColor(Color.Lerp(from, to, Mathf.Clamp01(t)));

    // ── Helpers ───────────────────────────────────────────────────────────────
    private Vector2 NormalizedDir()
        => flowDirection.sqrMagnitude > 0.0001f ? flowDirection.normalized : Vector2.right;

#if UNITY_EDITOR
    void OnValidate()
    {
        var r = GetComponent<MeshRenderer>();
        if (r == null) return;
        var mat = Application.isPlaying ? _mat : r.sharedMaterial;
        if (mat == null) return;
        Vector2 dir = NormalizedDir();
        mat.SetVector(PropFlowDir,   new Vector4(dir.x, dir.y, 0f, 0f));
        mat.SetFloat (PropFlowSpeed, flowSpeed);
    }
#endif
}
