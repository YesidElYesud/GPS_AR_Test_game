using UnityEngine;

/// <summary>
/// CameraFeedManager: desactivado. El proyecto ahora es VR puro,
/// no necesita feed de cámara.
/// Se mantiene para que los scripts que lo referencian compilen.
/// </summary>
public class CameraFeedManager : MonoBehaviour
{
    public static CameraFeedManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Stubs para que ARSceneSetup compile sin errores
    public void OnCameraReady(string msg) { }
    public void OnCameraError(string msg) { }
    public void OnVideoFrame(string data) { }
}