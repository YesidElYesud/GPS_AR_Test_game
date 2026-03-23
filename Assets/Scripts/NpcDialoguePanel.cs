using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// NpcDialoguePanel — Panel de conversación con NPC y selección de opciones múltiples.
///
/// Responsabilidades:
///   - Mostrar foto, nombre y texto del NPC.
///   - Generar dinámicamente los botones de opción desde NpcDialogueData.options.
///   - Al responder correctamente: feedback verde → espera → NextStage() → cierra.
///   - Al responder incorrectamente: feedback rojo → botón "Intentar de nuevo".
///   - Bloquea/desbloquea el input del jugador mientras está activo.
///
/// También recibe llamadas de tipo SiataCall (misma lógica, visual diferente en Sistema 10).
///
/// Setup en editor:
///   1. Crear panel hijo del Canvas llamado "NpcDialoguePanel" (inactivo por defecto).
///   2. Adjuntar este script al panel.
///   3. Crear y asignar un prefab "OptionButton" con Button + TextMeshProUGUI hijo.
///   4. Asignar todos los campos desde el Inspector.
///   5. En HotspotData (tipo NpcConversation), asignar el NpcDialogueData correspondiente.
/// </summary>
public class NpcDialoguePanel : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static NpcDialoguePanel Instance { get; private set; }

    // ── Inspector: NPC ────────────────────────────────────────────────────────
    [Header("Sección NPC")]
    [Tooltip("Image que muestra la foto/sprite del NPC. Se oculta si el NPC no tiene foto.")]
    public Image npcPhoto;

    [Tooltip("Texto con el nombre del NPC")]
    public TextMeshProUGUI npcNameText;

    [Tooltip("Texto principal del diálogo del NPC")]
    public TextMeshProUGUI dialogueText;

    // ── Inspector: Opciones ───────────────────────────────────────────────────
    [Header("Sección de opciones")]
    [Tooltip("GameObject padre que contiene los botones de opción generados.\n" +
             "Se oculta cuando se muestra el feedback.")]
    public GameObject optionsSection;

    [Tooltip("Transform que actúa como contenedor (Layout Group) para los botones generados.")]
    public Transform optionsContainer;

    [Tooltip("Prefab del botón de opción. Debe tener:\n" +
             "  • Button component en la raíz\n" +
             "  • TextMeshProUGUI hijo (para el texto de la opción)")]
    public GameObject optionButtonPrefab;

    // ── Inspector: Feedback ───────────────────────────────────────────────────
    [Header("Sección de feedback")]
    [Tooltip("GameObject que contiene el feedback. Se activa tras responder.")]
    public GameObject feedbackSection;

    [Tooltip("Texto explicativo del feedback (correcto o incorrecto)")]
    public TextMeshProUGUI feedbackText;

    [Tooltip("Image de fondo del feedback (cambia de color según la respuesta)")]
    public Image feedbackBackground;

    [Tooltip("Botón 'Intentar de nuevo'. Solo visible tras respuesta incorrecta.")]
    public Button retryButton;

    // ── Inspector: Colores ────────────────────────────────────────────────────
    [Header("Colores de feedback")]
    public Color correctColor   = new Color(0.13f, 0.69f, 0.30f, 0.95f);
    public Color incorrectColor = new Color(0.78f, 0.18f, 0.18f, 0.95f);

    // ── Internos ──────────────────────────────────────────────────────────────
    private NpcDialogueData       _currentData;
    private HotspotController     _sourceHotspot;
    private List<GameObject>      _spawnedButtons = new List<GameObject>();
    private Coroutine             _correctAnswerCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        gameObject.SetActive(false);
    }

    private void Start()
    {
        if (retryButton != null)
            retryButton.onClick.AddListener(OnRetry);
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Abre el panel con los datos del NpcDialogueData indicado.
    /// Llamado desde HotspotController cuando actionType = NpcConversation o SiataCall.
    /// </summary>
    public void Show(NpcDialogueData data, HotspotController source)
    {
        if (data == null)
        {
            Debug.LogWarning("[NpcDialoguePanel] NpcDialogueData es null. No se puede mostrar el panel.");
            return;
        }

        _currentData   = data;
        _sourceHotspot = source;

        PopulateNpcInfo();
        ShowOptionsSection();
        gameObject.SetActive(true);
        BlockInput(true);
    }

    /// <summary>Cierra el panel y desbloquea el input.</summary>
    public void Hide()
    {
        if (_correctAnswerCoroutine != null)
        {
            StopCoroutine(_correctAnswerCoroutine);
            _correctAnswerCoroutine = null;
        }

        ClearSpawnedButtons();
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
    private void PopulateNpcInfo()
    {
        if (_currentData == null) return;

        // Foto del NPC
        if (npcPhoto != null)
        {
            bool hasPhoto = _currentData.npcPhoto != null;
            npcPhoto.gameObject.SetActive(hasPhoto);
            if (hasPhoto) npcPhoto.sprite = _currentData.npcPhoto;
        }

        // Nombre
        if (npcNameText != null)
            npcNameText.text = _currentData.npcName;

        // Texto de diálogo
        if (dialogueText != null)
            dialogueText.text = _currentData.npcText;
    }

    private void ShowOptionsSection()
    {
        ClearSpawnedButtons();

        if (optionsSection  != null) optionsSection.SetActive(true);
        if (feedbackSection != null) feedbackSection.SetActive(false);

        if (_currentData.options == null || _currentData.options.Length == 0)
        {
            Debug.LogWarning($"[NpcDialoguePanel] '{_currentData.npcName}' no tiene opciones configuradas.");
            return;
        }

        foreach (DialogueOption option in _currentData.options)
        {
            if (optionButtonPrefab == null || optionsContainer == null) break;

            GameObject btnGo = Instantiate(optionButtonPrefab, optionsContainer);
            _spawnedButtons.Add(btnGo);

            // Asignar texto al botón
            TextMeshProUGUI label = btnGo.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = option.optionText;

            // Capturar la opción en una variable local para el closure del lambda
            DialogueOption captured = option;
            Button btn = btnGo.GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(() => OnOptionSelected(captured));
        }
    }

    private void ClearSpawnedButtons()
    {
        foreach (GameObject go in _spawnedButtons)
            if (go != null) Destroy(go);
        _spawnedButtons.Clear();
    }

    // ── Lógica de selección ───────────────────────────────────────────────────
    private void OnOptionSelected(DialogueOption option)
    {
        // Cambiar a sección de feedback
        if (optionsSection  != null) optionsSection.SetActive(false);
        if (feedbackSection != null) feedbackSection.SetActive(true);

        // Texto de feedback
        if (feedbackText != null)
            feedbackText.text = option.feedbackText;

        if (option.isCorrect)
        {
            // Feedback verde — avanzar etapa tras pausa
            if (feedbackBackground != null) feedbackBackground.color = correctColor;
            if (retryButton        != null) retryButton.gameObject.SetActive(false);
            _correctAnswerCoroutine = StartCoroutine(CorrectAnswerRoutine());
        }
        else
        {
            // Feedback rojo — permitir reintento
            if (feedbackBackground != null) feedbackBackground.color = incorrectColor;
            if (retryButton        != null) retryButton.gameObject.SetActive(true);
        }
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

    private void OnRetry()
    {
        ShowOptionsSection();
    }

    // ── Input ─────────────────────────────────────────────────────────────────
    private void BlockInput(bool block)
    {
        if (StageManager.Instance != null)
            StageManager.Instance.SetPlayerInputBlocked(block);
    }
}
