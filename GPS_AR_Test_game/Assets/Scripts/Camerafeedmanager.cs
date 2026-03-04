using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CameraFeedManager: Muestra el feed de la cámara del dispositivo como fondo.
///
/// En WebGL: solicita permisos de cámara vía JS y muestra el stream en un
/// RawImage de fondo. En editor usa WebCamTexture de Unity directamente.
///
/// La cámara del dispositivo se muestra SIEMPRE como fondo plano (no 3D),
/// la cámara de Unity renderiza los objetos 3D encima con fondo transparente.
/// </summary>
public class CameraFeedManager : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────────
    public static CameraFeedManager Instance { get; private set; }

    // ── Imports JS ──────────────────────────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void CamFeed_Start(string videoElementId);
    [DllImport("__Internal")] private static extern void CamFeed_Stop();
    [DllImport("__Internal")] private static extern bool CamFeed_IsReady();
#else
    private static void CamFeed_Start(string id) { }
    private static void CamFeed_Stop() { }
    private static bool CamFeed_IsReady() => false;
#endif

    // ── Inspector ────────────────────────────────────────────────────────────
    [Header("UI")]
    [Tooltip("RawImage que cubre toda la pantalla para el feed de cámara")]
    public RawImage backgroundImage;

    [Header("Editor fallback")]
    [Tooltip("Índice de cámara web para probar en editor")]
    public int editorCameraIndex = 0;

    // ── Internos ─────────────────────────────────────────────────────────────
    private WebCamTexture _webCamTexture;
    private bool _webglCamReady = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // En WebGL: el JS manejará el <video> element y renderizará en canvas
        // El fondo del canvas del video ya es gestionado por el template HTML
        Debug.Log("[CamFeed] Modo WebGL: cámara gestionada por JS.");
        CamFeed_Start("ar-video-bg");
        // Esconder la RawImage de Unity porque el video lo pinta el HTML
        if (backgroundImage != null)
            backgroundImage.gameObject.SetActive(false);
#else
        StartEditorCamera();
#endif
    }

    private void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        // En editor, actualizar textura de la webcam
        if (_webCamTexture != null && _webCamTexture.isPlaying && backgroundImage != null)
        {
            if (backgroundImage.texture != _webCamTexture)
                backgroundImage.texture = _webCamTexture;

            // Corregir rotación según la cámara
            AdjustEditorCameraDisplay();
        }
#endif
    }

    private void OnDestroy()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        CamFeed_Stop();
#else
        if (_webCamTexture != null && _webCamTexture.isPlaying)
            _webCamTexture.Stop();
#endif
    }

    // ── Editor Camera ─────────────────────────────────────────────────────────
    private void StartEditorCamera()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogWarning("[CamFeed] No se encontraron cámaras.");
            return;
        }

        int idx = Mathf.Clamp(editorCameraIndex, 0, devices.Length - 1);
        _webCamTexture = new WebCamTexture(devices[idx].name, 1280, 720, 30);
        _webCamTexture.Play();
        Debug.Log($"[CamFeed] Editor: usando cámara '{devices[idx].name}'");
    }

    private void AdjustEditorCameraDisplay()
    {
        if (_webCamTexture == null || backgroundImage == null) return;

        // Corregir rotación vertical si la cámara lo requiere
        int angle = _webCamTexture.videoRotationAngle;
        backgroundImage.rectTransform.localEulerAngles = new Vector3(0, 0, -angle);

        // Espejo horizontal en cámara frontal
        bool mirror = _webCamTexture.videoVerticallyMirrored;
        backgroundImage.rectTransform.localScale = new Vector3(mirror ? -1 : 1, 1, 1);
    }

    // ── Callback JS ──────────────────────────────────────────────────────────
    public void OnCameraReady(string msg)
    {
        _webglCamReady = true;
        Debug.Log($"[CamFeed] Cámara WebGL lista: {msg}");
    }

    public void OnCameraError(string errorMsg)
    {
        Debug.LogError($"[CamFeed] Error de cámara: {errorMsg}");
    }
}