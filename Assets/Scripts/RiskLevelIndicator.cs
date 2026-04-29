using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// RiskLevelIndicator — HUD de nivel de riesgo N1–N4 (Sistema 13).
///
/// Muestra un widget persistente en pantalla con el nivel de riesgo actual.
/// El nivel se actualiza desde dos fuentes:
///   1. StageManager.OnStageChanged → aplica el nivel por defecto de cada etapa.
///   2. HotspotController.DispatchAction() → sobreescribe con el nivel del hotspot activo.
///
/// El widget se oculta automáticamente cuando el nivel es None.
/// Niveles N3/N4 activan un pulso visual de alerta.
///
/// Setup en escena:
///   1. Crear un GameObject hijo del HUD: "RiskLevelIndicator".
///   2. Adjuntar este script.
///   3. Construir la jerarquía UI sugerida (ver abajo) y asignar referencias.
///
/// Jerarquía sugerida en HUD:
///   RiskLevelIndicator            [RiskLevelIndicator.cs] (este script)
///   └── IndicatorRoot             [GameObject] ← indicatorRoot (se activa/desactiva)
///       ├── Background            [Image]      ← backgroundImage
///       └── TextColumn            [VLG]
///           ├── LevelLabel        [TMP]        ← levelLabel  ("N1" / "N2" …)
///           └── LevelName         [TMP]        ← levelName   ("Nivel Bajo" …)
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
    [Tooltip("GameObject raíz del widget. Se activa cuando hay nivel asignado y se desactiva con None.")]
    public GameObject indicatorRoot;

    [Tooltip("Image de fondo del widget. Su color cambia según el nivel.")]
    public Image backgroundImage;

    [Tooltip("Texto grande con el código: 'N1', 'N2', 'N3', 'N4'.")]
    public TextMeshProUGUI levelLabel;

    [Tooltip("Texto descriptivo: 'Nivel Bajo', 'Nivel Moderado', etc.")]
    public TextMeshProUGUI levelName;

    // ── Nombres de nivel (editables en Inspector) ─────────────────────────────
    [Header("Nombres de nivel (LevelLabel 1)")]
    public string levelNameN1 = "Normalidad";
    public string levelNameN2 = "Prevención";
    public string levelNameN3 = "Peligro cercano";
    public string levelNameN4 = "Emergencia";

    // ── Colores ───────────────────────────────────────────────────────────────
    [Header("Colores por nivel")]
    public Color colorN1 = new Color(0.30f, 0.69f, 0.31f); // verde    #4CAF50
    public Color colorN2 = new Color(1.00f, 0.76f, 0.03f); // amarillo #FFC107
    public Color colorN3 = new Color(1.00f, 0.60f, 0.00f); // naranja  #FF9800
    public Color colorN4 = new Color(0.96f, 0.26f, 0.21f); // rojo     #F44336

    // ── Animación ─────────────────────────────────────────────────────────────
    [Header("Animación")]
    [Tooltip("Duración del crossfade de color al cambiar de nivel.")]
    [Range(0f, 2f)]
    public float transitionDuration = 0.4f;

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

    private Coroutine _transitionRoutine;
    private Vector3   _baseScale;

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
    /// Establece el nivel de riesgo activo.
    /// Estado (CurrentLevel, textos) se actualiza sincrónicamente siempre.
    /// El color del fondo se anima solo si el GO está activo; si no, se aplica directo.
    /// None oculta el widget.
    /// </summary>
    public void SetLevel(RiskLevel level)
    {
        if (level == CurrentLevel) return;

        if (_transitionRoutine != null)
            StopCoroutine(_transitionRoutine);

        RiskLevel oldLevel = CurrentLevel;
        CurrentLevel = level;

        bool visible = level != RiskLevel.None;
        if (indicatorRoot != null) indicatorRoot.SetActive(visible);
        if (!visible) return;

        if (levelLabel != null) levelLabel.text = level.ToString();
        if (levelName  != null) levelName.text  = GetLevelName(level);

        Color targetColor = GetColor(level);
        if (transitionDuration > 0f && oldLevel != RiskLevel.None && gameObject.activeInHierarchy)
            _transitionRoutine = StartCoroutine(ColorTransitionRoutine(GetColor(oldLevel), targetColor));
        else if (backgroundImage != null)
            backgroundImage.color = targetColor;
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

    // ── Garantizar color correcto al reactivarse ──────────────────────────────
    // Si el hub (u otro sistema) desactivó el GO mientras la corrutina de color
    // estaba en curso, OnEnable asegura que el color refleje CurrentLevel al volver.
    private void OnEnable()
    {
        if (backgroundImage != null && CurrentLevel != RiskLevel.None)
            backgroundImage.color = GetColor(CurrentLevel);
    }

    // ── Animación de color (solo cosmética) ──────────────────────────────────
    private IEnumerator ColorTransitionRoutine(Color from, Color to)
    {
        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            if (backgroundImage != null)
                backgroundImage.color = Color.Lerp(from, to,
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / transitionDuration)));
            yield return null;
        }
        if (backgroundImage != null) backgroundImage.color = to;
        _transitionRoutine = null;
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
    private Color GetColor(RiskLevel level) => level switch
    {
        RiskLevel.N1 => colorN1,
        RiskLevel.N2 => colorN2,
        RiskLevel.N3 => colorN3,
        RiskLevel.N4 => colorN4,
        _            => Color.gray,
    };

    private string GetLevelName(RiskLevel level) => level switch
    {
        RiskLevel.N1 => levelNameN1,
        RiskLevel.N2 => levelNameN2,
        RiskLevel.N3 => levelNameN3,
        RiskLevel.N4 => levelNameN4,
        _            => "",
    };

    private StageRiskConfig FindStageConfig(StageManager.Stage stage)
    {
        if (stageRiskDefaults == null) return null;
        foreach (var c in stageRiskDefaults)
            if (c.stage == stage) return c;
        return null;
    }
}
