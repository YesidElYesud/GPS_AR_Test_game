using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// NpcDialoguePanel v2 — Panel de conversación con NPC.
///
/// Responsabilidades de ESTE script:
///   - Mostrar foto, nombre y texto del NPC.
///   - Delegar toda la lógica de opciones a MultipleChoicePanel (campo choicePanel).
///   - Bloquear/desbloquear el input del jugador.
///   - Al responder correctamente: esperar correctAnswerDelay → NextStage() → cerrar.
///   - Al responder incorrectamente: solo registrarlo (MultipleChoicePanel ya muestra feedback).
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
    private NpcDialogueData   _currentData;
    private HotspotController _sourceHotspot;
    private Coroutine         _correctRoutine;
    private Coroutine         _wrongRoutine;
    private System.Action     _onCorrectCallback;

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
    /// Llamado desde HotspotController (NpcConversation y SiataCall).
    /// </summary>
    public void Show(NpcDialogueData data, HotspotController source, System.Action onCorrect = null)
    {
        if (data == null)
        {
            Debug.LogWarning("[NpcDialoguePanel] NpcDialogueData es null.");
            return;
        }

        _currentData       = data;
        _sourceHotspot     = source;
        _onCorrectCallback = onCorrect;

        PopulateNpcInfo();
        SetupChoicePanel();

        gameObject.SetActive(true);
        BlockInput(true);
    }

    /// <summary>Cierra el panel, limpia el estado y desbloquea el input.</summary>
    public void Hide()
    {
        if (_correctRoutine != null) { StopCoroutine(_correctRoutine); _correctRoutine = null; }
        if (_wrongRoutine   != null) { StopCoroutine(_wrongRoutine);   _wrongRoutine   = null; }

        // Desconectar eventos para evitar dobles disparos
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

        if (npcNameText  != null) npcNameText.text  = _currentData.npcName;
        if (dialogueText != null) dialogueText.text  = _currentData.npcText;
    }

    // ── Configuración de opciones ─────────────────────────────────────────────
    private void SetupChoicePanel()
    {
        if (choicePanel == null)
        {
            Debug.LogWarning("[NpcDialoguePanel] choicePanel no asignado en el Inspector.");
            return;
        }

        // Suscribir antes de SetOptions para que los eventos estén listos
        UnsubscribeChoiceEvents();
        choicePanel.OnCorrect += HandleCorrectAnswer;
        choicePanel.OnWrong   += HandleWrongAnswer;
        choicePanel.OnRetry   += HandleRetry;

        choicePanel.SetOptions(_currentData.options);
    }

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
        _correctRoutine = StartCoroutine(CorrectAnswerRoutine());
    }

    private void HandleRetry()
    {
        // El jugador pulsó "Intentar de nuevo" — cancelar el auto-reintento por timer.
        if (_wrongRoutine != null) { StopCoroutine(_wrongRoutine); _wrongRoutine = null; }
    }

    private void HandleWrongAnswer()
    {
        // MultipleChoicePanel muestra el feedback rojo y el botón de reintento (si está asignado).
        // Como fallback, relanzamos las opciones automáticamente tras un delay.
        if (_wrongRoutine != null) StopCoroutine(_wrongRoutine);
        _wrongRoutine = StartCoroutine(WrongAnswerRoutine());
    }

    private IEnumerator WrongAnswerRoutine()
    {
        float delay = _currentData != null ? _currentData.correctAnswerDelay : 1.5f;
        yield return new WaitForSeconds(delay);
        _wrongRoutine = null;
        if (_currentData != null) SetupChoicePanel();
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

        // Guardar callback antes de Hide() porque Hide() lo limpia
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
