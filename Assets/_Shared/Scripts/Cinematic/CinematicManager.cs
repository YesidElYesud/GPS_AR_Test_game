using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

/// <summary>
/// CinematicManager v2 — Reproductor de video en pantalla completa.
///
/// Fixes v2:
///   - iOS Safari: requiere gesto del usuario para play(). Se añade playButton
///     opcional: cuando está asignado, el video espera un tap para iniciar en
///     lugar de reproducirse automáticamente al terminar Prepare().
///   - Android layout: tras OnPrepared, la RenderTexture se redimensiona a la
///     resolución real del video y se ajusta uvRect del RawImage para mantener
///     la relación de aspecto sin deformar ni dejar franjas negras fuera del RT.
///   - Lag: videoPlayer.skipOnDrop = true descarta frames tardíos en lugar de
///     acumularlos, eliminando el retraso progresivo en Android WebGL.
///
/// Setup en escena:
///   CinematicPanel (inactivo por defecto)
///   ├── VideoRawImage   [RawImage — fullscreen stretch anchor 0,0→1,1]
///   ├── LoadingText     [TextMeshProUGUI]
///   ├── PlayButton      [Button — centrado, tap to play, SOLO iOS] ← nuevo opcional
///   └── SkipButton      [Button — esquina inferior derecha]
/// </summary>
public class CinematicManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static CinematicManager Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Panel UI")]
    public GameObject      cinematicPanel;
    public RawImage        videoDisplay;
    public TextMeshProUGUI loadingText;
    public Button          skipButton;

    [Header("Compatibilidad iOS")]
    [Tooltip("Botón central '▶ Toca para reproducir'.\n" +
             "Aparece cuando el video está listo pero requiere gesto del usuario.\n" +
             "Obligatorio en iOS Safari. Opcional en Android (puede quedar vacío).")]
    public GameObject playButton;

    [Header("VideoPlayer")]
    public VideoPlayer videoPlayer;

    [Header("RenderTexture")]
    [Tooltip("Resolución inicial del RT. Se redimensiona automáticamente al tamaño real del video en OnPrepared.")]
    public int renderWidth  = 1280;
    public int renderHeight = 720;

    [Header("Comportamiento")]
    public bool skipIfNoContent = true;

    // ── Internos ──────────────────────────────────────────────────────────────
    private RenderTexture     _rt;
    private HotspotController _sourceHotspot;
    private bool              _advancesStage;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        _rt      = new RenderTexture(renderWidth, renderHeight, 0);
        _rt.name = "CinematicRT";

        if (videoPlayer != null)
        {
            videoPlayer.renderMode    = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = _rt;
            videoPlayer.skipOnDrop    = true;   // FIX lag: descarta frames tardíos
            videoPlayer.prepareCompleted += OnPrepared;
            videoPlayer.loopPointReached += OnFinished;
        }

        if (videoDisplay != null)
            videoDisplay.texture = _rt;

        if (skipButton != null)
            skipButton.onClick.AddListener(Skip);

        if (playButton != null)
        {
            Button btn = playButton.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(OnPlayButtonClicked);
            playButton.SetActive(false);
        }

        if (cinematicPanel != null)
            cinematicPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnPrepared;
            videoPlayer.loopPointReached -= OnFinished;
        }
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
    }

    // ── API pública ───────────────────────────────────────────────────────────
    public void Play(HotspotData data, HotspotController source)
    {
        if (data == null) { Debug.LogWarning("[CinematicManager] HotspotData es null."); return; }

        bool hasClip = data.cinematicClip != null;
        bool hasUrl  = !string.IsNullOrEmpty(data.cinematicUrl);

        if (!hasClip && !hasUrl)
        {
            Debug.LogWarning($"[CinematicManager] '{data.title}' sin video asignado.");
            if (skipIfNoContent)
            {
                if (data.cinematicAdvancesStage && StageManager.Instance != null)
                    StageManager.Instance.NextStage();
                source?.ClosePanel();
            }
            return;
        }

        _sourceHotspot = source;
        _advancesStage = data.cinematicAdvancesStage;

        ShowPanel(true);
        BlockInput(true);
        SetLoading(true);

        if (playButton != null) playButton.SetActive(false);

        // Resetear uvRect por si el video anterior lo cambió
        if (videoDisplay != null)
            videoDisplay.uvRect = new Rect(0, 0, 1, 1);

        if (videoPlayer == null)
        {
            Debug.LogError("[CinematicManager] VideoPlayer no asignado.");
            return;
        }

        videoPlayer.Stop();

#if UNITY_WEBGL && !UNITY_EDITOR
        if (hasUrl)
        {
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url    = ResolveUrl(data.cinematicUrl);
        }
        else
        {
            Debug.LogWarning($"[CinematicManager] WebGL requiere cinematicUrl. '{data.title}' no la tiene.");
            FinishCinematic();
            return;
        }
#else
        if (hasClip)
        {
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip   = data.cinematicClip;
        }
        else
        {
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url    = ResolveUrl(data.cinematicUrl);
        }
#endif
        videoPlayer.Prepare();
    }

    public void Skip()
    {
        if (videoPlayer != null) videoPlayer.Stop();
        FinishCinematic();
    }

    // ── Callbacks VideoPlayer ─────────────────────────────────────────────────
    private void OnPrepared(VideoPlayer vp)
    {
        // FIX Android layout: redimensionar RT a la resolución real del video
        ResizeRenderTexture((int)vp.width, (int)vp.height);

        // FIX Android layout: ajustar uvRect para mantener aspect ratio
        AdjustAspectRatio(vp);

        SetLoading(false);

        // FIX iOS: solo mostrar playButton en iOS (requiere gesto del usuario).
        // En Android y PC el video arranca automáticamente.
        if (playButton != null && IsIOS())
            playButton.SetActive(true);
        else
            vp.Play();
    }

    private void OnFinished(VideoPlayer vp) => FinishCinematic();

    // ── Play button (iOS) ─────────────────────────────────────────────────────
    private void OnPlayButtonClicked()
    {
        if (playButton != null) playButton.SetActive(false);
        if (videoPlayer != null) videoPlayer.Play();
    }

    // ── FIX: Redimensionar RenderTexture al tamaño real del video ─────────────
    private void ResizeRenderTexture(int w, int h)
    {
        if (w == 0 || h == 0) return;
        if (_rt != null && _rt.width == w && _rt.height == h) return;

        if (_rt != null) { _rt.Release(); Destroy(_rt); }

        _rt      = new RenderTexture(w, h, 0);
        _rt.name = "CinematicRT";

        videoPlayer.targetTexture = _rt;
        if (videoDisplay != null) videoDisplay.texture = _rt;
    }

    // ── FIX: Ajustar uvRect para relación de aspecto correcta ─────────────────
    private void AdjustAspectRatio(VideoPlayer vp)
    {
        if (videoDisplay == null || vp.width == 0 || vp.height == 0) return;

        // Forzar recalculo del layout antes de leer rect
        Canvas.ForceUpdateCanvases();

        Rect panel = videoDisplay.rectTransform.rect;
        if (panel.width <= 0 || panel.height <= 0) return;

        float videoAspect = (float)vp.width  / vp.height;
        float panelAspect = panel.width / panel.height;

        if (Mathf.Abs(videoAspect - panelAspect) < 0.01f)
        {
            videoDisplay.uvRect = new Rect(0, 0, 1, 1);
            return;
        }

        if (videoAspect > panelAspect)
        {
            // Video más ancho que el panel → letterbox (recorta arriba/abajo)
            float h = panelAspect / videoAspect;
            videoDisplay.uvRect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }
        else
        {
            // Video más alto que el panel → pillarbox (recorta izquierda/derecha)
            float w = videoAspect / panelAspect;
            videoDisplay.uvRect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }
    }

    // ── Detección de plataforma ───────────────────────────────────────────────
    /// <summary>
    /// Devuelve true cuando el navegador está corriendo en un dispositivo iOS
    /// (iPhone, iPad, iPod). En WebGL, SystemInfo.operatingSystem refleja el
    /// user-agent del navegador, por lo que funciona en runtime sin jslib.
    /// En editor siempre devuelve false para no bloquear pruebas en PC.
    /// </summary>
    private static bool IsIOS()
    {
#if UNITY_EDITOR
        return false;
#else
        string os = SystemInfo.operatingSystem;
        return os.Contains("iPhone") || os.Contains("iPad") || os.Contains("iPod");
#endif
    }

    /// <summary>
    /// Devuelve true en smartphones y tablets (Android o iOS).
    /// Útil para adaptar otros comportamientos de UI si es necesario.
    /// </summary>
    private static bool IsMobile() => SystemInfo.deviceType == DeviceType.Handheld;

    // ── Resolución de URL ─────────────────────────────────────────────────────
    private string ResolveUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("file://")) return url;

        string relative = url.StartsWith("StreamingAssets/")
            ? url.Substring("StreamingAssets/".Length)
            : url;

        string basePath = Application.streamingAssetsPath;

#if UNITY_EDITOR || UNITY_STANDALONE
        return "file://" + basePath + "/" + relative;
#else
        return basePath + "/" + relative;
#endif
    }

    // ── Cierre y limpieza ─────────────────────────────────────────────────────
    private void FinishCinematic()
    {
        if (playButton != null) playButton.SetActive(false);

        if (_advancesStage && StageManager.Instance != null)
            StageManager.Instance.NextStage();

        BlockInput(false);
        ShowPanel(false);

        if (_sourceHotspot != null) { _sourceHotspot.ClosePanel(); _sourceHotspot = null; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void ShowPanel(bool visible)
    {
        if (cinematicPanel != null) cinematicPanel.SetActive(visible);
    }

    private void SetLoading(bool loading)
    {
        if (loadingText  != null) loadingText.gameObject.SetActive(loading);
        if (videoDisplay != null) videoDisplay.gameObject.SetActive(!loading);
    }

    private void BlockInput(bool block)
    {
        if (StageManager.Instance != null)
            StageManager.Instance.SetPlayerInputBlocked(block);
    }
}
