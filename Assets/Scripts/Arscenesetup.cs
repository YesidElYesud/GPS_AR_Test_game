using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class ARSceneSetup : MonoBehaviour
{
    [Header("Auto-setup")]
    public bool autoSetupScene = true;

    [Header("Prefab del objeto AR (opcional)")]
    public GameObject arObjectPrefab;

    private void Awake()
    {
        if (autoSetupScene) SetupScene();
    }

    private void SetupScene()
    {
        Debug.Log("[Setup] Iniciando...");

        EnsureManager<GPSManager>("GPSManager");
        EnsureManager<GyroscopeManager>("GyroscopeManager");
        EnsureManager<CameraFeedManager>("CameraFeedManager");

        // Camara principal
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            GameObject camGO = new GameObject("AR Camera");
            mainCam = camGO.AddComponent<Camera>();
            mainCam.tag = "MainCamera";
        }
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        mainCam.fieldOfView = 60f;

        ARCameraController camCtrl = mainCam.gameObject.GetComponent<ARCameraController>();
        if (camCtrl == null) camCtrl = mainCam.gameObject.AddComponent<ARCameraController>();

        // Objeto AR
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
            arObj.transform.localScale = Vector3.one * 0.4f;
            Renderer rend = arObj.GetComponent<Renderer>();
            if (rend != null)
            {
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(0.2f, 0.8f, 1.0f);
                rend.material = mat;
            }
            arObj.AddComponent<SimpleRotator>();
        }
        camCtrl.arObject = arObj;

        // Canvas UI principal
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject cGO = new GameObject("AR_Canvas");
            canvas = cGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler sc = cGO.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(390, 844); // iPhone size
            sc.matchWidthOrHeight = 0.5f;
            cGO.AddComponent<GraphicRaycaster>();
        }

        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject ev = new GameObject("EventSystem");
            ev.AddComponent<EventSystem>();
            ev.AddComponent<StandaloneInputModule>();
        }

        // ── Panel de estado (esquina superior izquierda) ──────────────────────
        GameObject statusPanel = CreatePanel(canvas.transform, "StatusPanel",
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(110f, -80f), new Vector2(200f, 130f));
        statusPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        TextMeshProUGUI gpsText  = CreateText(statusPanel.transform, "GPSStatus",  "GPS...",       new Vector2(10f, -12f), 18f);
        TextMeshProUGUI gyroText = CreateText(statusPanel.transform, "GyroStatus", "Giroscopio...",new Vector2(10f, -42f), 18f);
        TextMeshProUGUI dispText = CreateText(statusPanel.transform, "DispText",   "",             new Vector2(10f, -72f), 16f);
        TextMeshProUGUI modeText = CreateText(statusPanel.transform, "ModeText",   "Modo: GPS",    new Vector2(10f, -100f),16f);

        // ── Panel joystick ────────────────────────────────────────────────────
        GameObject joystickPanel = CreatePanel(canvas.transform, "JoystickPanel",
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(100f, 100f), new Vector2(180f, 180f));
        Image joystickImg = joystickPanel.GetComponent<Image>();
        joystickImg.color  = new Color(1f, 1f, 1f, 0.25f);
        joystickImg.sprite = CreateCircleSprite(64);
        joystickImg.type   = Image.Type.Simple;
        joystickPanel.SetActive(false);

        GameObject knobGO = CreatePanel(joystickPanel.transform, "Knob",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(72f, 72f));
        Image knobImg  = knobGO.GetComponent<Image>();
        knobImg.color  = new Color(1f, 1f, 1f, 0.75f);
        knobImg.sprite = CreateCircleSprite(32);
        knobImg.type   = Image.Type.Simple;

        JoystickController joystick = joystickPanel.AddComponent<JoystickController>();
        joystick.knob = knobGO.GetComponent<RectTransform>();
        joystick.knobRadius = 52f;
        camCtrl.joystickController = joystick;

        // ── Botones (esquina inferior derecha) ────────────────────────────────
        Button toggleBtn = CreateButton(canvas.transform, "ToggleBtn",  "Joystick ON/OFF", new Vector2(-105f, 90f));
        Button recalBtn  = CreateButton(canvas.transform, "RecalBtn",   "Recalibrar GPS",  new Vector2(-105f, 40f));

        // ── UIManager ─────────────────────────────────────────────────────────
        UIManager uiMgr = canvas.gameObject.GetComponent<UIManager>();
        if (uiMgr == null) uiMgr = canvas.gameObject.AddComponent<UIManager>();

        uiMgr.joystickPanel        = joystickPanel;
        uiMgr.cameraController     = camCtrl;
        uiMgr.toggleJoystickButton = toggleBtn;
        uiMgr.recalibrateButton    = recalBtn;
        uiMgr.gpsStatusText        = gpsText;
        uiMgr.gyroStatusText       = gyroText;
        uiMgr.displacementText     = dispText;
        uiMgr.modeText             = modeText;

        Debug.Log("[Setup] Listo.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private GameObject CreatePanel(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        go.AddComponent<Image>();
        return go;
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, string content, Vector2 pos, float size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(-10f, 26f);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = content; tmp.fontSize = size; tmp.color = Color.white;
        return tmp;
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 0f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(195f, 44f);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.1f, 0.75f, 0.45f, 0.92f);
        Button btn = go.AddComponent<Button>();

        GameObject lGO = new GameObject("Label");
        lGO.transform.SetParent(go.transform, false);
        RectTransform lrt = lGO.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.sizeDelta = Vector2.zero;
        TextMeshProUGUI tmp = lGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label; tmp.fontSize = 16f;
        tmp.color = Color.black; tmp.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    // Crea una sprite circular por codigo (sin necesitar assets externos)
    private Sprite CreateCircleSprite(int radius = 64)
    {
        int size = radius * 2;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(radius, radius);
        float r = radius - 1f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dist = Vector2.Distance(new Vector2(x, y), center);
            // Antialiasing suave en el borde
            float alpha = Mathf.Clamp01(1f - (dist - r + 1f));
            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
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

public class SimpleRotator : MonoBehaviour
{
    public float speed = 40f;
    private void Update() => transform.Rotate(Vector3.up, speed * Time.deltaTime, Space.World);
}