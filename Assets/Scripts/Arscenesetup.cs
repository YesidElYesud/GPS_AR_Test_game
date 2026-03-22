using UnityEngine;

/// <summary>
/// ARSceneSetup v3: Sin inicialización automática.
/// Todos los managers y objetos AR deben estar configurados manualmente en el editor.
/// </summary>
public class ARSceneSetup : MonoBehaviour
{
    private void Awake()
    {
        Debug.Log("[ARSceneSetup] Escena lista (sin inicialización automática).");
    }
}