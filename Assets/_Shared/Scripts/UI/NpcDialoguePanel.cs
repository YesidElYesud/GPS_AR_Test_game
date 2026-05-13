using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// NpcDialoguePanel v3 — Panel de conversación con NPC.
///
/// Responsabilidades:
///   - Mostrar foto, nombre y texto del NPC.
///   - Paginar el diálogo con un botón "Continuar" generado por MultipleChoicePanel.
///   - En la última página, mostrar las opciones reales (si las hay) o un botón "Cerrar".
///   - Delegar toda la lógica de botones a MultipleChoicePanel (campo choicePanel).
///   - Bloquear/desbloquear el input del jugador.
///
/// Singleton para ser llamado desde HotspotController.
///
/// Jerarquía sugerida en Canvas:
///   NpcDialoguePanel  (inactivo por defecto)
///   ├── NpcInfoRow
///   │   ├── NpcPhoto        (Image)
///   │   └── NpcName         (TextMeshProUGUI)
///   ├── DialogueText        (TextMeshProUGUI)
///   └── MultipleChoicePanelGO  ← MultipleChoicePanel.cs aquí
///       ├── OptionsContainer   (Vertical Layout Group)
///       ├── FeedbackSection
///       │   ├── FeedbackBG     (Image)
///       │   ├── FeedbackText   (TextMeshProUGUI)
///       │   └── RetryButton    (Button)
/// </summary>
public class NpcDialoguePanel : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static NpcDialoguePanel Instance { get; private set; }

    // ── Inspector: NPC ────────────────────────────────────────────────────────
    [Header("Sección NPC")]
    [Tooltip("Imagen del NPC. Se oculta automáticamente si el NpcDialogueData no tiene foto.")]
    public Image npcPhoto;

    [Tooltip("Nombre del NPC.")]
    public TextMeshProUGUI npcNameText;

    [Tooltip("Texto principal del diálogo.")]
    public TextMeshProUGUI dialogueText;

    // ── Inspector: Opciones múltiples ─────────────────────────────────────────
    [Header("Panel de opciones múltiples")]
    [Tooltip("Componente hijo que maneja la generación de botones, feedback y reintento.")]
    public MultipleChoicePanel choicePanel;

    // ── Internos ──────────────────────────────────────────────────────────────
    private NpcDialogueData      _currentData;
    private IHotspotInteractable _sourceHotspot;
    private Coroutine         _correctRoutine;
    private Coroutine         _wrongRoutine;
    private System.Action     _onCorrectCallback;
    private string[]          _lines;
    private int               _lineIndex;

    // Opción reutilizable para botones de navegación (evita alloaciones en cada ShowLine)
    private static readonly DialogueOption _continueOption = new DialogueOption
        { optionText = "Continuar", isCorrect = true, feedbackText = "" };
    private static readonly DialogueOption _closeOption = new DialogueOption
        { optionText = "Cerrar", isCorrect = true, feedbackText = "" };
    private static readonly DialogueOption[] _continueOptions = { _continueOption };
    private static readonly DialogueOption[] _closeOptions    = { _closeOption };

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        gameObject.SetActive(false);
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Muestra el panel con los datos del diálogo indicado.
    /// Llamado desde HotspotController.
    /// </summary>
    public void Show(NpcDialogueData data, IHotspotInteractable source, System.Action onCorrect = null)
    {
        if (data == null)
        {
            Debug.LogWarning("[NpcDialoguePanel] NpcDialogueData es null.");
            return;
        }

        _currentData       = data;
        _sourceHotspot     = source;
        _onCorrectCallback = onCorrect;

        bool hasLines = data.dialogueLines != null && data.dialogueLines.Length > 0;
        _lines     = hasLines ? data.dialogueLines : new string[] { data.npcText };
        _lineIndex = 0;

        PopulateNpcInfo();
        ShowLine(0);

        gameObject.SetActive(true);
        BlockInput(true);
    }

    /// <summary>Cierra el panel, limpia el estado y desbloquea el input.</summary>
    public void Hide()
    {
        if (_correctRoutine != null) { StopCoroutine(_correctRoutine); _correctRoutine = null; }
        if (_wrongRoutine   != null) { StopCoroutine(_wrongRoutine);   _wrongRoutine   = null; }

        UnsubscribeChoiceEvents();

        if (choicePanel != null) choicePanel.Clear();

        BlockInput(false);

        if (_sourceHotspot != null)
        {
            _sourceHotspot.ClosePanel();
            _sourceHotspot = null;
        }

        _currentData       = null;
        _onCorrectCallback = null;
        gameObject.SetActive(false);
    }

    // ── Paginación ────────────────────────────────────────────────────────────
    private void ShowLine(int index)
    {
        if (_lines == null || index < 0 || index >= _lines.Length) return;

        if (dialogueText != null) dialogueText.text = _lines[index];

        bool isLast  = index >= _lines.Length - 1;
        bool hasOpts = HasOptions();

        UnsubscribeChoiceEvents();

        if (choicePanel != null)
        {
            choicePanel.OnCorrect += HandleCorrectAnswer;
            choicePanel.OnWrong   += HandleWrongAnswer;
            choicePanel.OnRetry   += HandleRetry;

            if (!isLast)
                choicePanel.SetOptions(_continueOptions);
            else if (hasOpts)
                choicePanel.SetOptions(_currentData.options);
            else
                choicePanel.SetOptions(_closeOptions);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private bool HasOptions() =>
        _currentData != null &&
        _currentData.options != null &&
        _currentData.options.Length > 0;

    private bool IsOnLastLine() => _lines == null || _lineIndex >= _lines.Length - 1;

    // ── Población de UI ───────────────────────────────────────────────────────
    private void PopulateNpcInfo()
    {
        if (_currentData == null) return;

        bool hasPhoto = _currentData.npcPhoto != null;
        if (npcPhoto != null)
        {
            npcPhoto.gameObject.SetActive(hasPhoto);
            if (hasPhoto) npcPhoto.sprite = _currentData.npcPhoto;
        }

        if (npcNameText != null) npcNameText.text = _currentData.npcName;
    }

    // ── Suscripción de eventos ────────────────────────────────────────────────
    private void UnsubscribeChoiceEvents()
    {
        if (choicePanel == null) return;
        choicePanel.OnCorrect -= HandleCorrectAnswer;
        choicePanel.OnWrong   -= HandleWrongAnswer;
        choicePanel.OnRetry   -= HandleRetry;
    }

    // ── Respuestas ────────────────────────────────────────────────────────────
    private void HandleCorrectAnswer()
    {
        if (!IsOnLastLine())
        {
            // Botón "Continuar" en página intermedia → avanzar a la siguiente línea
            _lineIndex++;
            ShowLine(_lineIndex);
            return;
        }

        // Última página
        if (HasOptions())
        {
            // Respuesta correcta a la pregunta real → cerrar panel y avanzar etapa
            _correctRoutine = StartCoroutine(CorrectAnswerRoutine());
        }
        else
        {
            // Botón "Cerrar" en última página sin pregunta
            Hide();
        }
    }

    private void HandleRetry()
    {
        if (_wrongRoutine != null) { StopCoroutine(_wrongRoutine); _wrongRoutine = null; }
    }

    private void HandleWrongAnswer()
    {
        if (_wrongRoutine != null) StopCoroutine(_wrongRoutine);
        _wrongRoutine = StartCoroutine(WrongAnswerRoutine());
    }

    private IEnumerator WrongAnswerRoutine()
    {
        float delay = _currentData != null ? _currentData.correctAnswerDelay : 1.5f;
        yield return new WaitForSeconds(delay);
        _wrongRoutine = null;
        if (_currentData != null) ShowLine(_lineIndex);
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

        System.Action walkerCallback = _onCorrectCallback;
        Hide();
        walkerCallback?.Invoke();
    }

    // ── Input ─────────────────────────────────────────────────────────────────
    private void BlockInput(bool block)
    {
        if (StageManager.Instance != null)
            StageManager.Instance.SetPlayerInputBlocked(block);
    }
}
