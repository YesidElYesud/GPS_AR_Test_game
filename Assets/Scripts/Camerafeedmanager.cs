using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CameraFeedManager: Muestra el feed de camara como fondo dentro de Unity.
/// Usa una camara secundaria de depth -1 que renderiza solo la RawImage del video.
/// La camara principal (depth 0) renderiza el cubo AR encima.
/// </summary>
public class CameraFeedManager : MonoBehaviour
{
    public static CameraFeedManager Instance { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void CamFeed_Start(string id);
    [DllImport("__Internal")] private static extern void CamFeed_Stop();
    [DllImport("__Internal")] private static extern int  CamFeed_IsReady();
    [DllImport("__Internal")] private static extern void GrabVideoFrame();
#else
    private static void CamFeed_Start(string id) { }
    private static void CamFeed_Stop()           { }
    private static int  CamFeed_IsReady()        => 0;
    private static void GrabVideoFrame()         { }
#endif

    private RawImage    _bgImage;
    private Texture2D   _videoTex;
    private bool        _cameraReady = false;
    private WebCamTexture _editorCam;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        CreateBackgroundSetup();

#if UNITY_WEBGL && !UNITY_EDITOR
        CamFeed_Start("ar-video-bg");
#else
        StartEditorCamera();
#endif
    }

    private void OnDestroy() { CamFeed_Stop(); }

    // ── Setup: camara de fondo + canvas + RawImage ────────────────────────────
    private void CreateBackgroundSetup()
    {
        // ── Camara de fondo (depth -1) ────────────────────────────────────────
        // Solo renderiza el layer "UI" donde está la RawImage del video.
        // La camara principal (depth 0) renderiza todo LO DEMAS encima.
        GameObject bgCamGO = new GameObject("BackgroundCamera");
        DontDestroyOnLoad(bgCamGO);
        Camera bgCam = bgCamGO.AddComponent<Camera>();
        bgCam.depth      = -1;                         // se renderiza primero
        bgCam.clearFlags = CameraClearFlags.SolidColor;
        bgCam.backgroundColor = Color.black;
        bgCam.cullingMask = LayerMask.GetMask("UI");   // solo UI
        bgCam.orthographic = true;

        // ── Canvas en Screen Space - Camera ───────────────────────────────────
        GameObject canvasGO = new GameObject("CameraFeedCanvas");
        DontDestroyOnLoad(canvasGO);
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera  = bgCam;
        canvas.planeDistance = 1f;
        canvas.sortingOrder  = 0;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ── RawImage que llena toda la pantalla ───────────────────────────────
        GameObject imgGO = new GameObject("VideoFrame");
        imgGO.transform.SetParent(canvasGO.transform, false);
        // Asignar al layer UI
        imgGO.layer = LayerMask.NameToLayer("UI");

        _bgImage = imgGO.AddComponent<RawImage>();
        RectTransform rt = _bgImage.GetComponent<RectTransform>();
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.sizeDelta        = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        _bgImage.color = Color.black;

        // ── Camara principal: NO limpiar color, solo depth ────────────────────
        // Asi la camara de fondo ya pinto el video, y la principal dibuja
        // el cubo AR encima sin borrar lo que hay debajo.
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.depth      = 0;
            mainCam.clearFlags = CameraClearFlags.Depth; // <-- solo limpiar depth
            mainCam.cullingMask = ~LayerMask.GetMask("UI"); // todo excepto UI
        }
    }

    // ── Callbacks desde JS ────────────────────────────────────────────────────
    public void OnCameraReady(string msg)
    {
        _cameraReady = true;
        Debug.Log("[Cam] Lista.");
        InvokeRepeating(nameof(RequestFrame), 0.1f, 1f / 25f);
    }

    public void OnCameraError(string msg)
    {
        Debug.LogWarning("[Cam] Error: " + msg);
    }

    public void OnVideoFrame(string base64jpeg)
    {
        if (string.IsNullOrEmpty(base64jpeg) || _bgImage == null) return;
        try
        {
            byte[] bytes = System.Convert.FromBase64String(base64jpeg);
            if (_videoTex == null)
                _videoTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (_videoTex.LoadImage(bytes))
            {
                _bgImage.texture = _videoTex;
                _bgImage.color   = Color.white;

                // Ajustar UV para mantener aspect ratio sin deformar
                float vidAR    = (float)_videoTex.width  / _videoTex.height;
                float scrAR    = (float)Screen.width     / Screen.height;
                if (vidAR > scrAR)
                {
                    float s = scrAR / vidAR;
                    _bgImage.uvRect = new Rect((1f-s)/2f, 0f, s, 1f);
                }
                else
                {
                    float s = vidAR / scrAR;
                    _bgImage.uvRect = new Rect(0f, (1f-s)/2f, 1f, s);
                }
            }
        }
        catch { /* ignorar frame corrupto */ }
    }

    private void RequestFrame()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (_cameraReady) GrabVideoFrame();
#endif
    }

    // ── Editor ────────────────────────────────────────────────────────────────
    private void StartEditorCamera()
    {
        if (WebCamTexture.devices.Length == 0) return;
        _editorCam = new WebCamTexture();
        _editorCam.Play();
        if (_bgImage != null)
        {
            _bgImage.texture = _editorCam;
            _bgImage.color   = Color.white;
        }
    }
}