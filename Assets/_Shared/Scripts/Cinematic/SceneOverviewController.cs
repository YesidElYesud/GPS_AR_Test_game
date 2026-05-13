using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// ── Config por botón de etapa ─────────────────────────────────────────────────
[System.Serializable]
public class OverviewStageConfig
{
    [Tooltip("Etapa cuyos efectos visuales (skybox, iluminación, niebla) se aplican al pulsar este botón.\n" +
             "Los objetos 3D de cada etapa los gestiona el StageManager — no se duplican aquí.")]
    public StageManager.Stage stage = StageManager.Stage.Etapa1;

    [Tooltip("Intensidad de lluvia mientras este botón está activo.")]
    public RainParticleController.RainIntensity rainIntensity = RainParticleController.RainIntensity.None;
}

// ── SceneOverviewController ───────────────────────────────────────────────────
/// <summary>
/// Hub de vista panorámica. Reutiliza un CinematicSequencer existente para el
/// bucle de cámara. Gestiona entrada/salida (fade + jugador) y los botones de
/// etapa (skybox, iluminación y lluvia). Los objetos 3D de cada etapa los
/// gestiona el StageManager — este hub no duplica esa lógica.
///
/// Integración con HotspotController:
///   · Primera entrada: HotspotController.DispatchAction() llama Enter(advancesStage, caller).
///   · Replay:          HotspotController.ReplaySequence()  llama Enter(false, null).
///   · El sequencer asignado aquí debe coincidir con el campo cameraSequencer del
///     HotspotController correspondiente (la comparación es por referencia).
///
/// Setup en escena:
///   1. Crear GameObject "SceneOverviewController" y adjuntar este script.
///   2. overviewSequencer → el CinematicSequencer del botón "Conocer Clima".
///   3. En AR_Canvas crear "SceneOverviewPanel" (inactivo por defecto) con:
///        · Botones de etapa → stageButtons[]  (uno por previewConfig)
///        · Botón "Salir"   → exitButton
///   4. fadeOverlay → la misma SequenceFadeOverlay del AR_Canvas.
///   5. Completar previewConfigs[] (uno por etapa a mostrar en el hub).
/// </summary>
public class SceneOverviewController : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static SceneOverviewController Instance { get; private set; }

    /// <summary>True mientras el hub está activo (cámara en bucle, panel visible).</summary>
    public bool IsActive => _active;

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Secuenciador de cámara")]
    [Tooltip("CinematicSequencer cuyas shots[] se recorren en bucle.\n" +
             "Debe ser el mismo al que apunta el HotspotController del hotspot 'Conocer Clima'.")]
    public CinematicSequencer overviewSequencer;

    [Header("Transición")]
    [Range(0f, 1.5f)] public float fadeDuration = 0.45f;
    [Tooltip("Image negra a pantalla completa (la misma SequenceFadeOverlay del CinematicSequencer).")]
    public Image fadeOverlay;

    [Header("UI del hub")]
    [Tooltip("Panel completo del hub. Inactivo por defecto en la escena.")]
    public GameObject overviewPanel;
    [Tooltip("Botones de etapa — uno por previewConfig. Sus OnClick se asignan automáticamente.\n" +
             "Los botones de etapas aún no alcanzadas se ocultan mientras el hub está abierto.")]
    public Button[] stageButtons;
    [Tooltip("Botón 'Salir de la vista'. Su OnClick se asigna automáticamente.")]
    public Button exitButton;
    [Tooltip("JoystickPanel del AR_Canvas.")]
    public GameObject joystickPanel;
    [Tooltip("Elementos HUD adicionales a ocultar mientras el hub está activo (StatusPanel, RiskLevelIndicator, ListadoBotnes, etc.).\n" +
             "Se restauran al salir con el mismo estado que tenían al entrar.")]
    public GameObject[] hudElements;

    [Header("Configuración de etapas preview")]
    [Tooltip("Una entrada por botón de etapa. El índice debe coincidir con stageButtons[].")]
    public OverviewStageConfig[] previewConfigs;

    [Header("Sistemas de río (preview)")]
    [Tooltip("WaterLevelController del río. Se busca automáticamente si queda vacío.")]
    public WaterLevelController waterLevelController;
    [Tooltip("WaterFlowController del río. Se busca automáticamente si queda vacío.")]
    public WaterFlowController waterFlowController;
    [Tooltip("Duración de transición del río al cambiar preview (segundos).")]
    [Range(0.2f, 4f)] public float previewWaterTransition = 1.2f;
    [Tooltip("RiverDebrisControllers de la escena. Se buscan automáticamente si queda vacío.")]
    public RiverDebrisController[] debrisControllers;
    [Tooltip("GrassWindController de la escena. Se busca automáticamente si queda vacío.")]
    public GrassWindController grassWindController;
    [Tooltip("TreeWindController de la escena. Se busca automáticamente si queda vacío.")]
    public TreeWindController treeWindController;
    [Tooltip("WetGroundController del camino. Se busca automáticamente si queda vacío.")]
    public WetGroundController wetGroundController;

    [Header("Escala de botones")]
    [Tooltip("Escala de los botones inactivos (más pequeños).")]
    [Range(0.5f, 1f)]  public float normalButtonScale = 0.82f;
    [Tooltip("Escala del botón de la etapa seleccionada actualmente.")]
    [Range(0.8f, 1.2f)] public float activeButtonScale = 1.0f;
    [Tooltip("Velocidad de la transición suave entre escalas.")]
    [Range(1f, 20f)]   public float buttonScaleSpeed   = 10f;

    [Header("Referencias")]
    [Tooltip("Se busca automáticamente en la escena si queda vacío.")]
    public RainParticleController rainController;

    // ── Estado interno ────────────────────────────────────────────────────────
    private bool               _active;
    private bool               _advancesStageOnExit;
    private HotspotController  _callerHotspot;
    private Coroutine          _transitionRoutine;
    private StageManager.Stage _realStage;
    private Vector3            _savedPos;
    private Quaternion         _savedRot;
    private bool               _joystickWasActive;
    private bool[]             _hudWasActive;
    private int                _currentPreviewIndex = -1;
    private RainParticleController.RainIntensity _savedRainIntensity;
    private float[]            _buttonTargetScales;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (overviewPanel != null) overviewPanel.SetActive(false);
        SetFadeAlpha(0f);
    }

    private void Start()
    {
        if (exitButton != null)
            exitButton.onClick.AddListener(Exit);

        for (int i = 0; i < stageButtons.Length; i++)
        {
            int idx = i;
            if (stageButtons[i] != null)
                stageButtons[i].onClick.AddListener(() => SetPreviewStage(idx));
        }

        if (rainController == null)
            rainController = FindObjectOfType<RainParticleController>();

        if (waterLevelController == null)
            waterLevelController = FindObjectOfType<WaterLevelController>();

        if (waterFlowController == null)
            waterFlowController = FindObjectOfType<WaterFlowController>();

        if (debrisControllers == null || debrisControllers.Length == 0)
            debrisControllers = FindObjectsOfType<RiverDebrisController>();

        if (grassWindController == null)
            grassWindController = FindObjectOfType<GrassWindController>();
        if (treeWindController == null)
            treeWindController = FindObjectOfType<TreeWindController>();
        if (wetGroundController == null)
            wetGroundController = FindObjectOfType<WetGroundController>();
    }

    private void Update()
    {
        if (!_active || stageButtons == null || _buttonTargetScales == null) return;

        for (int i = 0; i < stageButtons.Length; i++)
        {
            if (stageButtons[i] == null) continue;
            var t       = stageButtons[i].transform;
            float cur   = t.localScale.x;
            float tgt   = _buttonTargetScales[i];
            float next  = Mathf.Lerp(cur, tgt, Time.deltaTime * buttonScaleSpeed);
            t.localScale = new Vector3(next, next, 1f);
        }
    }

    // ── API pública ────────────────────────────────────────────────────────────

    /// <summary>
    /// Abre el hub de vista panorámica.
    /// Llamado por HotspotController: Enter(data.sequenceAdvancesStage, this) en primer contacto,
    /// o Enter(false, null) desde ReplaySequence().
    /// </summary>
    public void Enter(bool advancesStageOnExit, HotspotController caller)
    {
        if (_active) return;
        if (overviewSequencer == null)
        {
            Debug.LogWarning("[SceneOverview] overviewSequencer no asignado.", this);
            return;
        }

        _active              = true;
        _advancesStageOnExit = advancesStageOnExit;
        _callerHotspot       = caller;
        _realStage           = StageManager.Instance != null
                                ? StageManager.Instance.CurrentStage
                                : StageManager.Stage.Etapa1;

        // Guardar estado del jugador
        var cam   = Camera.main;
        _savedPos = cam.transform.position;
        _savedRot = cam.transform.rotation;

        // Joystick
        _joystickWasActive = joystickPanel != null && joystickPanel.activeSelf;
        if (joystickPanel != null) joystickPanel.SetActive(false);

        // HUD: guardar estado y ocultar
        if (hudElements != null && hudElements.Length > 0)
        {
            _hudWasActive = new bool[hudElements.Length];
            for (int i = 0; i < hudElements.Length; i++)
            {
                _hudWasActive[i] = hudElements[i] != null && hudElements[i].activeSelf;
                if (hudElements[i] != null) hudElements[i].SetActive(false);
            }
        }

        // Capturar lluvia activa antes de cualquier cambio
        _savedRainIntensity = rainController != null
            ? rainController.intensity
            : RainParticleController.RainIntensity.None;

        // Tomar control de cámara e input
        cam.GetComponent<ARCameraController>()?.SetAerialMode(true);
        StageManager.Instance?.SetPlayerInputBlocked(true);

        // Mostrar UI del hub
        if (overviewPanel != null) overviewPanel.SetActive(true);

        // Arrancar: fade → bucle → reveal
        if (_transitionRoutine != null) StopCoroutine(_transitionRoutine);
        _transitionRoutine = StartCoroutine(EnterRoutine());
    }

    /// <summary>Llamado por el botón 'Salir' del hub.</summary>
    public void Exit()
    {
        if (!_active) return;
        _active = false;
        overviewSequencer?.StopLoop();
        if (_transitionRoutine != null) StopCoroutine(_transitionRoutine);
        _transitionRoutine = StartCoroutine(ExitRoutine());
    }

    /// <summary>
    /// Aplica la etapa visual del botón de índice 'index' sin tocar StageManager.CurrentStage.
    /// Solo funciona con botones desbloqueados (etapa ≤ etapa real actual).
    /// </summary>
    public void SetPreviewStage(int index)
    {
        if (previewConfigs == null || index < 0 || index >= previewConfigs.Length) return;
        if (index == _currentPreviewIndex) return;

        // Bloquear si la etapa aún no ha sido alcanzada
        if (StageManager.Instance != null &&
            (int)previewConfigs[index].stage > (int)StageManager.Instance.CurrentStage)
            return;

        _currentPreviewIndex = index;
        var config = previewConfigs[index];

        // Efectos visuales (skybox, iluminación, niebla) — objetos 3D los gestiona StageManager
        VisualEffectsStageController.Instance?.ForceApplyStage((int)config.stage, fade: true);

        // Lluvia — usa los stagePresets de RainParticleController para garantizar que N1 = None
        if (rainController != null)
            rainController.ApplyStageIntensity(config.stage);

        // Nivel y apariencia del río
        waterLevelController?.ForceStage(config.stage, previewWaterTransition);
        waterFlowController?.ForceStage(config.stage, previewWaterTransition);

        // Escombros: visibles solo si la etapa preview los activaría en gameplay
        if (debrisControllers != null)
            foreach (var dc in debrisControllers)
                if (dc != null) ApplyDebrisForPreviewStage(dc, config.stage);

        // Viento de hojas y árboles — sin esto quedan en la intensidad del nivel anterior
        grassWindController?.ForcePreset((int)config.stage);
        treeWindController?.ForcePreset((int)config.stage);

        // Suelo mojado: reflectividad según la etapa preview
        wetGroundController?.ForceStage(config.stage, previewWaterTransition);

        RefreshButtonStates();
    }

    // ── Corrutinas de transición ──────────────────────────────────────────────
    private IEnumerator EnterRoutine()
    {
        // Fade a negro — cubre el salto al primer shot y el cambio de visuals
        yield return StartCoroutine(DoFade(0f, 1f, fadeDuration));

        // Arrancar el bucle (snaps al primer shot bajo el fade negro)
        overviewSequencer.PlayLoop();

        // Inicializar array de escalas: todos pequeños bajo el fade (sin animación visible)
        InitButtonScalesImmediate(normalButtonScale);

        // Aplicar preset inicial: la etapa real si tiene config, si no la primera disponible
        int initialIndex = FindBestInitialPreview();
        _currentPreviewIndex = -1;  // forzar aplicación
        SetPreviewStage(initialIndex);

        // Revelar la vista
        yield return StartCoroutine(DoFade(1f, 0f, fadeDuration));
        _transitionRoutine = null;
    }

    private IEnumerator ExitRoutine()
    {
        // Fade a negro — cubre el retorno al jugador
        yield return StartCoroutine(DoFade(0f, 1f, fadeDuration));

        _currentPreviewIndex = -1;

        // Restaurar jugador
        var cam    = Camera.main;
        var arCtrl = cam.GetComponent<ARCameraController>();
        var cc     = cam.GetComponent<CharacterController>();

        if (cc != null) cc.enabled = false;
        cam.transform.position = _savedPos;
        cam.transform.rotation = _savedRot;
        if (cc != null) cc.enabled = true;

        arCtrl?.SetAerialMode(false);
        StageManager.Instance?.SetPlayerInputBlocked(false);

        // Avanzar etapa o restaurar visuals de la etapa real
        if (_advancesStageOnExit && StageManager.Instance != null)
        {
            // NextStage dispara OnStageChanged → RainParticleController y VisualEffects reaccionan automáticamente
            StageManager.Instance.NextStage();
        }
        else
        {
            VisualEffectsStageController.Instance?.ForceApplyStage((int)_realStage, fade: true);
            if (rainController != null)
                rainController.SetIntensity(_savedRainIntensity);

            // Restaurar nivel, apariencia del río y escombros a la etapa real
            waterLevelController?.ForceStage(_realStage, previewWaterTransition);
            waterFlowController?.ForceStage(_realStage, previewWaterTransition);
            if (debrisControllers != null)
                foreach (var dc in debrisControllers)
                    if (dc != null) ApplyDebrisForPreviewStage(dc, _realStage);

            // Restaurar viento a la etapa real
            grassWindController?.ForcePreset((int)_realStage);
            treeWindController?.ForcePreset((int)_realStage);

            // Restaurar suelo mojado a la etapa real
            wetGroundController?.ForceStage(_realStage, previewWaterTransition);
        }

        // Notificar al hotspot que terminó la interacción
        _callerHotspot?.ClosePanel();
        _callerHotspot = null;

        // Restaurar joystick y ocultar panel
        if (_joystickWasActive && joystickPanel != null) joystickPanel.SetActive(true);

        // Restaurar HUD
        if (hudElements != null && _hudWasActive != null)
        {
            for (int i = 0; i < hudElements.Length && i < _hudWasActive.Length; i++)
                if (hudElements[i] != null) hudElements[i].SetActive(_hudWasActive[i]);
        }

        if (overviewPanel != null) overviewPanel.SetActive(false);

        // Resetear escalas al tamaño normal (por si el panel se vuelve a abrir)
        InitButtonScalesImmediate(1f);

        // Revelar vista del jugador
        yield return StartCoroutine(DoFade(1f, 0f, fadeDuration));
        _transitionRoutine = null;
    }

    // ── Botones: visibilidad, estado activo y escala objetivo ────────────────
    private void RefreshButtonStates()
    {
        if (stageButtons == null || previewConfigs == null) return;

        int reachedStage = StageManager.Instance != null ? (int)StageManager.Instance.CurrentStage : 0;

        // Asegurar que el array de escalas objetivo existe
        if (_buttonTargetScales == null || _buttonTargetScales.Length != stageButtons.Length)
        {
            _buttonTargetScales = new float[stageButtons.Length];
            for (int i = 0; i < _buttonTargetScales.Length; i++) _buttonTargetScales[i] = normalButtonScale;
        }

        for (int i = 0; i < stageButtons.Length; i++)
        {
            if (stageButtons[i] == null) continue;

            bool unlocked = i < previewConfigs.Length && (int)previewConfigs[i].stage <= reachedStage;
            bool isActive = (i == _currentPreviewIndex);

            // Ocultar botones de etapas aún no alcanzadas
            stageButtons[i].gameObject.SetActive(unlocked);
            if (!unlocked) continue;

            // Escala objetivo: activo = tamaño completo, inactivos = más pequeños
            _buttonTargetScales[i] = isActive ? activeButtonScale : normalButtonScale;

            // Resaltar el botón activo y deshabilitar su interactividad
            stageButtons[i].interactable = !isActive;
            var cb = stageButtons[i].colors;
            cb.normalColor      = isActive ? new Color(0.25f, 0.55f, 1.00f) : Color.white;
            cb.highlightedColor = isActive ? new Color(0.30f, 0.65f, 1.00f) : new Color(0.90f, 0.90f, 0.90f);
            stageButtons[i].colors = cb;
        }
    }

    // Snap instantáneo de todas las escalas (se usa bajo el fade negro)
    private void InitButtonScalesImmediate(float scale)
    {
        if (stageButtons == null) return;

        if (_buttonTargetScales == null || _buttonTargetScales.Length != stageButtons.Length)
            _buttonTargetScales = new float[stageButtons.Length];

        for (int i = 0; i < stageButtons.Length; i++)
        {
            _buttonTargetScales[i] = scale;
            if (stageButtons[i] != null)
                stageButtons[i].transform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    // ── Helpers de río / escombros ────────────────────────────────────────────

    /// Activa o desactiva un RiverDebrisController según si la etapa de preview
    /// es suficiente para que debería estar activo en gameplay normal.
    private void ApplyDebrisForPreviewStage(RiverDebrisController dc, StageManager.Stage previewStage)
    {
        bool shouldBeActive = (int)previewStage >= (int)dc.activateOnStage;
        if (shouldBeActive && !dc.IsActive)  dc.Activate();
        else if (!shouldBeActive && dc.IsActive) dc.Deactivate();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve el índice del preset que coincide con la etapa real.
    /// Si no hay coincidencia, devuelve el más alto desbloqueado.
    /// </summary>
    private int FindBestInitialPreview()
    {
        if (previewConfigs == null || previewConfigs.Length == 0) return 0;

        int reachedStage = StageManager.Instance != null ? (int)StageManager.Instance.CurrentStage : 0;

        // Buscar coincidencia exacta
        for (int i = 0; i < previewConfigs.Length; i++)
            if ((int)previewConfigs[i].stage == reachedStage) return i;

        // Si no hay exacta, devolver el más alto desbloqueado
        int best = 0;
        for (int i = 0; i < previewConfigs.Length; i++)
            if ((int)previewConfigs[i].stage <= reachedStage) best = i;
        return best;
    }

    // ── Fade ─────────────────────────────────────────────────────────────────
    private IEnumerator DoFade(float from, float to, float duration)
    {
        if (fadeOverlay == null || duration <= 0f) { SetFadeAlpha(to); yield break; }
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetFadeAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        SetFadeAlpha(to);
    }

    private void SetFadeAlpha(float alpha)
    {
        if (fadeOverlay == null) return;
        var c = fadeOverlay.color;
        fadeOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }
}
