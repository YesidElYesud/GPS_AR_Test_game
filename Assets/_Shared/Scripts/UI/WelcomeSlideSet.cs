using UnityEngine;

/// <summary>
/// Conjunto de slides de bienvenida para una escena / SAC específico.
///
/// Crear via  Assets > Create > AR > Welcome Slide Set
/// y asignar en el campo "Slide Set" del WelcomePanel de la escena correspondiente.
///
/// Si el campo está asignado, el WelcomePanel ignora el array inline "slideData"
/// y usa estos slides. Si queda vacío, el WelcomePanel sigue usando su array interno
/// (retrocompatibilidad total).
///
/// Convención de nombres:
///   SAC_Olaya/   → WelcomeSlideSet_Olaya.asset
///   SAC_Rita/    → WelcomeSlideSet_Rita.asset
/// </summary>
[CreateAssetMenu(menuName = "AR/Welcome Slide Set", fileName = "WelcomeSlideSet")]
public class WelcomeSlideSet : ScriptableObject
{
    [Tooltip("Slides a mostrar en el onboarding de esta escena/SAC.")]
    public WelcomePanel.WelcomeSlideData[] slides;
}
