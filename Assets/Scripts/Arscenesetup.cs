using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// ARSceneSetup: Bootstrap automático de la escena AR.
/// Compatible con Unity 2022.3 — usa FindObjectOfType (no FindFirstObjectByType).
/// </summary>
public class ARSceneSetup : MonoBehaviour
{
    [Header("Auto-setup")]
    public bool autoSetupScene = true;

    [Header("Prefab del objeto AR (opcional)")]
    public GameObject arObjectPrefab;

    private void Awake()
    {
        if (autoSetupScene)
            SetupScene();
    }

    private void SetupScene()
    {
        Debug.Log("[Setup] Iniciando configuracion de escena AR...");

        // ── 1. Managers ──────────────────────────────────────────────────────
        EnsureManager<GPSManager>("GPSManager");
        EnsureManager<GyroscopeManager>("GyroscopeManager");
        EnsureManager<CameraFeedManager>("CameraFeedManager");

        // ── 2. Camara principal ──────────────────────────────────────────────
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            GameObject camGO = new GameObject("AR Camera");
            mainCam = camGO.AddComponent<Camera>();
            mainCam.tag = "MainCamera";
        }
        mainCam.clearFlags = CameraClearFlags.Depth;
        // backgroundColor no necesaria con Depth
        mainCam.fieldOfView = 60f;

        ARCameraController camController = mainCam.gameObject.GetComponent<ARCameraController>();
        if (camController == null)
            camController = mainCam.gameObject.AddComponent<ARCameraController>();

        // ── 3. Objeto AR ─────────────────────────────────────────────────────
        GameObject arObj;
        if (arObjectPrefab != null)
        {
            arObj = Instantiate(arObjectPrefab);
            arObj.name = "AR_Object";
        }
        else
        {
            arObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arObj.name = "AR_Cube";
            arObj.transform.localScale = Vector3.one * 0.3f;
            Renderer rend = arObj.GetComponent<Renderer>();
            if (rend != null)
            {
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(0.2f, 0.8f, 1.0f);
                rend.material = mat;
            }
            arObj.AddComponent<SimpleRotator>();
        }
        camController.arObject = arObj;

        // ── 4. Canvas UI ──────────────────────────────────────────────────────
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("AR_Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject evGO = new GameObject("EventSystem");
            evGO.AddComponent<EventSystem>();
            evGO.AddComponent<StandaloneInputModule>();
        }

        // ── 5. Panel de estado (esquina superior izquierda) ───────────────────
        GameObject statusPanel = CreatePanel(canvas.transform, "StatusPanel",
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(120f, -50f), new Vector2(230f, 95f));
        statusPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        TextMeshProUGUI gpsText  = CreateTMPText(statusPanel.transform, "GPSStatus",  "GPS...",        new Vector2(8f, -8f));
        TextMeshProUGUI gyroText = CreateTMPText(statusPanel.transform, "GyroStatus", "Giroscopio...", new Vector2(8f, -30f));
        TextMeshProUGUI dispText = CreateTMPText(statusPanel.transform, "DispText",   "",              new Vector2(8f, -52f));
        TextMeshProUGUI modeText = CreateTMPText(statusPanel.transform, "ModeText",   "Modo: GPS",     new Vector2(8f, -74f));

        // ── 6. Panel joystick (esquina inferior izquierda) ────────────────────
        GameObject joystickPanel = CreatePanel(canvas.transform, "JoystickPanel",
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(90f, 90f), new Vector2(160f, 160f));
        joystickPanel.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
        joystickPanel.SetActive(false);

        GameObject knobGO = CreatePanel(joystickPanel.transform, "Knob",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(65f, 65f));
        knobGO.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.6f);

        JoystickController joystickComp = joystickPanel.AddComponent<JoystickController>();
        joystickComp.knob = knobGO.GetComponent<RectTransform>();
        joystickComp.knobRadius = 48f;
        camController.joystickController = joystickComp;

        // ── 7. Botones (esquina inferior derecha) ─────────────────────────────
        Button toggleBtn = CreateButton(canvas.transform, "ToggleBtn",  "Joystick ON/OFF", new Vector2(-95f, 75f));
        Button recalBtn  = CreateButton(canvas.transform, "RecalBtn",   "Recalibrar",      new Vector2(-95f, 30f));

        // ── 8. UIManager ──────────────────────────────────────────────────────
        UIManager uiMgr = canvas.gameObject.GetComponent<UIManager>();
        if (uiMgr == null)
            uiMgr = canvas.gameObject.AddComponent<UIManager>();

        uiMgr.joystickPanel        = joystickPanel;
        uiMgr.cameraController     = camController;
        uiMgr.toggleJoystickButton = toggleBtn;
        uiMgr.recalibrateButton    = recalBtn;
        uiMgr.gpsStatusText        = gpsText;
        uiMgr.gyroStatusText       = gyroText;
        uiMgr.displacementText     = dispText;
        uiMgr.modeText             = modeText;

        Debug.Log("[Setup] Escena AR lista.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private GameObject CreatePanel(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = sizeDelta;
        go.AddComponent<Image>();
        return go;
    }

    private TextMeshProUGUI CreateTMPText(Transform parent, string name, string content, Vector2 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = new Vector2(-8f, 20f);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text     = content;
        tmp.fontSize = 12f;
        tmp.color    = Color.white;
        return tmp;
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 0f);
        rt.anchorMax        = new Vector2(1f, 0f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = new Vector2(175f, 38f);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.1f, 0.75f, 0.45f, 0.9f);
        Button btn = go.AddComponent<Button>();

        GameObject textGO = new GameObject("Label");
        textGO.transform.SetParent(go.transform, false);
        RectTransform trt = textGO.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.sizeDelta = Vector2.zero;
        TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 13f;
        tmp.color     = Color.black;
        tmp.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    private T EnsureManager<T>(string goName) where T : Component
    {
        T existing = FindObjectOfType<T>();
        if (existing != null) return existing;
        GameObject go = new GameObject(goName);
        DontDestroyOnLoad(go);
        return go.AddComponent<T>();
    }
}

/// <summary>Rota el objeto AR para que se vea tridimensional.</summary>
public class SimpleRotator : MonoBehaviour
{
    public float speed = 45f;
    private void Update() =>
        transform.Rotate(Vector3.up, speed * Time.deltaTime, Space.World);
}