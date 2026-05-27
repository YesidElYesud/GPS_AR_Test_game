using UnityEngine;

/// <summary>
/// Línea de diálogo con texto + clip de voz opcional.
/// Usar en NpcDialogueData.dialogueEntries[].
/// </summary>
[System.Serializable]
public class DialogueLine
{
    [TextArea(2, 5)]
    [Tooltip("Texto que aparece en el panel de diálogo.")]
    public string text;

    [Tooltip("Clip de voz que se reproduce al mostrar esta línea. " +
             "Si el jugador avanza antes de que termine, se corta y arranca el siguiente. " +
             "Dejar vacío para líneas sin narración.")]
    public AudioClip audio;
}

/// <summary>
/// DialogueOption — Una opción de respuesta dentro de un diálogo.
/// Serializable para aparecer como lista editable en el Inspector.
/// </summary>
[System.Serializable]
public class DialogueOption
{
    [Tooltip("Texto de la opción que ve el jugador en el botón")]
    public string optionText = "Opción";

    [Tooltip("Marca esta opción como la respuesta correcta")]
    public bool isCorrect = false;

    [Tooltip("Texto que aparece como feedback al seleccionar esta opción.\n" +
             "Correcto: refuerza el aprendizaje.\n" +
             "Incorrecto: explica por qué no es la mejor decisión.")]
    [TextArea(2, 5)]
    public string feedbackText = "Feedback de la opción.";
}

/// <summary>
/// NpcDialogueData — ScriptableObject con los datos completos de un diálogo de NPC.
///
/// Usado por NpcDialoguePanel y SiataCallPanel (ambos comparten la misma estructura).
/// Crear via: Assets > Create > AR > NPC Dialogue Data
///
/// Flujo esperado:
///   1. Asignar al campo dialogueData del HotspotData correspondiente.
///   2. Configurar npcName, npcPhoto y npcText.
///   3. Agregar 3 opciones en el array options (solo una con isCorrect = true).
///   4. Ajustar advancesStageOnCorrect y correctAnswerDelay según la etapa.
/// </summary>
[CreateAssetMenu(fileName = "NewDialogue", menuName = "AR/NPC Dialogue Data", order = 2)]
public class NpcDialogueData : ScriptableObject
{
    // ── NPC ───────────────────────────────────────────────────────────────────
    [Header("Identificación del NPC")]
    [Tooltip("Nombre del NPC (ej: Líder comunitario, Vecino, Operador SIATA)")]
    public string npcName = "NPC";

    [Tooltip("Foto o sprite del NPC que aparece en el panel de diálogo")]
    public Sprite npcPhoto;

    // ── Diálogo ───────────────────────────────────────────────────────────────
    [Header("Diálogo paginado con audio")]
    [Tooltip("Cada entrada tiene texto + clip de voz opcional.\n" +
             "Si este array tiene contenido, se ignoran los campos legados de abajo.\n" +
             "Crear via Assets > Create > AR > NPC Dialogue Data.")]
    public DialogueLine[] dialogueEntries;

    [Header("Legado — solo si dialogueEntries está vacío")]
    [Tooltip("Líneas de texto sin audio (compatibilidad con assets anteriores).")]
    public string[] dialogueLines;

    [Tooltip("Texto único del NPC. Ignorado si dialogueLines o dialogueEntries tienen contenido.")]
    [TextArea(3, 6)]
    public string npcText = "Texto del NPC.";

    // ── Opciones de respuesta ─────────────────────────────────────────────────
    [Header("Opciones de respuesta")]
    [Tooltip("Lista de opciones (A, B, C…). Solo una debe tener isCorrect = true.")]
    public DialogueOption[] options;

    // ── Comportamiento ────────────────────────────────────────────────────────
    [Header("Comportamiento tras la respuesta")]
    [Tooltip("Si true, responder correctamente avanza la etapa con StageManager.NextStage().\n" +
             "Poner en false para diálogos puramente informativos.")]
    public bool advancesStageOnCorrect = true;

    [Tooltip("Segundos de pausa tras respuesta correcta antes de cerrar el panel y avanzar.")]
    [Range(0.5f, 4f)]
    public float correctAnswerDelay = 1.5f;
}
