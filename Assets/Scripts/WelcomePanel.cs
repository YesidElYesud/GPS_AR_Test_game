using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// WelcomePanel — Panel de bienvenida y tutorial de controles.
///
/// Flujo:
///   1. El panel arranca activo en escena (Stage.Intro).
///   2. Bloquea el movimiento del jugador al iniciarse.
///   3. El usuario navega entre slides con Anterior / Siguiente.
///   4. En el último slide aparece "Comenzar" → desbloquea input y va a Etapa1.
///
/// Setup en editor:
///   1. Crear un panel hijo del Canvas llamado "WelcomePanel" (activo al inicio).
///   2. Adjuntar este script al panel.
///   3. Crear slides como GameObjects hijos y asignarlos al array "slides".
///   4. Asignar los botones, contador y (opcionalmente) cameraController.
///   5. En StageManager → stageConfigs[0] (Intro):
///        objectsToActivate  → WelcomePanel
///        objectsToDeactivate → (vacío, ya estaba activo)
///
/// Dependencias:
///   - StageManager.Instance  → para bloquear input y avanzar a Etapa1.
///   - ARCameraController     → fallback directo si StageManager no está listo.
/// </summary>
public class WelcomePanel : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Slides (hijos del panel, en orden)")]
    [Tooltip("Cada slide es un GameObject hijo. Se muestran de a uno en orden.")]
    public GameObject[] slides;

    [Header("Botones de navegación")]
    [Tooltip("Botón para ir al slide anterior. Se oculta en el primer slide.")]
    public Button previousButton;

    [Tooltip("Botón para ir al siguiente slide. Se oculta en el último slide.")]
    public Button nextButton;

    [Tooltip("Botón para iniciar la experiencia. Solo visible en el último slide.")]
    public Button startButton;

    [Header("Indicador de progreso (opcional)")]
    [Tooltip("Muestra '1 / 3', '2 / 3', etc. Puede quedar vacío.")]
    public TextMeshProUGUI slideCounterText;

    [Header("Debug")]
    [Tooltip("Botón para saltar el onboarding directamente (útil en editor). Puede quedar vacío.")]
    public Button skipButton;

    [Header("Referencia directa de cámara")]
    [Tooltip("Fallback si StageManager no encontró aún la cámara. Se busca automáticamente.")]
    public ARCameraController cameraController;

    // ── Internos ──────────────────────────────────────────────────────────────
    private int _currentSlide = 0;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Start()
    {
        // Buscar cámara si no está asignada (fallback)
        if (cameraController == null)
            cameraController = FindObjectOfType<ARCameraController>();

        // Bloquear input del jugador mientras el panel esté visible
        BlockPlayerInput(true);

        // Conectar botones
        if (previousButton != null) previousButton.onClick.AddListener(OnPrevious);
        if (nextButton     != null) nextButton.onClick.AddListener(OnNext);
        if (startButton    != null) startButton.onClick.AddListener(OnStart);
        if (skipButton     != null) skipButton.onClick.AddListener(OnStart);

        // Mostrar primer slide
        ShowSlide(0);
    }

    // ── Navegación ────────────────────────────────────────────────────────────
    private void OnPrevious()
    {
        if (_currentSlide > 0)
            ShowSlide(_currentSlide - 1);
    }

    private void OnNext()
    {
        if (_currentSlide < slides.Length - 1)
            ShowSlide(_currentSlide + 1);
    }

    /// <summary>
    /// Llamado al presionar "Comenzar" o el botón skip.
    /// Desbloquea input, avanza a Etapa1 y oculta este panel.
    /// </summary>
    private void OnStart()
    {
        BlockPlayerInput(false);

        if (StageManager.Instance != null)
            StageManager.Instance.GoToStage(StageManager.Stage.Etapa1);

        gameObject.SetActive(false);
    }

    // ── Visualización de slides ───────────────────────────────────────────────
    private void ShowSlide(int index)
    {
        if (slides == null || slides.Length == 0) return;

        index = Mathf.Clamp(index, 0, slides.Length - 1);
        _currentSlide = index;

        // Activar solo el slide actual
        for (int i = 0; i < slides.Length; i++)
        {
            if (slides[i] != null)
                slides[i].SetActive(i == _currentSlide);
        }

        RefreshButtons();
        UpdateCounter();
    }

    private void RefreshButtons()
    {
        bool isFirst = _currentSlide == 0;
        bool isLast  = _currentSlide >= slides.Length - 1;

        // "Anterior" solo visible si no estamos en el primero
        if (previousButton != null)
            previousButton.gameObject.SetActive(!isFirst);

        // "Siguiente" solo visible si no estamos en el último
        if (nextButton != null)
            nextButton.gameObject.SetActive(!isLast);

        // "Comenzar" solo visible en el último slide
        if (startButton != null)
            startButton.gameObject.SetActive(isLast);
    }

    private void UpdateCounter()
    {
        if (slideCounterText == null || slides == null || slides.Length == 0) return;
        slideCounterText.text = $"{_currentSlide + 1} / {slides.Length}";
    }

    // ── Bloqueo de input ─────────────────────────────────────────────────────
    /// <summary>
    /// Bloquea/desbloquea el movimiento del jugador.
    /// Intenta StageManager primero; si no está listo, actúa directamente sobre la cámara.
    /// </summary>
    private void BlockPlayerInput(bool block)
    {
        if (StageManager.Instance != null)
        {
            StageManager.Instance.SetPlayerInputBlocked(block);
        }
        else if (cameraController != null)
        {
            // Fallback directo — puede ocurrir si el orden de Start() no favorece a StageManager
            cameraController.SetInputBlocked(block);
        }
    }

    // ── API pública ───────────────────────────────────────────────────────────
    /// <summary>
    /// Muestra el panel desde código (ej: botón "Ver controles" en pausa).
    /// </summary>
    public void Show()
    {
        gameObject.SetActive(true);
        BlockPlayerInput(true);
        ShowSlide(0);
    }
}
