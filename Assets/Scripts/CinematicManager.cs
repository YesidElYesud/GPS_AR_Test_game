using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

/// <summary>
/// CinematicManager — Reproductor de video en pantalla completa.
/// Sistema 6 del proyecto SATCS.
///
/// Responsabilidades:
///   - Recibir VideoClip (editor) o URL (WebGL) desde HotspotController.
///   - Renderizar el video en un RawImage a pantalla completa.
///   - Bloquear input del jugador durante la reproducción.
///   - Permitir saltar con el botón Skip.
///   - Avanzar etapa al terminar/saltar si cinematicAdvancesStage = true.
///
/// Setup en escena:
///   1. Crear GameObject vacío "CinematicManager" en la raíz (junto a StageManager, etc.).
///   2. Adjuntar este script y un componente VideoPlayer al mismo GameObject.
///   3. En AR_Canvas crear "CinematicPanel" (inactivo por defecto):
///        CinematicPanel
///        ├── VideoRawImage   (RawImage — fullscreen, stretch)
///        ├── LoadingText     (TextMeshProUGUI — "Cargando video...")
///        └── SkipButton      (Button — esquina inferior derecha)
///   4. Asignar referencias en el Inspector de CinematicManager.
/// </summary>
public class CinematicManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static CinematicManager Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Panel UI")]
    [Tooltip("GameObject raíz del panel de cinemática en AR_Canvas. Inactivo por defecto.")]
    public GameObject cinematicPanel;

    [Tooltip("RawImage de pantalla completa donde se renderiza el video.")]
    public RawImage videoDisplay;

    [Tooltip("Texto visible mientras el video está cargando ('Cargando video...').")]
    public TextMeshProUGUI loadingText;

    [Tooltip("Botón para saltar la cinemática.")]
    public Button skipButton;

    [Header("VideoPlayer")]
    [Tooltip("Componente VideoPlayer. Puede estar en este mismo GameObject.")]
    public VideoPlayer videoPlayer;

    [Header("RenderTexture")]
    [Tooltip("Ancho de la RenderTexture interna. 1280 es suficiente para WebGL.")]
    public int renderWidth  = 1280;
    [Tooltip("Alto de la RenderTexture interna.")]
    public int renderHeight = 720;

    [Header("Comportamiento")]
    [Tooltip("Si no hay clip ni URL, cierra el panel y avanza etapa sin bloquear el juego.")]
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
        // RenderTexture creada en tiempo de ejecución (no necesita asset)
        _rt      = new RenderTexture(renderWidth, renderHeight, 0);
        _rt.name = "CinematicRT";

        if (videoPlayer != null)
        {
            videoPlayer.renderMode    = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = _rt;
            videoPlayer.prepareCompleted += OnPrepared;
            videoPlayer.loopPointReached += OnFinished;
        }

        if (videoDisplay != null)
            videoDisplay.texture = _rt;

        if (skipButton != null)
            skipButton.onClick.AddListener(Skip);

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

        if (_rt != null)
        {
            _rt.Release();
            Destroy(_rt);
        }
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Inicia la cinemática del hotspot indicado.
    /// Llamado desde HotspotController.DispatchAction().
    /// </summary>
    public void Play(HotspotData data, HotspotController source)
    {
        if (data == null)
        {
            Debug.LogWarning("[CinematicManager] HotspotData es null.");
            return;
        }

        bool hasClip = data.cinematicClip != null;
        bool hasUrl  = !string.IsNullOrEmpty(data.cinematicUrl);

        if (!hasClip && !hasUrl)
        {
            Debug.LogWarning($"[CinematicManager] '{data.title}' no tiene VideoClip ni URL de video asignados.");
            if (skipIfNoContent)
            {
                if (data.cinematicAdvancesStage && StageManager.Instance != null)
                    StageManager.Instance.NextStage();
                source?.ClosePanel();
                return;
            }
        }

        _sourceHotspot = source;
        _advancesStage = data.cinematicAdvancesStage;

        ShowPanel(true);
        BlockInput(true);
        SetLoading(true);

        if (videoPlayer == null)
        {
            Debug.LogError("[CinematicManager] VideoPlayer no asignado en el Inspector.");
            return;
        }

        videoPlayer.Stop();

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL solo soporta reproducción por URL (no VideoClip).
        if (hasUrl)
        {
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url    = ResolveUrl(data.cinematicUrl);
        }
        else
        {
            Debug.LogWarning($"[CinematicManager] WebGL: '{data.title}' no tiene cinematicUrl. Se omite el video.");
            FinishCinematic();
            return;
        }
#else
        // Editor / standalone: VideoClip tiene prioridad; URL como fallback.
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

    /// <summary>Salta la cinemática actual inmediatamente.</summary>
    public void Skip()
    {
        if (videoPlayer != null) videoPlayer.Stop();
        FinishCinematic();
    }

    // ── Callbacks VideoPlayer ─────────────────────────────────────────────────
    private void OnPrepared(VideoPlayer vp)
    {
        SetLoading(false);
        vp.Play();
    }

    private void OnFinished(VideoPlayer vp)
    {
        FinishCinematic();
    }

    // ── Resolución de URL ─────────────────────────────────────────────────────
    /// <summary>
    /// Convierte paths relativos de StreamingAssets en URLs absolutas.
    /// - WebGL/builds: http://host/StreamingAssets/...
    /// - Editor/standalone: file:///ruta/absoluta/...
    /// URLs que ya empiezan con http/https/file se devuelven sin cambios.
    /// </summary>
    private string ResolveUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("file://")) return url;

        // Quitar prefijo "StreamingAssets/" si el usuario lo incluyó
        string relative = url.StartsWith("StreamingAssets/")
            ? url.Substring("StreamingAssets/".Length)
            : url;

        string basePath = Application.streamingAssetsPath;

#if UNITY_EDITOR || UNITY_STANDALONE
        // En editor/standalone el path es del sistema de archivos → necesita file://
        return "file://" + basePath + "/" + relative;
#else
        // En WebGL streamingAssetsPath ya es una URL http completa
        return basePath + "/" + relative;
#endif
    }

    // ── Cierre y limpieza ─────────────────────────────────────────────────────
    private void FinishCinematic()
    {
        if (_advancesStage && StageManager.Instance != null)
            StageManager.Instance.NextStage();

        BlockInput(false);
        ShowPanel(false);

        if (_sourceHotspot != null)
        {
            _sourceHotspot.ClosePanel();
            _sourceHotspot = null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void ShowPanel(bool visible)
    {
        if (cinematicPanel != null)
            cinematicPanel.SetActive(visible);
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
