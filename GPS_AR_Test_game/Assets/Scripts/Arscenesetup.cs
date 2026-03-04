using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ARSceneSetup: Script de inicialización de la escena.
/// Crea y conecta todos los GameObjects en el Start si no existen en la escena.
/// Puedes usarlo como "bootstrap" o configurar la escena manualmente siguiendo
/// la GUIA DE ESCENA al final de este archivo.
///
/// NOTA: Este script se puede eliminar si prefieres configurar la escena
/// manualmente desde el editor de Unity (recomendado para producción).
/// </summary>
public class ARSceneSetup : MonoBehaviour
{
    [Header("Auto-setup")]
    [Tooltip("Si es true, crea los GameObjects necesarios al iniciar")]
    public bool autoSetupScene = true;

    [Header("Prefabs opcionales (si no se auto-crean)")]
    public GameObject arObjectPrefab;

    private void Awake()
    {
        if (autoSetupScene)
            SetupScene();
    }

    private void SetupScene()
    {
        Debug.Log("[Setup] Iniciando configuración de escena AR...");

        // ── 1. Managers (GameObject vacíos con scripts) ──────────────────────
        EnsureManager<GPSManager>("GPSManager");
        EnsureManager<GyroscopeManager>("GyroscopeManager");
        EnsureManager<CameraFeedManager>("CameraFeedManager");

        // ── 2. Cámara principal ──────────────────────────────────────────────
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            GameObject camGO = new GameObject("AR Camera");
            mainCam = camGO.AddComponent<Camera>();
        }
        // Fondo transparente para ver el video de la cámara del dispositivo
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = new Color(0, 0, 0, 0);
        mainCam.fieldOfView = 60f;

        // Controlador de cámara
        ARCameraController camController = mainCam.gameObject.GetComponent<ARCameraController>();
        if (camController == null)
            camController = mainCam.gameObject.AddComponent<ARCameraController>();

        // ── 3. Objeto AR (Cubo) ──────────────────────────────────────────────
        GameObject arObj;
        if (arObjectPrefab != null)
        {
            arObj = Instantiate(arObjectPrefab);
        }
        else
        {
            arObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arObj.name = "AR_Cube";
            arObj.transform.localScale = Vector3.one * 0.3f; // 30cm de lado

            // Material simple
            Renderer rend = arObj.GetComponent<Renderer>();
            if (rend != null)
            {
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (mat.shader.name == "Hidden/InternalErrorShader")
                    mat = new Material(Shader.Find("Standard")); // Fallback
                mat.color = new Color(0.2f, 0.8f, 1.0f, 1f); // Azul cian
                rend.material = mat;
            }

            // Rotación simple para que se vea que es 3D
            arObj.AddComponent<SimpleRotator>();
        }

        camController.arObject = arObj;

        // ── 4. Canvas UI ──────────────────────────────────────────────────────
        SetupUI(camController);

        Debug.Log("[Setup] Escena AR configurada.");
    }

    private void SetupUI(ARCameraController camController)
    {
        // Canvas raíz
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("AR_Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();

            // EventSystem
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject evGO = new GameObject("EventSystem");
                evGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
                evGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
        }

        // Panel de estado (esquina superior izquierda)
        GameObject statusPanel = CreatePanel(canvas.transform, "StatusPanel",
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -20),
            new Vector2(220, 80));
        statusPanel.GetComponent<Image>().color = new Color(0, 0, 0, 0.5f);

        // Textos de estado
        CreateText(statusPanel.transform, "GPSStatus", "GPS...", 12, new Vector2(10, -10));
        CreateText(statusPanel.transform, "GyroStatus", "Giroscopio...", 12, new Vector2(10, -28));
        CreateText(statusPanel.transform, "Displacement", "", 11, new Vector2(10, -46));

        // Texto de modo (esquina superior derecha)
        GameObject modePanel = CreatePanel(canvas.transform, "ModePanel",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -20),
            new Vector2(150, 30));
        modePanel.GetComponent<Image>().color = new Color(0, 0, 0, 0.5f);
        CreateText(modePanel.transform, "ModeText", "Modo: GPS", 12, new Vector2(10, -8));

        // Panel joystick (parte inferior izquierda)
        GameObject joystickPanel = CreatePanel(canvas.transform, "JoystickPanel",
            new Vector2(0, 0), new Vector2(0, 0), new Vector2(80, 80),
            new Vector2(150, 150));
        joystickPanel.GetComponent<Image>().color = new Color(1, 1, 1, 0.15f);
        joystickPanel.SetActive(false);

        // Knob del joystick
        GameObject knob = CreatePanel(joystickPanel.transform, "Knob",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(60, 60));
        knob.GetComponent<Image>().color = new Color(1, 1, 1, 0.6f);

        // Script joystick
        JoystickController joystickComp = joystickPanel.AddComponent<JoystickController>();
        joystickComp.knob = knob.GetComponent<RectTransform>();
        joystickComp.knobRadius = 45f;

        camController.joystickController = joystickComp;

        // Botones (parte inferior derecha)
        GameObject btnPanel = CreatePanel(canvas.transform, "ButtonPanel",
            new Vector2(1, 0), new Vector2(1, 0), new Vector2(-10, 10),
            new Vector2(170, 90));
        btnPanel.GetComponent<Image>().color = new Color(0, 0, 0, 0f);

        Button toggleBtn = CreateButton(btnPanel.transform, "ToggleJoystickBtn",
            "Usar Joystick", new Vector2(0, 50));
        Button recalBtn = CreateButton(btnPanel.transform, "RecalibrateBtn",
            "Recalibrar", new Vector2(0, 10));

        // UIManager
        UIManager uiMgr = canvas.gameObject.GetComponent<UIManager>();
        if (uiMgr == null) uiMgr = canvas.gameObject.AddComponent<UIManager>();

        uiMgr.joystickPanel     = joystickPanel;
        uiMgr.cameraController  = camController;
        uiMgr.toggleJoystickButton = toggleBtn;
        uiMgr.recalibrateButton    = recalBtn;

        // Asignar textos al UIManager
        var allTexts = statusPanel.GetComponentsInChildren<TextMeshProUGUI>();
        // Necesitarían asignarse por nombre; en editor esto es más limpio
        // Para el auto-setup básico dejamos los textos funcionales sin binding
    }

    // ── Helpers de creación de UI ─────────────────────────────────────────────
    private GameObject CreatePanel(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        go.AddComponent<Image>();
        return go;
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, string text,
        int fontSize, Vector2 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(0, 20);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        return tmp;
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(-10, 35);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0f, 0.8f, 0.5f, 0.85f);
        Button btn = go.AddComponent<Button>();

        GameObject textGO = new GameObject("Label");
        textGO.transform.SetParent(go.transform, false);
        RectTransform trt = textGO.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.sizeDelta = Vector2.zero;
        TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 13;
        tmp.color = Color.black;
        tmp.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    private T EnsureManager<T>(string goName) where T : Component
    {
        T existing = FindFirstObjectByType<T>();
        if (existing != null) return existing;
        GameObject go = new GameObject(goName);
        DontDestroyOnLoad(go);
        return go.AddComponent<T>();
    }
}

/// <summary>
/// Rotador simple para el cubo AR - gira para indicar que es 3D.
/// </summary>
public class SimpleRotator : MonoBehaviour
{
    public float speed = 45f;
    private void Update()
    {
        transform.Rotate(Vector3.up, speed * Time.deltaTime, Space.World);
    }
}