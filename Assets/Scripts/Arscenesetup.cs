using UnityEngine;

/// <summary>
/// ARSceneSetup v2: Solo garantiza que los managers singleton existan.
/// La UI del Canvas se configura DESDE EL EDITOR, no desde código.
///
/// Pasos mínimos en la escena:
///   1. Tener un GameObject con este script (puede ser el mismo que tenga ARCameraController).
///   2. La cámara principal debe tener ARCameraController adjunto.
///   3. El Canvas con UIManager, HotspotUIPanel, etc. se arma en el editor.
/// </summary>
public class ARSceneSetup : MonoBehaviour
{
    [Header("Configuración")]
    [Tooltip("Si está activo, crea los managers singleton si no existen en la escena")]
    public bool autoCreateManagers = true;

    [Header("Prefab del objeto AR (opcional)")]
    [Tooltip("Si se asigna, se instancia como objeto AR. Si no, se usa el arObject de ARCameraController.")]
    public GameObject arObjectPrefab;

    [Header("Referencias (asignar desde el editor)")]
    [Tooltip("ARCameraController de la cámara principal")]
    public ARCameraController cameraController;

    private void Awake()
    {
        if (autoCreateManagers)
        {
            EnsureManager<GPSManager>("GPSManager");
            EnsureManager<GyroscopeManager>("GyroscopeManager");
            EnsureManager<CameraFeedManager>("CameraFeedManager");
        }

        // Si el cameraController no está asignado, buscar en la escena
        if (cameraController == null)
            cameraController = FindObjectOfType<ARCameraController>();

        // Instanciar objeto AR si se proporcionó un prefab y la cámara no tiene uno
        if (arObjectPrefab != null && cameraController != null && cameraController.arObject == null)
        {
            GameObject arObj = Instantiate(arObjectPrefab);
            arObj.name = "AR_Object";
            cameraController.arObject = arObj;
            Debug.Log("[ARSceneSetup] Objeto AR instanciado desde prefab.");
        }

        Debug.Log("[ARSceneSetup] Inicialización completada.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private T EnsureManager<T>(string goName) where T : Component
    {
        T existing = FindObjectOfType<T>();
        if (existing != null) return existing;

        GameObject go = new GameObject(goName);
        DontDestroyOnLoad(go);
        T component = go.AddComponent<T>();
        Debug.Log($"[ARSceneSetup] Manager creado: {goName}");
        return component;
    }
}