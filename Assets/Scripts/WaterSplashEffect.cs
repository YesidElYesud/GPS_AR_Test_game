using UnityEngine;

/// <summary>
/// Efecto de salpicadura de agua contra rocas.
///
/// SETUP:
///   1. Crear un GameObject vacío, posicionarlo donde la roca toca el agua.
///   2. Agregar este componente.
///   3. Ajustar Spray Direction con la flecha del Gizmo en la escena,
///      o escribir el vector directamente (ej: (0.3, 1, 0) = arriba con inclinación).
///   4. Usar Emission Rate y Particle Size para controlar volumen y tamaño.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class WaterSplashEffect : MonoBehaviour
{
    public enum SplashIntensity { Off, Light, Normal, Heavy }

    // ─── Dirección ────────────────────────────────────────────────────────
    [Header("Dirección del spray")]
    [Tooltip("Hacia dónde salen las partículas en espacio mundo.\n" +
             "(0,1,0) = recto arriba | (0.5,1,0) = arriba-derecha\n" +
             "NO modifica la posición/rotación del GameObject.")]
    [SerializeField] private Vector3 sprayDirection = new Vector3(0f, 1f, 0.3f);

    [Tooltip("Apertura del cono de dispersión en grados.")]
    [SerializeField] [Range(5f, 80f)] private float spreadAngle = 35f;

    // ─── Volumen y tamaño ─────────────────────────────────────────────────
    [Header("Volumen y tamaño")]
    [Tooltip("Cuántas partículas por segundo se emiten.")]
    [SerializeField] [Range(0f, 120f)] private float emissionRate = 35f;

    [Tooltip("Tamaño base de cada partícula (metros). Auméntalo si se ven pequeñas.")]
    [SerializeField] [Range(0.05f, 2f)] private float particleSize = 0.35f;

    [Tooltip("Velocidad de salida de las partículas.")]
    [SerializeField] [Range(0.1f, 8f)] private float launchSpeed = 2.5f;

    [Tooltip("Tiempo de vida de cada partícula (segundos).")]
    [SerializeField] [Range(0.2f, 3f)] private float lifetime = 0.8f;

    // ─── Apariencia ───────────────────────────────────────────────────────
    [Header("Apariencia")]
    [SerializeField] private Color splashColor = new Color(0.75f, 0.90f, 1f, 0.85f);

    [Tooltip("Cuánto cae por gravedad (0 = sin gravedad, 1 = gravedad normal).")]
    [SerializeField] [Range(0f, 2f)] private float gravity = 0.7f;

    // ─── Control de etapa ─────────────────────────────────────────────────
    [Header("Estado")]
    [SerializeField] private SplashIntensity intensity = SplashIntensity.Normal;

    // Multiplicadores por intensidad (aplicados sobre los valores del Inspector)
    //                                              Off   Light  Normal  Heavy
    private static readonly float[] RateMult   = { 0f,   0.4f,  1.0f,  2.0f };
    private static readonly float[] SizeMult   = { 0f,   0.7f,  1.0f,  1.4f };
    private static readonly float[] SpeedMult  = { 0f,   0.6f,  1.0f,  1.6f };

    private ParticleSystem _ps;

    // ──────────────────────────────────────────────────────────────────────
    void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
        ApplySettings();
    }

    // ─── API pública ──────────────────────────────────────────────────────

    public void SetIntensity(SplashIntensity newIntensity)
    {
        intensity = newIntensity;
        ApplySettings();
    }

    /// <summary>Dirección de flujo del río → orienta el spray.</summary>
    public void SetFlowDirection(Vector3 dir)
    {
        // El spray sube ligeramente y se inclina en la dirección del flujo
        Vector3 d = dir.normalized;
        sprayDirection = (Vector3.up * 1.5f + d * 0.5f).normalized;
        ApplySettings();
    }

    public void SetColor(Color color)
    {
        splashColor = color;
        ApplySettings();
    }

    public void LerpColor(Color from, Color to, float t)
        => SetColor(Color.Lerp(from, to, Mathf.Clamp01(t)));

    // ─── Configuración de módulos del PS ─────────────────────────────────

    private void ApplySettings()
    {
        if (_ps == null) _ps = GetComponent<ParticleSystem>();

        int idx = (int)intensity;

        float rate  = emissionRate * RateMult[idx];
        float size  = particleSize * SizeMult[idx];
        float speed = launchSpeed  * SpeedMult[idx];

        // ── Main ──────────────────────────────────────────────────────────
        var main = _ps.main;
        main.loop            = true;
        main.playOnAwake     = intensity != SplashIntensity.Off;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = Mathf.Max(1, Mathf.RoundToInt(rate * lifetime * 2f));
        main.gravityModifier = gravity;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
        main.startSize       = new ParticleSystem.MinMaxCurve(size * 0.5f, size);

        Color c1 = new Color(splashColor.r, splashColor.g, splashColor.b, splashColor.a * 0.45f);
        main.startColor = new ParticleSystem.MinMaxGradient(splashColor, c1);

        // ── Emission ──────────────────────────────────────────────────────
        var emission = _ps.emission;
        emission.enabled      = intensity != SplashIntensity.Off;
        emission.rateOverTime = rate;

        // ── Shape: cono apuntando en sprayDirection ───────────────────────
        // IMPORTANTE: rotamos el módulo Shape, NO el transform del GO.
        var shape = _ps.shape;
        shape.enabled         = true;
        shape.shapeType       = ParticleSystemShapeType.Cone;
        shape.angle           = spreadAngle;
        shape.radius          = 0.08f;
        shape.radiusThickness = 1f;

        // Calcular la rotación del cono para que apunte en sprayDirection
        Vector3 dir = sprayDirection.sqrMagnitude > 0.001f
                      ? sprayDirection.normalized : Vector3.up;
        // El cono del PS emite por defecto en +Y local; lo rotamos al target
        shape.rotation = Quaternion.FromToRotation(Vector3.up, dir).eulerAngles;

        // ── Velocity over lifetime: desactivado (el shape ya orienta) ─────
        var vol = _ps.velocityOverLifetime;
        vol.enabled = false;

        // ── Size over lifetime: aparece rápido, se encoge al final ────────
        var sol = _ps.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f,   0.2f),
            new Keyframe(0.15f, 1f),
            new Keyframe(1f,   0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // ── Color over lifetime: fade in rápido, fade out suave ───────────
        var col = _ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f,   0f),
                new GradientAlphaKey(1f,   0.1f),
                new GradientAlphaKey(0.8f, 0.6f),
                new GradientAlphaKey(0f,   1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // ── Renderer ──────────────────────────────────────────────────────
        var rend = _ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode        = ParticleSystemRenderMode.Billboard;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows    = false;

        // ── Play / Stop ───────────────────────────────────────────────────
        if (Application.isPlaying)
        {
            if (intensity == SplashIntensity.Off)
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            else if (!_ps.isPlaying)
                _ps.Play();
        }
    }

    // Dibuja una flecha en la escena para visualizar sprayDirection
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
        Vector3 dir = sprayDirection.sqrMagnitude > 0.001f
                      ? sprayDirection.normalized : Vector3.up;
        Gizmos.DrawRay(transform.position, dir * 1.5f);
        Gizmos.DrawSphere(transform.position + dir * 1.5f, 0.06f);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (_ps == null) _ps = GetComponent<ParticleSystem>();
        ApplySettings();
    }
#endif
}
