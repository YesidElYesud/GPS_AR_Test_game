using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// CriticalModePanel — Modal automático de alerta para etapas críticas.
/// Sistema 11 del proyecto SATCS.
///
/// Responsabilidades:
///   - Suscribirse a StageManager.OnStageChanged y mostrarse automáticamente
///     cuando la etapa activa está en la lista triggerStages.
///   - Bloquear el input del jugador mientras está visible.
///   - Mostrar contenido diferente por etapa (título + descripción + icono).
///   - Cerrarse con el botón "Entendido" sin avanzar de etapa.
///
/// Setup en escena:
///   1. En AR_Canvas crear "CriticalModePanel" (inactivo por defecto):
///        CriticalModePanel
///        ├── Overlay          (Image negro semitransparente, fullscreen stretch)
///        ├── ContentBox       (Image panel centrado)
///        │   ├── AlertIcon    (Image — ícono de sirena/alerta)
///        │   ├── TitleText    (TextMeshProUGUI)
///        │   ├── DescriptionText (TextMeshProUGUI)
///        │   └── ContinueButton  (Button — "Entendido")
///   2. Adjuntar este script al GameObject CriticalModePanel.
///   3. Asignar todas las referencias en el Inspector.
///   4. Configurar stageContents con al menos una entrada (Stage=Etapa3).
/// </summary>
public class CriticalModePanel : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static CriticalModePanel Instance { get; private set; }

    // ── Datos de contenido por etapa ──────────────────────────────────────────
    [Serializable]
    public class StageContent
    {
        [Tooltip("Etapa que dispara este modal")]
        public StageManager.Stage stage;

        [Tooltip("Título del modal (ej: '⚠ MODO CRÍTICO')")]
        public string title = "⚠ MODO CRÍTICO";

        [Tooltip("Descripción del evento crítico")]
        [TextArea(3, 6)]
        public string description = "Se ha detectado un evento crítico. Mantén la calma y sigue las instrucciones.";

        [Tooltip("Ícono opcional para esta etapa (sirena, alerta, inundación, etc.)")]
        public Sprite icon;
    }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Contenido por etapa")]
    [Tooltip("Define qué etapas disparan el modal y con qué contenido.")]
    public StageContent[] stageContents = new StageContent[]
    {
        new StageContent
        {
            stage       = StageManager.Stage.Etapa3,
            title       = "⚠ MODO CRÍTICO",
            description = "El caudal de la quebrada ha aumentado peligrosamente.\nMantén la calma y sigue las instrucciones de evacuación."
        },
        new StageContent
        {
            stage       = StageManager.Stage.Etapa4,
            title       = "⚠ ALERTA MÁXIMA",
            description = "Se registra un evento de inundación activo.\nSigue las rutas de evacuación señalizadas y contacta al SIATA."
        }
    };

    [Header("UI")]
    [Tooltip("Imagen del ícono de alerta. Se oculta si StageContent no tiene ícono.")]
    public Image alertIcon;

    [Tooltip("Texto del título del modal.")]
    public TextMeshProUGUI titleText;

    [Tooltip("Texto de descripción del evento crítico.")]
    public TextMeshProUGUI descriptionText;

    [Tooltip("Botón 'Entendido' para cerrar el modal.")]
    public Button continueButton;

    [Header("Comportamiento")]
    [Tooltip("Segundos de espera antes de mostrar el modal al entrar a la etapa.\n" +
             "Útil para dejar que otras animaciones/transiciones terminen primero.")]
    [Range(0f, 5f)]
    public float showDelay = 1f;

    // ── Internos ──────────────────────────────────────────────────────────────
    private Coroutine _showDelayRoutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        gameObject.SetActive(false);
    }

    private void Start()
    {
        if (continueButton != null)
            continueButton.onClick.AddListener(Hide);

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
        StageContent content = FindContent(current);
        if (content == null) return;

        if (_showDelayRoutine != null) StopCoroutine(_showDelayRoutine);
        _showDelayRoutine = StartCoroutine(ShowDelayed(content));
    }

    private System.Collections.IEnumerator ShowDelayed(StageContent content)
    {
        if (showDelay > 0f)
            yield return new WaitForSeconds(showDelay);

        Show(content);
        _showDelayRoutine = null;
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Muestra el modal con el contenido de la etapa actual.
    /// Puede llamarse manualmente desde otros sistemas si es necesario.
    /// </summary>
    public void Show(StageContent content)
    {
        if (content == null) return;

        PopulateUI(content);
        gameObject.SetActive(true);
        BlockInput(true);

        Debug.Log($"[CriticalModePanel] Mostrando modal para {content.stage}: {content.title}");
    }

    /// <summary>Cierra el modal y desbloquea el input. Llamado por el botón "Entendido".</summary>
    public void Hide()
    {
        if (_showDelayRoutine != null)
        {
            StopCoroutine(_showDelayRoutine);
            _showDelayRoutine = null;
        }

        BlockInput(false);
        gameObject.SetActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private StageContent FindContent(StageManager.Stage stage)
    {
        if (stageContents == null) return null;
        foreach (var content in stageContents)
            if (content.stage == stage) return content;
        return null;
    }

    private void PopulateUI(StageContent content)
    {
        if (titleText       != null) titleText.text       = content.title;
        if (descriptionText != null) descriptionText.text  = content.description;

        if (alertIcon != null)
        {
            bool hasIcon = content.icon != null;
            alertIcon.gameObject.SetActive(hasIcon);
            if (hasIcon) alertIcon.sprite = content.icon;
        }
    }

    private void BlockInput(bool block)
    {
        if (StageManager.Instance != null)
            StageManager.Instance.SetPlayerInputBlocked(block);
    }
}
