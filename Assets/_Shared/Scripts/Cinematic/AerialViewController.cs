using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// AerialViewController — Cámara dron para Etapa5 (Sistema 12).
///
/// Al entrar a la etapa configurada, anima la cámara suavemente desde el suelo
/// hasta una posición elevada que orbita lentamente el punto central de la escena.
/// Al salir de esa etapa, desciende y devuelve el control a ARCameraController.
///
/// Integración:
///   - Se suscribe a StageManager.OnStageChanged (mismo patrón que AudioStageManager).
///   - Llama ARCameraController.SetAerialMode(true/false) para ceder/recuperar el control.
///   - No modifica StageManager ni ningún otro sistema.
///
/// Setup en escena:
///   1. Crear GameObject vacío "AerialViewController" en la raíz.
///   2. Adjuntar este script.
///   3. En stageConfigs añadir una entrada con stage = Etapa5.
///   4. (Opcional) Crear un Transform vacío "SceneCenter" en el mapa
///      y arrastrarlo a pivotTarget para definir el punto de órbita.
///   5. cameraController se busca automáticamente si queda vacío.
/// </summary>
public class AerialViewController : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static AerialViewController Instance { get; private set; }

    // ── Datos por etapa ───────────────────────────────────────────────────────
    [Serializable]
    public class AerialConfig
    {
        [Tooltip("Etapa que activa la vista aérea.")]
        public StageManager.Stage stage;

        [Header("Punto de órbita")]
        [Tooltip("Transform que marca el centro de la órbita. Null = origen de la escena.")]
        public Transform pivotTarget;

        [Header("Posición aérea")]
        [Tooltip("Altura sobre el pivote (unidades Unity).")]
        [Range(10f, 200f)]
        public float height = 40f;

        [Tooltip("Radio horizontal de la órbita.")]
        [Range(5f, 100f)]
        public float orbitRadius = 25f;

        [Header("Movimiento")]
        [Tooltip("Velocidad de órbita automática (grados/segundo). 0 = estática.")]
        [Range(0f, 60f)]
        public float autoOrbitSpeed = 8f;

        [Header("Cámara")]
        [Tooltip("Field of View durante la vista aérea.")]
        [Range(20f, 90f)]
        public float fieldOfView = 50f;

        [Header("Transición")]
        [Tooltip("Tiempo de ascenso desde el suelo (segundos).")]
        [Range(0.5f, 8f)]
        public float ascentDuration = 2.5f;

        [Tooltip("Tiempo de descenso al salir (segundos).")]
        [Range(0.5f, 8f)]
        public float descentDuration = 2f;

        [Header("Auto-salida")]
        [Tooltip("Salir automáticamente después de N segundos. 0 = no salir.")]
        [Range(0f, 300f)]
        public float autoExitAfterSeconds = 0f;
    }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Configuración de vista aérea por etapa")]
    [Tooltip("Una entrada por etapa que deba activar la vista aérea (normalmente solo Etapa5).")]
    public AerialConfig[] stageConfigs = new AerialConfig[]
    {
        new AerialConfig
        {
            stage            = StageManager.Stage.Etapa5,
            height           = 40f,
            orbitRadius      = 25f,
            autoOrbitSpeed   = 8f,
            fieldOfView      = 50f,
            ascentDuration   = 2.5f,
            descentDuration  = 2f,
            autoExitAfterSeconds = 0f
        }
    };

    [Header("Referencias")]
    [Tooltip("ARCameraController de la Main Camera. Se busca automáticamente si queda vacío.")]
    public ARCameraController cameraController;

    [Header("Input (PC)")]
    [Tooltip("Sensibilidad del arrastre de mouse para girar la órbita manualmente.")]
    [Range(0f, 5f)]
    public float mouseDragSensitivity = 1.5f;

    [Tooltip("Velocidad de zoom con la rueda del mouse (ajusta altura y radio).")]
    [Range(0f, 20f)]
    public float scrollZoomSpeed = 8f;

    [Header("Activación automática")]
    [Tooltip("Si está desactivado, OnStageChanged no activa la cámara aérea automáticamente.\n" +
             "Útil cuando otra ruta (UIManager, botón manual) maneja la activación.")]
    public bool autoActivateOnStageChange = false;

    [Header("Debug")]
    public bool debugLogs = true;

    // ── Estado ────────────────────────────────────────────────────────────────
    public bool IsActive { get; private set; }

    // ── Internos ──────────────────────────────────────────────────────────────
    private AerialConfig  _config;
    private float         _orbitAngle;
    private float         _liveRadius;
    private float         _liveHeight;
    private Vector3       _savedPosition;
    private Quaternion    _savedRotation;
    private float         _savedFOV;
    private Camera        _cam;
    private Transform     _camTransform;
    private Coroutine     _ascentRoutine;
    private Coroutine     _autoExitRoutine;
    private bool          _inTransition;    // true durante ascenso/descenso

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (cameraController == null)
            cameraController = FindObjectOfType<ARCameraController>();

        if (cameraController != null)
        {
            _cam          = cameraController.GetComponent<Camera>();
            _camTransform = cameraController.transform;
        }

        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged += OnStageChanged;
    }

    private void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;
    }

    // ── Reacción al cambio de etapa ───────────────────────────────────────────
    private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
    {
        if (!autoActivateOnStageChange) return;

        AerialConfig config = FindConfig(current);

        if (config != null)
            Activate(config);
        else if (IsActive)
            Deactivate();
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Activa la vista aérea con la configuración dada.
    /// Llamado automáticamente por OnStageChanged o desde UIManager.
    /// </summary>
    public void Activate(AerialConfig config)
    {
        if (IsActive || _camTransform == null) return;

        IsActive = true;
        _config  = config;

        // Guardar estado de la cámara para restaurar al salir
        _savedPosition = _camTransform.position;
        _savedRotation = _camTransform.rotation;
        _savedFOV      = _cam != null ? _cam.fieldOfView : 60f;

        // Ángulo inicial = dirección actual del jugador (sin salto visual)
        _orbitAngle = _camTransform.eulerAngles.y;
        _liveRadius = config.orbitRadius;
        _liveHeight = config.height;

        // Ceder control de la cámara
        cameraController?.SetAerialMode(true);
        StageManager.Instance?.SetPlayerInputBlocked(true);

        if (_ascentRoutine != null) StopCoroutine(_ascentRoutine);
        _ascentRoutine = StartCoroutine(AscentRoutine());

        if (config.autoExitAfterSeconds > 0f)
            _autoExitRoutine = StartCoroutine(AutoExitRoutine(config.autoExitAfterSeconds));

        if (debugLogs) Debug.Log($"[AerialView] Activado — Etapa {config.stage}.");
    }

    /// <summary>
    /// Desactiva la vista aérea y devuelve la cámara al suelo.
    /// Llamado automáticamente al cambiar de etapa o desde UIManager.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive) return;

        if (_autoExitRoutine != null) { StopCoroutine(_autoExitRoutine); _autoExitRoutine = null; }
        if (_ascentRoutine   != null) { StopCoroutine(_ascentRoutine);   _ascentRoutine   = null; }

        StartCoroutine(DescentRoutine());
    }

    // ── LateUpdate: órbita ───────────────────────────────────────────────────
    private void LateUpdate()
    {
        if (!IsActive || _inTransition || _camTransform == null) return;

        HandleInput();
        UpdateOrbitPosition();
    }

    // ── Input (PC / mouse) ────────────────────────────────────────────────────
    private void HandleInput()
    {
        // Arrastre de mouse: rotar órbita manualmente
        if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
        {
            float dx = Input.GetAxis("Mouse X");
            _orbitAngle -= dx * mouseDragSensitivity * 60f * Time.deltaTime;
        }
        else
        {
            // Sin input: órbita automática
            _orbitAngle += _config.autoOrbitSpeed * Time.deltaTime;
        }

        if (_orbitAngle > 360f)  _orbitAngle -= 360f;
        if (_orbitAngle <   0f)  _orbitAngle += 360f;

        // Rueda del mouse: zoom (ajusta altura y radio proporcionalmente)
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            _liveHeight = Mathf.Clamp(_liveHeight - scroll * scrollZoomSpeed * 15f, 10f, 200f);
            _liveRadius = Mathf.Clamp(_liveRadius - scroll * scrollZoomSpeed *  8f,  5f, 100f);
        }
    }

    // ── Posición de órbita ────────────────────────────────────────────────────
    private void UpdateOrbitPosition()
    {
        Vector3 pivot = GetPivot();
        float   rad   = _orbitAngle * Mathf.Deg2Rad;

        Vector3 pos = new Vector3(
            pivot.x + _liveRadius * Mathf.Sin(rad),
            pivot.y + _liveHeight,
            pivot.z + _liveRadius * Mathf.Cos(rad));

        _camTransform.position = pos;
        _camTransform.rotation = Quaternion.LookRotation(pivot - pos, Vector3.up);

        if (_cam != null) _cam.fieldOfView = _config.fieldOfView;
    }

    private Vector3 GetPivot()
    {
        return _config?.pivotTarget != null ? _config.pivotTarget.position : Vector3.zero;
    }

    // ── Corrutinas de transición ──────────────────────────────────────────────
    private IEnumerator AscentRoutine()
    {
        _inTransition = true;

        float      elapsed   = 0f;
        float      duration  = _config.ascentDuration;
        Vector3    startPos  = _camTransform.position;
        Quaternion startRot  = _camTransform.rotation;
        float      startFOV  = _cam != null ? _cam.fieldOfView : 60f;

        // Destino del ascenso (posición orbital inicial)
        Vector3 pivot  = GetPivot();
        float   rad    = _orbitAngle * Mathf.Deg2Rad;
        Vector3 endPos = new Vector3(
            pivot.x + _liveRadius * Mathf.Sin(rad),
            pivot.y + _liveHeight,
            pivot.z + _liveRadius * Mathf.Cos(rad));
        Quaternion endRot = Quaternion.LookRotation(pivot - endPos, Vector3.up);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

            _camTransform.position = Vector3.Lerp(startPos, endPos, t);
            _camTransform.rotation = Quaternion.Slerp(startRot, endRot, t);
            if (_cam != null) _cam.fieldOfView = Mathf.Lerp(startFOV, _config.fieldOfView, t);

            yield return null;
        }

        _inTransition  = false;
        _ascentRoutine = null;
    }

    private IEnumerator DescentRoutine()
    {
        _inTransition = true;

        float      elapsed   = 0f;
        float      duration  = _config?.descentDuration ?? 2f;
        Vector3    startPos  = _camTransform.position;
        Quaternion startRot  = _camTransform.rotation;
        float      startFOV  = _cam != null ? _cam.fieldOfView : 50f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

            _camTransform.position = Vector3.Lerp(startPos, _savedPosition, t);
            _camTransform.rotation = Quaternion.Slerp(startRot, _savedRotation, t);
            if (_cam != null) _cam.fieldOfView = Mathf.Lerp(startFOV, _savedFOV, t);

            yield return null;
        }

        // Restaurar estado exacto
        _camTransform.position = _savedPosition;
        _camTransform.rotation = _savedRotation;
        if (_cam != null) _cam.fieldOfView = _savedFOV;

        IsActive      = false;
        _config       = null;
        _inTransition = false;

        cameraController?.SetAerialMode(false);
        StageManager.Instance?.SetPlayerInputBlocked(false);

        if (debugLogs) Debug.Log("[AerialView] Desactivado, control devuelto.");
    }

    private IEnumerator AutoExitRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        Deactivate();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private AerialConfig FindConfig(StageManager.Stage stage)
    {
        if (stageConfigs == null) return null;
        foreach (var c in stageConfigs)
            if (c.stage == stage) return c;
        return null;
    }
}
