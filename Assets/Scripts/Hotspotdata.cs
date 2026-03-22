using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// HotspotData v2 — ScriptableObject con los datos de un hotspot AR.
///
/// Novedades respecto a v1:
///   - actionType: determina qué ocurre al activar el hotspot (panel, cinemática, NPC, llamada).
///   - requiredStage: etapa en que este hotspot es visible (-1 = siempre visible).
///   - isBlinking: activa el efecto de pulso visual en el objeto 3D.
///   - cinematicClip: video a reproducir cuando actionType = Cinematic.
///   - dialogueData: datos del NPC/SIATA cuando actionType = NpcConversation o SiataCall.
///
/// Crear via: Assets > Create > AR > Hotspot Data
/// </summary>

// ── Enum de tipo de acción ────────────────────────────────────────────────────
public enum HotspotActionType
{
    /// <summary>Muestra el panel informativo clásico (comportamiento original).</summary>
    InfoPanel       = 0,

    /// <summary>Reproduce una cinemática en pantalla completa (Sistema 6).</summary>
    Cinematic       = 1,

    /// <summary>Abre un diálogo con un NPC con opciones de respuesta (Sistema 9).</summary>
    NpcConversation = 2,

    /// <summary>Simula una llamada al SIATA con opciones de reporte (Sistema 10).</summary>
    SiataCall       = 3,
}

// ── ScriptableObject ──────────────────────────────────────────────────────────
[CreateAssetMenu(fileName = "NewHotspot", menuName = "AR/Hotspot Data", order = 1)]
public class HotspotData : ScriptableObject
{
    // ── Contenido base ────────────────────────────────────────────────────────
    [Header("Contenido")]
    [Tooltip("Título principal del hotspot")]
    public string title = "Hotspot";

    [Tooltip("Descripción o información a mostrar (usado en InfoPanel)")]
    [TextArea(3, 8)]
    public string description = "Información del hotspot.";

    [Tooltip("Ícono opcional para el panel informativo")]
    public Sprite icon;

    // ── Tipo de acción ────────────────────────────────────────────────────────
    [Header("Tipo de acción")]
    [Tooltip("Qué ocurre al activar este hotspot:\n" +
             "• InfoPanel       → muestra panel de texto (comportamiento original)\n" +
             "• Cinematic       → reproduce un video en pantalla completa\n" +
             "• NpcConversation → abre diálogo con NPC y opciones de respuesta\n" +
             "• SiataCall       → simula una llamada al SIATA con opciones de reporte")]
    public HotspotActionType actionType = HotspotActionType.InfoPanel;

    [Header("Datos según tipo de acción")]
    [Tooltip("Video a reproducir. Solo se usa cuando actionType = Cinematic.")]
    public VideoClip cinematicClip;

    [Tooltip("Datos del diálogo. Solo se usa cuando actionType = NpcConversation o SiataCall.")]
    public NpcDialogueData dialogueData;

    // ── Activación ────────────────────────────────────────────────────────────
    [Header("Activación")]
    [Tooltip("Radio en unidades Unity para activación por proximidad")]
    public float triggerRadius = 3f;

    [Tooltip("Si está marcado, el hotspot también puede activarse con clic/tap")]
    public bool allowClick = true;

    [Tooltip("Etapa en que este hotspot es visible y activo.\n" +
             "-1 = visible en todas las etapas (comportamiento original).\n" +
             " 0 = solo en Intro,  1 = Etapa1,  2 = Etapa2,  etc.")]
    public int requiredStage = -1;

    // ── Visual ────────────────────────────────────────────────────────────────
    [Header("Visual")]
    [Tooltip("Color del marcador del hotspot en la escena")]
    public Color markerColor = new Color(0.2f, 0.8f, 1f, 0.9f);

    [Tooltip("Si está marcado, el objeto 3D del hotspot pulsa visualmente para llamar la atención")]
    public bool isBlinking = true;

    [Tooltip("Velocidad del pulso visual (ciclos por segundo). Solo aplica si isBlinking = true.")]
    [Range(0.5f, 4f)]
    public float blinkSpeed = 1.5f;
}
