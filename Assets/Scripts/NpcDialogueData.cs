using UnityEngine;

/// <summary>
/// NpcDialogueData — ScriptableObject con los datos de un diálogo de NPC.
///
/// STUB: estructura mínima para que HotspotData compile.
/// Se expande completamente en Sistema 8 (NpcController + NpcDialoguePanel).
///
/// Crear via: Assets > Create > AR > NPC Dialogue Data
/// </summary>
[CreateAssetMenu(fileName = "NewDialogue", menuName = "AR/NPC Dialogue Data", order = 2)]
public class NpcDialogueData : ScriptableObject
{
    [Header("NPC")]
    [Tooltip("Nombre del NPC que habla (ej: Líder comunitario, Vecino, SIATA)")]
    public string npcName = "NPC";

    [Tooltip("Foto o sprite del NPC para el panel de diálogo")]
    public Sprite npcPhoto;

    [Header("Diálogo")]
    [Tooltip("Texto que dice el NPC al iniciar la conversación")]
    [TextArea(3, 6)]
    public string npcText = "Texto del NPC.";

    // Los campos de opciones múltiples (DialogueOption[]) se agregan en Sistema 8.
}
