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

// ── Nivel de riesgo (Sistema 5 + Sistema 13) ─────────────────────────────────
/// <summary>
/// Nivel de riesgo asociado a un hotspot o zona.
/// Usado por HotspotUIPanel (badge de color) y RiskLevelIndicator (HUD).
/// </summary>
public enum RiskLevel
{
    None = 0,   // Sin nivel asignado — badge oculto
    N1   = 1,   // Bajo     — verde
    N2   = 2,   // Moderado — amarillo
    N3   = 3,   // Alto     — naranja
    N4   = 4,   // Crítico  — rojo
}

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

    /// <summary>Muestra slides secuenciales de contenido educativo (Programación 1+).</summary>
    InfoSlidePanel  = 4,

    /// <summary>
    /// Ejecuta una secuencia de tomas de cámara en tiempo real sobre la escena del juego.
    /// Usa CinematicSequencer (componente en escena) — no requiere video externo.
    /// Ideal para mostrar el estado atmosférico/ambiental actual (Programación 2, 5, etc.).
    /// </summary>
    CameraSequence  = 5,
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
    [Tooltip("VideoClip a reproducir en editor/standalone. Solo se usa cuando actionType = Cinematic.")]
    public VideoClip cinematicClip;

    [Tooltip("URL del video para WebGL (streaming). Requerida en builds WebGL porque VideoClip no es compatible.\n" +
             "Ejemplo: 'StreamingAssets/Videos/clip.mp4' o URL remota HTTPS.")]
    public string cinematicUrl = "";

    [Tooltip("Si es true, al terminar (o saltar) la cinemática se avanza a la siguiente etapa.")]
    public bool cinematicAdvancesStage = true;

    [Tooltip("Datos del diálogo. Solo se usa cuando actionType = NpcConversation o SiataCall.")]
    public NpcDialogueData dialogueData;

    [Tooltip("Slides a mostrar cuando actionType = InfoSlidePanel.\n" +
             "Ejemplo Botón 1:\n" +
             "  [0] ¿Qué es una cuenca torrencial?\n" +
             "  [1] Historia de crecientes en La Iguaná\n" +
             "  [2] Cómo la morfología influye en el riesgo")]
    public InfoSlideData[] infoSlides;

    [Tooltip("Si true, al cerrar el último slide se avanza a la siguiente etapa (NextStage).")]
    public bool infoSlideAdvancesStage = true;

    [Tooltip("Si true, al terminar (o saltar) la secuencia de cámara se avanza a la siguiente etapa.\n" +
             "Solo aplica cuando actionType = CameraSequence.")]
    public bool sequenceAdvancesStage = true;

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

    // ── Panel enriquecido ─────────────────────────────────────────────────────
    [Header("Panel Enriquecido")]
    [Tooltip("Imagen de cabecera mostrada en la parte superior del panel informativo.\n" +
             "Opcional: si es null el header se oculta automáticamente.")]
    public Sprite headerImage;

    [Tooltip("Nivel de riesgo de esta zona. Controla el color del badge en el panel\n" +
             "y será leído por el RiskLevelIndicator (HUD). None = badge oculto.")]
    public RiskLevel riskLevel = RiskLevel.None;

    // ── Visual ────────────────────────────────────────────────────────────────
    [Header("Visual")]
    [Tooltip("Color del marcador del hotspot en la escena")]
    public Color markerColor = new Color(0.2f, 0.8f, 1f, 0.9f);

    [Tooltip("Material aplicado al segundo slot de la malla hija del hotspot.\n" +
             "Opcional: si es null se mantiene el material original.")]
    public Material hotspotMaterial;

    [Tooltip("Si está marcado, el objeto 3D del hotspot pulsa visualmente para llamar la atención")]
    public bool isBlinking = true;

    [Tooltip("Velocidad del pulso visual (ciclos por segundo). Solo aplica si isBlinking = true.")]
    [Range(0.5f, 4f)]
    public float blinkSpeed = 1.5f;
}
