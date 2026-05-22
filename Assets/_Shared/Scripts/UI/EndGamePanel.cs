using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Panel final de la experiencia. Se muestra al cerrar el hotspot "HP Punto Encuentro".
/// Ocupa toda la pantalla, muestra la imagen de felicitación y ofrece un botón de reinicio.
///
/// Setup en Unity:
///   1. Crear GO "EndGamePanel" como hijo directo de AR_Canvas (DESPUÉS de CriticalModePanel
///      en la jerarquía para que se dibuje encima de todo).
///   2. Añadir Image (full-stretch, alpha 0 para fondo si se quiere transparente).
///   3. Añadir hijo "PanelImage" (Image — aquí va la imagen de felicitación, full-stretch o centrada).
///   4. Añadir hijo "RestartButton" (Button + Text/TMP).
///   5. Asignar referencias en Inspector: panelImage, restartButton.
///   6. Asignar RestartButton.onClick → EndGamePanel.Restart() (vía el componente en escena).
///   7. Dejar el GO ACTIVO en escena — Awake lo desactiva automáticamente.
/// </summary>
public class EndGamePanel : MonoBehaviour
{
    public static EndGamePanel Instance { get; private set; }

    [Header("UI")]
    [Tooltip("Image donde se muestra la imagen de felicitación / slide final.")]
    [SerializeField] private Image panelImage;

    [Tooltip("Botón que reinicia la experiencia sin recargar el navegador.")]
    [SerializeField] private Button restartButton;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── API pública ───────────────────────────────────────────────────────────
    public void Show()
    {
        gameObject.SetActive(true);
        StageManager.Instance?.SetPlayerInputBlocked(true);
    }

    /// <summary>
    /// Reinicia la experiencia.
    /// • WebGL: recarga la página desde caché del navegador (no re-descarga nada).
    /// • Editor/standalone: destruye los singletons DontDestroyOnLoad y recarga la escena,
    ///   evitando referencias obsoletas al cielo, al RiskLevelIndicator, etc.
    /// </summary>
    public void Restart()
    {
        StageManager.Instance?.SetPlayerInputBlocked(false);

#if UNITY_WEBGL && !UNITY_EDITOR
        Application.ExternalEval("location.reload()");
#else
        DestroyPersistentSingletons();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
#endif
    }

    private static void DestroyPersistentSingletons()
    {
        if (StageManager.Instance                 != null) Destroy(StageManager.Instance.gameObject);
        if (VisualEffectsStageController.Instance != null) Destroy(VisualEffectsStageController.Instance.gameObject);
        if (AudioStageManager.Instance            != null) Destroy(AudioStageManager.Instance.gameObject);
        if (AerialViewController.Instance         != null) Destroy(AerialViewController.Instance.gameObject);
        if (CinematicManager.Instance             != null) Destroy(CinematicManager.Instance.gameObject);
        if (SceneOverviewController.Instance      != null) Destroy(SceneOverviewController.Instance.gameObject);
        if (GPSManager.Instance                   != null) Destroy(GPSManager.Instance.gameObject);
        if (GyroscopeManager.Instance             != null) Destroy(GyroscopeManager.Instance.gameObject);
        if (CameraFeedManager.Instance            != null) Destroy(CameraFeedManager.Instance.gameObject);
    }

    // ── Imagen configurable en runtime (opcional) ─────────────────────────────
    public void SetImage(Sprite sprite)
    {
        if (panelImage != null) panelImage.sprite = sprite;
    }
}
