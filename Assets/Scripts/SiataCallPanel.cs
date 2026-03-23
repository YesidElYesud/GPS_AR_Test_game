using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SiataCallPanel — Simula una llamada telefónica al SIATA (Sistema de Alerta Temprana).
///
/// Diferencia visual respecto a NpcDialoguePanel:
///   - Encabezado de llamada con estado ("Llamando…" → "Conectado").
///   - Botón de colgar (HangUpButton) para cerrar el panel.
///   - Usa la misma estructura NpcDialogueData y MultipleChoicePanel.
///
/// Singleton. Llamado desde HotspotController cuando actionType = SiataCall.
///
/// Jerarquía sugerida en Canvas (hijo de AR_Canvas, inactivo por defecto):
///   SiataCallPanel
///   ├── CallHeader
///   │   ├── CallerPhoto       (Image)
///   │   ├── CallerName        (TextMeshProUGUI)
///   │   └── CallStatus        (TextMeshProUGUI)  ← "Llamando…" / "Conectado"
///   ├── DialogueText          (TextMeshProUGUI)
///   ├── MultipleChoicePanelGO ← MultipleChoicePanel.cs
///   │   ├── OptionsContainer  (con Vertical Layout Group)
///   │   └── FeedbackSection
///   │       ├── FeedbackBG    (Image)
///   │       ├── FeedbackText  (TextMeshProUGUI)
///   │       └── RetryButton   (Button)
///   └── HangUpButton          (Button)
/// </summary>
public class SiataCallPanel : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static SiataCallPanel Instance { get; private set; }

    // ── Inspector: Encabezado de llamada ──────────────────────────────────────
    [Header("Encabezado de llamada")]
    [Tooltip("Foto/avatar del operador SIATA. Se oculta si NpcDialogueData no tiene foto.")]
    public Image callerPhoto;

    [Tooltip("Nombre del operador o número de línea (ej: 'SIATA - Línea de Alerta').")]
    public TextMeshProUGUI callerNameText;

    [Tooltip("Estado de la llamada. Muestra 'Llamando…' al abrir y 'Conectado' tras el delay.")]
    public TextMeshProUGUI callStatusText;

    // ── Inspector: Cuerpo de la llamada ───────────────────────────────────────
    [Header("Cuerpo")]
    [Tooltip("Texto con el mensaje/pregunta del operador SIATA.")]
    public TextMeshProUGUI dialogueText;

    // ── Inspector: Opciones múltiples ─────────────────────────────────────────
    [Header("Panel de opciones múltiples")]
    [Tooltip("Componente hijo MultipleChoicePanel que gestiona botones A/B/C y feedback.")]
    public MultipleChoicePanel choicePanel;

    // ── Inspector: Controles ──────────────────────────────────────────────────
    [Header("Controles")]
    [Tooltip("Botón 'Colgar'. Cierra el panel sin avanzar la etapa.")]
    public Button hangUpButton;

    // ── Inspector: Comportamiento ─────────────────────────────────────────────
    [Header("Comportamiento")]
    [Tooltip("Segundos de animación 'Llamando…' antes de mostrar el diálogo y las opciones.")]
    [Range(0f, 3f)]
    public float connectingDelay = 1.5f;

    [Tooltip("Texto mostrado mientras se establece la llamada.")]
    public string connectingText = "Llamando…";

    [Tooltip("Texto mostrado cuando la llamada está activa.")]
    public string connectedText  = "Conectado";

    // ── Internos ──────────────────────────────────────────────────────────────
    private NpcDialogueData   _currentData;
    private HotspotController _sourceHotspot;
    private Coroutine         _connectRoutine;
    private Coroutine         _correctRoutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        gameObject.SetActive(false);
    }

    private void Start()
    {
        if (hangUpButton != null)
            hangUpButton.onClick.AddListener(Hide);
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Muestra el panel de llamada con los datos indicados.
    /// Llamado desde HotspotController (SiataCall).
    /// </summary>
    public void Show(NpcDialogueData data, HotspotController source)
    {
        if (data == null)
        {
            Debug.LogWarning("[SiataCallPanel] NpcDialogueData es null.");
            return;
        }

        _currentData   = data;
        _sourceHotspot = source;

        PopulateCallerInfo();

        // Ocultar diálogo y opciones hasta que la llamada "conecte"
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        if (choicePanel  != null) choicePanel.gameObject.SetActive(false);

        gameObject.SetActive(true);
        BlockInput(true);

        _connectRoutine = StartCoroutine(ConnectingRoutine());
    }

    /// <summary>Cierra el panel, limpia estado y desbloquea input.</summary>
    public void Hide()
    {
        StopAllCoroutinesInternal();
        UnsubscribeChoiceEvents();

        if (choicePanel != null) choicePanel.Clear();

        BlockInput(false);

        if (_sourceHotspot != null)
        {
            _sourceHotspot.ClosePanel();
            _sourceHotspot = null;
        }

        _currentData = null;
        gameObject.SetActive(false);
    }

    // ── Población de UI ───────────────────────────────────────────────────────
    private void PopulateCallerInfo()
    {
        if (_currentData == null) return;

        bool hasPhoto = _currentData.npcPhoto != null;
        if (callerPhoto != null)
        {
            callerPhoto.gameObject.SetActive(hasPhoto);
            if (hasPhoto) callerPhoto.sprite = _currentData.npcPhoto;
        }

        if (callerNameText  != null) callerNameText.text  = _currentData.npcName;
        if (callStatusText  != null) callStatusText.text  = connectingText;
        if (dialogueText    != null) dialogueText.text    = _currentData.npcText;
    }

    // ── Secuencia de conexión ─────────────────────────────────────────────────
    private IEnumerator ConnectingRoutine()
    {
        yield return new WaitForSeconds(connectingDelay);

        if (callStatusText != null) callStatusText.text = connectedText;

        // Mostrar diálogo y opciones al conectar
        if (dialogueText != null) dialogueText.gameObject.SetActive(true);
        if (choicePanel  != null)
        {
            choicePanel.gameObject.SetActive(true);
            SetupChoicePanel();
        }

        _connectRoutine = null;
    }

    // ── Configuración de opciones ─────────────────────────────────────────────
    private void SetupChoicePanel()
    {
        if (choicePanel == null)
        {
            Debug.LogWarning("[SiataCallPanel] choicePanel no asignado en el Inspector.");
            return;
        }

        UnsubscribeChoiceEvents();
        choicePanel.OnCorrect += HandleCorrectAnswer;
        choicePanel.OnWrong   += HandleWrongAnswer;

        choicePanel.SetOptions(_currentData.options);
    }

    private void UnsubscribeChoiceEvents()
    {
        if (choicePanel == null) return;
        choicePanel.OnCorrect -= HandleCorrectAnswer;
        choicePanel.OnWrong   -= HandleWrongAnswer;
    }

    // ── Respuestas ────────────────────────────────────────────────────────────
    private void HandleCorrectAnswer()
    {
        _correctRoutine = StartCoroutine(CorrectAnswerRoutine());
    }

    private void HandleWrongAnswer()
    {
        Debug.Log("[SiataCallPanel] Respuesta incorrecta.");
    }

    private IEnumerator CorrectAnswerRoutine()
    {
        float delay = _currentData != null ? _currentData.correctAnswerDelay : 1.5f;
        yield return new WaitForSeconds(delay);

        if (_currentData != null && _currentData.advancesStageOnCorrect)
        {
            if (StageManager.Instance != null)
                StageManager.Instance.NextStage();
        }

        Hide();
    }

    // ── Input ─────────────────────────────────────────────────────────────────
    private void BlockInput(bool block)
    {
        if (StageManager.Instance != null)
            StageManager.Instance.SetPlayerInputBlocked(block);
    }

    // ── Utilidades ────────────────────────────────────────────────────────────
    private void StopAllCoroutinesInternal()
    {
        if (_connectRoutine != null) { StopCoroutine(_connectRoutine); _connectRoutine = null; }
        if (_correctRoutine != null) { StopCoroutine(_correctRoutine); _correctRoutine = null; }
    }
}
