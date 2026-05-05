using UnityEngine;

/// <summary>
/// PulsingPlane — pulso suave de escala en un plano cuando el jugador está cerca
/// y el nivel de riesgo es N2, N3 o N4.
///
/// Setup:
///   1. Adjuntar este script al plano.
///   2. El script exige un SphereCollider (se añade automáticamente si falta).
///      Ajustar el radio en el Inspector; el collider se fuerza a isTrigger en Awake.
///   3. Si el plano tiene MeshCollider, asegurarse de que no bloquee al jugador
///      (marcarlo isTrigger o eliminarlo según necesidad).
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class PulsingPlane : MonoBehaviour
{
    [Header("Pulso")]
    [Range(0f, 0.12f)]
    [Tooltip("Amplitud del pulso como fracción de la escala original. 0.03 = ±3%.")]
    public float pulseAmplitude = 0.03f;

    [Range(0.1f, 3f)]
    [Tooltip("Velocidad del pulso en ciclos por segundo.")]
    public float pulseSpeed = 0.7f;

    [Header("Transición")]
    [Range(0.1f, 2f)]
    [Tooltip("Tiempo en segundos para que el pulso alcance su amplitud máxima al entrar en rango, " +
             "y para desvanecerse al salir.")]
    public float fadeTime = 0.8f;

    // ── Estado interno ────────────────────────────────────────────────────────
    private Vector3 _originalScale;
    private bool    _playerInRange;
    private float   _pulseWeight;   // 0 = estático, 1 = pulso a plena amplitud

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        _originalScale = transform.localScale;

        // Garantiza que el SphereCollider sea siempre un trigger
        GetComponent<SphereCollider>().isTrigger = true;
    }

    private void Update()
    {
        bool active = _playerInRange && IsActivePulseLevel();

        // Fade suave hacia el estado objetivo
        float target = active ? 1f : 0f;
        _pulseWeight = Mathf.MoveTowards(_pulseWeight, target, Time.deltaTime / fadeTime);

        if (_pulseWeight > 0f)
        {
            float t = 1f + pulseAmplitude * _pulseWeight * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f);
            transform.localScale = _originalScale * t;
        }
        else
        {
            // Escala exacta al bajar a cero; evita deriva por punto flotante
            transform.localScale = _originalScale;
        }
    }

    // ── Detección de proximidad ───────────────────────────────────────────────
    // CharacterController dispara OnTrigger* igual que un Rigidbody
    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<CharacterController>() != null)
            _playerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<CharacterController>() != null)
            _playerInRange = false;
    }

    // ── Condición de nivel ────────────────────────────────────────────────────
    private bool IsActivePulseLevel()
    {
        if (RiskLevelIndicator.Instance == null) return false;
        RiskLevel level = RiskLevelIndicator.Instance.CurrentLevel;
        return level == RiskLevel.N2 || level == RiskLevel.N3 || level == RiskLevel.N4;
    }
}
