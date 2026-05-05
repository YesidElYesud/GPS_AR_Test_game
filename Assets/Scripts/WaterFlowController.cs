using System.Collections;
using UnityEngine;

/// <summary>
/// Controla la animación de flujo de agua sobre una malla con el shader Custom/WaterFlow.
/// También funciona en modo fallback con cualquier shader estándar (scroll de _MainTex).
///
/// El color del agua cambia automáticamente al cambiar de etapa, interpolando
/// suavemente entre el color configurado para cada stage.
///
/// Uso:
///   1. Asignar el script al GameObject "agua" (o "rio") del Terrain_V2.
///   2. Si el material usa Custom/WaterFlow → dejar fallbackMode = false.
///   3. Si el material usa otro shader → activar fallbackMode = true (solo scrollea _MainTex).
///   4. Ajustar flowDirection y los colores por etapa en el Inspector.
///
/// API pública:
///   SetFlowDirection(Vector2)  — cambia dirección en tiempo de ejecución
///   SetSpeed(float)            — cambia velocidad
///   SetColor(Color)            — aplica un color directamente
///   LerpColor(Color, Color, t) — interpola color entre dos valores
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class WaterFlowController : MonoBehaviour
{
    // ─── Parámetros de flujo ───────────────────────────────────────────────
    [Header("Dirección de corriente")]
    [Tooltip("Vector 2D que indica hacia dónde fluye el agua. Se normaliza automáticamente.")]
    [SerializeField] private Vector2 flowDirection = new Vector2(1f, 0f);

    [Tooltip("Velocidad de desplazamiento de la textura.")]
    [SerializeField] [Range(0f, 1f)] private float flowSpeed = 0.08f;

    // ─── Color base ───────────────────────────────────────────────────────
    [Header("Color base")]
    [Tooltip("Color inicial del agua (se usa si 'stageColors' está vacío o para Intro).")]
    [SerializeField] private Color waterColor = new Color(0.18f, 0.48f, 0.75f, 0.75f);

    // ─── Color por etapa ──────────────────────────────────────────────────
    [System.Serializable]
    public class StageColorConfig
    {
        public StageManager.Stage stage;
        [Tooltip("Color del agua al entrar a esta etapa.")]
        public Color color;
    }

    [Header("Color por etapa")]
    [Tooltip("Color del agua para cada etapa. Si una etapa no está en la lista, mantiene el color anterior.")]
    public StageColorConfig[] stageColors = new StageColorConfig[]
    {
        new StageColorConfig { stage = StageManager.Stage.Intro,  color = new Color(0.18f, 0.48f, 0.75f, 0.75f) }, // azul claro
        new StageColorConfig { stage = StageManager.Stage.Etapa1, color = new Color(0.18f, 0.48f, 0.75f, 0.75f) }, // azul claro
        new StageColorConfig { stage = StageManager.Stage.Etapa2, color = new Color(0.20f, 0.44f, 0.60f, 0.80f) }, // azul más oscuro
        new StageColorConfig { stage = StageManager.Stage.Etapa3, color = new Color(0.42f, 0.30f, 0.12f, 0.85f) }, // marrón intermedio
        new StageColorConfig { stage = StageManager.Stage.Etapa4, color = new Color(0.38f, 0.22f, 0.06f, 0.90f) }, // marrón oscuro crecida
        new StageColorConfig { stage = StageManager.Stage.Etapa5, color = new Color(0.35f, 0.26f, 0.10f, 0.88f) }, // marrón residual
    };

    [Tooltip("Duración de la transición de color al cambiar de etapa (segundos).")]
    [Range(0f, 8f)]
    public float colorTransitionDuration = 3.0f;

    // ─── Modo de compatibilidad ────────────────────────────────────────────
    [Header("Compatibilidad")]
    [Tooltip("TRUE: scroll manual de _MainTex (cualquier shader). " +
             "FALSE: usa propiedades de Custom/WaterFlow (mejor calidad).")]
    [SerializeField] private bool fallbackMode = false;

    // ─── Interno ──────────────────────────────────────────────────────────
    private Material  _mat;
    private Coroutine _colorRoutine;

    private static readonly int PropFlowDir   = Shader.PropertyToID("_FlowDirection");
    private static readonly int PropFlowSpeed = Shader.PropertyToID("_FlowSpeed");
    private static readonly int PropColor     = Shader.PropertyToID("_Color");

    // ──────────────────────────────────────────────────────────────────────
    void Awake()
    {
        _mat = GetComponent<MeshRenderer>().material;
        PushToMaterial();
    }

    void Start()
    {
        if (StageManager.Instance != null)
        {
            StageManager.Instance.OnStageChanged += OnStageChanged;
            // Aplicar color de la etapa inicial sin transición
            ApplyStageColor(StageManager.Instance.CurrentStage, instant: true);
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

        Vector2 dir    = NormalizedDir();
        Vector2 offset = dir * (flowSpeed * Time.time);
        _mat.SetTextureOffset("_MainTex", offset);
    }

    // ─── Reacción a cambio de etapa ───────────────────────────────────────
    private void OnStageChanged(StageManager.Stage prev, StageManager.Stage next)
    {
        ApplyStageColor(next, instant: false);
    }

    private void ApplyStageColor(StageManager.Stage stage, bool instant)
    {
        Color target = FindStageColor(stage);

        if (_colorRoutine != null)
            StopCoroutine(_colorRoutine);

        if (instant || colorTransitionDuration <= 0f)
        {
            SetColor(target);
        }
        else
        {
            _colorRoutine = StartCoroutine(ColorTransitionRoutine(waterColor, target));
        }
    }

    private Color FindStageColor(StageManager.Stage stage)
    {
        if (stageColors == null) return waterColor;
        foreach (var entry in stageColors)
            if (entry.stage == stage) return entry.color;
        return waterColor;
    }

    private IEnumerator ColorTransitionRoutine(Color from, Color to)
    {
        float elapsed = 0f;
        while (elapsed < colorTransitionDuration)
        {
            elapsed += Time.deltaTime;
            SetColor(Color.Lerp(from, to,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / colorTransitionDuration))));
            yield return null;
        }
        SetColor(to);
        _colorRoutine = null;
    }

    // ─── API pública ──────────────────────────────────────────────────────

    /// <summary>Cambia la dirección del flujo en tiempo de ejecución.</summary>
    public void SetFlowDirection(Vector2 dir)
    {
        flowDirection = dir;
        PushToMaterial();
    }

    /// <summary>Cambia la velocidad del flujo.</summary>
    public void SetSpeed(float speed)
    {
        flowSpeed = Mathf.Max(0f, speed);
        PushToMaterial();
    }

    /// <summary>Aplica un color directamente (cancela cualquier transición en curso).</summary>
    public void SetColor(Color color)
    {
        waterColor = color;
        if (!fallbackMode && _mat != null)
            _mat.SetColor(PropColor, color);
    }

    /// <summary>Interpola el color entre <paramref name="from"/> y <paramref name="to"/>.</summary>
    public void LerpColor(Color from, Color to, float t)
        => SetColor(Color.Lerp(from, to, Mathf.Clamp01(t)));

    // ─── Interno ──────────────────────────────────────────────────────────
    private void PushToMaterial()
    {
        if (_mat == null || fallbackMode) return;

        Vector2 dir = NormalizedDir();
        _mat.SetVector(PropFlowDir,   new Vector4(dir.x, dir.y, 0f, 0f));
        _mat.SetFloat (PropFlowSpeed, flowSpeed);
        _mat.SetColor (PropColor,     waterColor);
    }

    private Vector2 NormalizedDir()
    {
        return flowDirection.sqrMagnitude > 0.0001f
            ? flowDirection.normalized
            : Vector2.right;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (_mat == null)
        {
            var r = GetComponent<MeshRenderer>();
            if (r != null) _mat = r.sharedMaterial;
        }
        PushToMaterial();
    }
#endif
}
