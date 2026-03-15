using UnityEngine;

/// <summary>
/// ScriptableObject que define los datos de un hotspot AR.
/// Crear via: Assets > Create > AR > Hotspot Data
/// </summary>
[CreateAssetMenu(fileName = "NewHotspot", menuName = "AR/Hotspot Data", order = 1)]
public class HotspotData : ScriptableObject
{
    [Header("Contenido")]
    [Tooltip("Título principal del hotspot")]
    public string title = "Hotspot";

    [Tooltip("Descripción o información a mostrar")]
    [TextArea(3, 8)]
    public string description = "Información del hotspot.";

    [Tooltip("Ícono opcional para el panel")]
    public Sprite icon;

    [Header("Activación")]
    [Tooltip("Radio en unidades Unity para activación por proximidad")]
    public float triggerRadius = 3f;

    [Tooltip("Si está marcado, el hotspot también puede activarse con clic/tap")]
    public bool allowClick = true;

    [Header("Visual")]
    [Tooltip("Color del marcador del hotspot en la escena")]
    public Color markerColor = new Color(0.2f, 0.8f, 1f, 0.9f);
}