using UnityEngine;
using System;

/// <summary>
/// StageManager — Núcleo de progresión de la experiencia SATC.
///
/// Responsabilidades:
///   - Mantener la etapa actual (Intro → Etapa1 … Etapa5).
///   - Activar/desactivar GameObjects de escena al cambiar de etapa.
///   - Disparar el evento OnStageChanged para que otros sistemas reaccionen.
///   - Exponer NextStage() y GoToStage() como API pública.
///
/// Setup en editor:
///   1. Crear un GameObject vacío "StageManager" en la escena raíz.
///   2. Adjuntar este script.
///   3. Asignar ARCameraController (o dejarlo vacío — lo busca automáticamente).
///   4. En stageConfigs, añadir 6 entradas (índice 0=Intro … 5=Etapa5)
///      y arrastrar los GameObjects a activar/desactivar en cada etapa.
///   5. Elegir startStage (normalmente Intro).
/// </summary>

// ── Datos de configuración por etapa ─────────────────────────────────────────
[Serializable]
public class StageConfig
{
    [Tooltip("Nombre descriptivo (solo para el editor, no afecta lógica)")]
    public string stageName;

    [Tooltip("GameObjects que se activan al entrar a esta etapa")]
    public GameObject[] objectsToActivate;

    [Tooltip("GameObjects que se desactivan al entrar a esta etapa")]
    public GameObject[] objectsToDeactivate;
}

// ── StageManager ──────────────────────────────────────────────────────────────
public class StageManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static StageManager Instance { get; private set; }

    // ── Enum de etapas ────────────────────────────────────────────────────────
    public enum Stage
    {
        Intro   = 0,
        Etapa1  = 1,
        Etapa2  = 2,
        Etapa3  = 3,
        Etapa4  = 4,
        Etapa5  = 5
    }

    // ── Evento público ────────────────────────────────────────────────────────
    /// <summary>
    /// Se dispara cada vez que la etapa cambia.
    /// Firma: (Stage etapaAnterior, Stage etapaNueva)
    /// Suscribirse desde cualquier sistema: AudioStageManager, UIManager, etc.
    /// </summary>
    public event Action<Stage, Stage> OnStageChanged;

    // ── Propiedades públicas ──────────────────────────────────────────────────
    public Stage CurrentStage { get; private set; } = Stage.Intro;

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Configuración por etapa")]
    [Tooltip("6 entradas: índice 0=Intro, 1=Etapa1, 2=Etapa2, 3=Etapa3, 4=Etapa4, 5=Etapa5")]
    public StageConfig[] stageConfigs = new StageConfig[6];

    [Header("Referencias")]
    [Tooltip("ARCameraController de la Main Camera. Se busca automáticamente si queda vacío.")]
    public ARCameraController cameraController;

    [Header("Debug")]
    [Tooltip("Etapa con la que arranca la escena al presionar Play.")]
    public Stage startStage = Stage.Intro;

    [Tooltip("Muestra en consola cada transición de etapa.")]
    public bool debugLogs = true;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (cameraController == null)
            cameraController = FindObjectOfType<ARCameraController>();

        // Forzar la etapa inicial aunque sea la misma que CurrentStage por defecto
        Stage initial = startStage;
        CurrentStage  = (Stage)(((int)initial - 1 + 6) % 6); // valor diferente para forzar el cambio
        GoToStage(initial);
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Avanza una etapa hacia adelante.
    /// Llamar desde MultipleChoicePanel al responder correctamente.
    /// </summary>
    public void NextStage()
    {
        int next = (int)CurrentStage + 1;
        if (next > (int)Stage.Etapa5)
        {
            if (debugLogs) Debug.Log("[StageManager] La experiencia ha finalizado (Etapa5 completada).");
            return;
        }
        GoToStage((Stage)next);
    }

    /// <summary>
    /// Salta directamente a la etapa indicada.
    /// Útil para debugging o para que WelcomePanel inicie en Etapa1.
    /// </summary>
    public void GoToStage(Stage target)
    {
        if (target == CurrentStage) return;

        Stage previous = CurrentStage;
        CurrentStage   = target;

        if (debugLogs)
            Debug.Log($"[StageManager] {previous} → {target}");

        ApplyStageConfig(target);
        OnStageChanged?.Invoke(previous, target);
    }

    /// <summary>
    /// Bloquea o desbloquea el input del jugador delegando en ARCameraController.
    /// Llamar desde WelcomePanel, CinematicManager, NpcDialoguePanel, AerialViewController.
    /// </summary>
    public void SetPlayerInputBlocked(bool blocked)
    {
        if (cameraController != null)
            cameraController.SetInputBlocked(blocked);
    }

    // ── Privados ──────────────────────────────────────────────────────────────
    private void ApplyStageConfig(Stage stage)
    {
        int index = (int)stage;
        if (stageConfigs == null || index >= stageConfigs.Length) return;

        StageConfig config = stageConfigs[index];
        if (config == null) return;

        if (config.objectsToActivate != null)
        {
            foreach (var go in config.objectsToActivate)
                if (go != null) go.SetActive(true);
        }

        if (config.objectsToDeactivate != null)
        {
            foreach (var go in config.objectsToDeactivate)
                if (go != null) go.SetActive(false);
        }
    }
}
