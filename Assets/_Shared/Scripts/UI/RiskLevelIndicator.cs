using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// RiskLevelIndicator — HUD de nivel de riesgo N1–N4.
///
/// Muestra un sprite distinto para cada nivel. El contenido visual
/// (textos, colores, distribución) vive dentro de cada sprite.
/// El widget se oculta cuando el nivel es None.
/// Niveles N3/N4 activan un pulso de escala.
///
/// Setup en escena:
///   RiskLevelIndicator          [este script]
///   └── IndicatorRoot           [GameObject] ← indicatorRoot
///       └── LevelImage          [Image]      ← levelImage
///
/// Asignar spriteN1–spriteN4 en el Inspector.
/// </summary>
public class RiskLevelIndicator : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static RiskLevelIndicator Instance { get; private set; }

    // ── Nivel por defecto por etapa ───────────────────────────────────────────
    [Serializable]
    public class StageRiskConfig
    {
        [Tooltip("Etapa a la que aplica este nivel por defecto.")]
        public StageManager.Stage stage;

        [Tooltip("Nivel de riesgo a mostrar al entrar a esta etapa.\n" +
                 "None = limpiar el indicador al entrar a la etapa.")]
        public RiskLevel defaultRiskLevel = RiskLevel.None;
    }

    [Header("Nivel de riesgo por etapa (opcional)")]
    [Tooltip("Al entrar a cada etapa configurada, el indicador muestra el nivel asignado.\n" +
             "Deja vacío si solo quieres que los hotspots controlen el nivel.")]
    public StageRiskConfig[] stageRiskDefaults = new StageRiskConfig[]
    {
        new StageRiskConfig { stage = StageManager.Stage.Intro,  defaultRiskLevel = RiskLevel.None },
        new StageRiskConfig { stage = StageManager.Stage.Etapa1, defaultRiskLevel = RiskLevel.N1  },
        new StageRiskConfig { stage = StageManager.Stage.Etapa2, defaultRiskLevel = RiskLevel.N2  },
        new StageRiskConfig { stage = StageManager.Stage.Etapa3, defaultRiskLevel = RiskLevel.N3  },
        new StageRiskConfig { stage = StageManager.Stage.Etapa4, defaultRiskLevel = RiskLevel.N4  },
        new StageRiskConfig { stage = StageManager.Stage.Etapa5, defaultRiskLevel = RiskLevel.N1  },
    };

    // ── Referencias UI ────────────────────────────────────────────────────────
    [Header("UI")]
    [Tooltip("GameObject raíz del widget. Se activa cuando hay nivel asignado, se desactiva con None.")]
    public GameObject indicatorRoot;

    [Tooltip("Image que muestra el sprite del nivel activo.")]
    public Image levelImage;

    // ── Sprites por nivel ─────────────────────────────────────────────────────
    [Header("Sprites por nivel")]
    public Sprite spriteN1;
    public Sprite spriteN2;
    public Sprite spriteN3;
    public Sprite spriteN4;

    // ── Animación ─────────────────────────────────────────────────────────────
    [Header("Animación")]
    [Tooltip("Velocidad del pulso de escala para Nivel 3 (ciclos/segundo).")]
    [Range(0f, 5f)]
    public float pulseSpeedN3 = 1.5f;

    [Tooltip("Velocidad del pulso de escala para Nivel 4 (ciclos/segundo).")]
    [Range(0f, 5f)]
    public float pulseSpeedN4 = 2.5f;

    [Tooltip("Amplitud del pulso de escala (0.05 = ±5% del tamaño original).")]
    [Range(0f, 0.2f)]
    public float pulseAmplitude = 0.07f;

    // ── Estado ────────────────────────────────────────────────────────────────
    public RiskLevel CurrentLevel { get; private set; } = RiskLevel.None;

    private Vector3 _baseScale;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (indicatorRoot != null)
            _baseScale = indicatorRoot.transform.localScale;
    }

    private void Start()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged += OnStageChanged;

        if (indicatorRoot != null) indicatorRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;
    }

    private void Update()
    {
        ApplyPulse();
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Establece el nivel de riesgo activo y actualiza el sprite.
    /// None oculta el widget.
    /// </summary>
    public void SetLevel(RiskLevel level)
    {
        if (level == CurrentLevel) return;
        CurrentLevel = level;

        bool visible = level != RiskLevel.None;
        if (indicatorRoot != null) indicatorRoot.SetActive(visible);
        if (!visible) return;

        if (levelImage != null) levelImage.sprite = GetSprite(level);
    }

    /// <summary>Oculta el widget. Equivale a SetLevel(None).</summary>
    public void ClearLevel() => SetLevel(RiskLevel.None);

    // ── Reacción al cambio de etapa ───────────────────────────────────────────
    private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
    {
        StageRiskConfig config = FindStageConfig(current);
        if (config != null)
            SetLevel(config.defaultRiskLevel);
    }

    // ── Restaurar sprite al reactivarse ──────────────────────────────────────
    private void OnEnable()
    {
        if (CurrentLevel == RiskLevel.None || levelImage == null) return;
        levelImage.sprite = GetSprite(CurrentLevel);
    }

    // ── Pulso de escala (N3 / N4) ─────────────────────────────────────────────
    private void ApplyPulse()
    {
        if (indicatorRoot == null || CurrentLevel < RiskLevel.N3) return;

        float speed = CurrentLevel == RiskLevel.N4 ? pulseSpeedN4 : pulseSpeedN3;
        float pulse = 1f + pulseAmplitude * Mathf.Sin(Time.time * speed * Mathf.PI * 2f);
        indicatorRoot.transform.localScale = _baseScale * pulse;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private Sprite GetSprite(RiskLevel level) => level switch
    {
        RiskLevel.N1 => spriteN1,
        RiskLevel.N2 => spriteN2,
        RiskLevel.N3 => spriteN3,
        RiskLevel.N4 => spriteN4,
        _            => null,
    };

    private StageRiskConfig FindStageConfig(StageManager.Stage stage)
    {
        if (stageRiskDefaults == null) return null;
        foreach (var c in stageRiskDefaults)
            if (c.stage == stage) return c;
        return null;
    }
}
