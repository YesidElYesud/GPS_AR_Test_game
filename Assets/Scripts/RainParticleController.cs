using UnityEngine;

/// <summary>
/// RainParticleController — Sistema de lluvia autoconfigurado y optimizado para WebGL.
///
/// Setup (una sola vez):
///   1. Crear un GameObject vacío "Rain" en la escena.
///   2. Adjuntar este script (agrega ParticleSystem automáticamente).
///   3. Crear y asignar un Material de lluvia (ver comentario en campo rainMaterial).
///   4. Guardar como Prefab.
///
/// Material recomendado:
///   Project → Create → Material
///   Shader: "Particles/Standard Unlit"  (o "Legacy Shaders/Particles/Alpha Blended")
///   Rendering Mode: Transparent
///   Color: RGBA (190, 220, 255, 130)  ← azul claro, ~50% alpha
///   Sin textura necesaria para resultado básico.
///
/// API pública:
///   SetIntensity(RainIntensity)      — None / Light / Medium / Heavy
///   SetDensityNormalized(float 0-1)  — mapeo continuo a los 4 presets
///
/// Optimizaciones WebGL:
///   - simulationSpace = World (sin recálculo por transform)
///   - gravityModifier = 0 (sin física, velocidad constante via velocityOverLifetime)
///   - Shadows OFF en renderer
///   - maxParticles calculado exacto por preset (no derroche de memoria)
///   - Ningún módulo opcional activo (sin noise, trails, collision, sub-emitters)
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class RainParticleController : MonoBehaviour
{
    // ── Enum público ──────────────────────────────────────────────────────────
    public enum RainIntensity { None, Light, Medium, Heavy }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Intensidad")]
    [Tooltip("Preset activo al iniciar.")]
    public RainIntensity intensity = RainIntensity.Light;

    [Header("Área de lluvia")]
    [Tooltip("Ancho y largo del área emisora en metros.")]
    [Range(10f, 60f)]
    public float areaSize = 25f;

    [Tooltip("Altura del emisor sobre la cámara en metros.")]
    [Range(8f, 40f)]
    public float height = 20f;

    [Header("Movimiento")]
    [Tooltip("Velocidad de caída de las gotas (m/s).")]
    [Range(5f, 35f)]
    public float fallSpeed = 14f;

    [Tooltip("Seguir a la Main Camera cada frame.")]
    public bool followCamera = true;

    [Header("Visual")]
    [Tooltip(
        "Material de partícula para las gotas.\n" +
        "Cómo crearlo:\n" +
        "  1. Project → Create → Material\n" +
        "  2. Shader: Particles/Standard Unlit\n" +
        "  3. Rendering Mode: Transparent\n" +
        "  4. Color: azul claro, ~50% alpha\n" +
        "Null = usa el material default de Unity (rosa en editor, blanco en build)."
    )]
    public Material rainMaterial;

    // ── Presets: (emissionRate, sizeMin, sizeMax) ─────────────────────────────
    // Diseñados para WebGL móvil. Heavy = ~560 partículas simultáneas máximo.
    private static readonly (float rate, float sMin, float sMax)[] k_Presets =
    {
        (  0f, 0.000f, 0.000f),   // None
        ( 80f, 0.015f, 0.030f),   // Light   → ~112 partículas activas
        (200f, 0.025f, 0.040f),   // Medium  → ~280 partículas activas
        (400f, 0.035f, 0.055f),   // Heavy   → ~560 partículas activas
    };

    // ── Internos ──────────────────────────────────────────────────────────────
    private ParticleSystem         _ps;
    private ParticleSystemRenderer _renderer;
    private Transform              _cam;
    private float                  _cachedLifetime;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        _ps       = GetComponent<ParticleSystem>();
        _renderer = GetComponent<ParticleSystemRenderer>();

        // Sin rotación: la dirección de caída la controla velocityOverLifetime
        // en espacio mundo → funciona independiente de la orientación del objeto.
        transform.rotation = Quaternion.identity;

        BuildParticleSystem();
    }

    private void Start()
    {
        Camera cam = Camera.main;
        if (cam != null) _cam = cam.transform;

        SnapToCamera();
        ApplyIntensity(intensity);
    }

    private void LateUpdate()
    {
        if (followCamera && _cam != null)
            SnapToCamera();
    }

    // ── API pública ────────────────────────────────────────────────────────────

    /// <summary>Cambia el preset de intensidad.</summary>
    public void SetIntensity(RainIntensity newIntensity)
    {
        intensity = newIntensity;
        ApplyIntensity(newIntensity);
    }

    /// <summary>Control continuo: 0 = sin lluvia · 1 = Heavy.</summary>
    public void SetDensityNormalized(float t)
    {
        t = Mathf.Clamp01(t);
        SetIntensity((RainIntensity)Mathf.RoundToInt(t * 3f));
    }

    // ── Construcción del ParticleSystem (una vez en Awake) ────────────────────
    private void BuildParticleSystem()
    {
        _cachedLifetime = (height / fallSpeed) + 0.5f;   // tiempo de caída + buffer
        int maxP = Mathf.CeilToInt(k_Presets[(int)RainIntensity.Heavy].rate * _cachedLifetime * 1.5f);

        // ── Main ──────────────────────────────────────────────────────────────
        var main = _ps.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.startSpeed      = 0f;          // la velocidad la define velocityOverLifetime
        main.startLifetime   = new ParticleSystem.MinMaxCurve(_cachedLifetime * 0.85f,
                                                               _cachedLifetime * 1.15f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.72f, 0.87f, 1f, 0.30f),
                                   new Color(0.88f, 0.95f, 1f, 0.55f));
        main.maxParticles    = maxP;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.stopAction      = ParticleSystemStopAction.Disable;

        // ── Shape: caja plana horizontal (sin rotación del transform) ──────────
        var shape = _ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        // Y = 0.2 → slab delgado en altura; X y Z = área de cobertura
        shape.scale     = new Vector3(areaSize, 0.2f, areaSize);

        // ── VelocityOverLifetime: empuja las gotas hacia abajo en mundo ─────────
        // Este módulo garantiza caída vertical sin importar la rotación del objeto.
        var vol = _ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space   = ParticleSystemSimulationSpace.World;
        // Los tres ejes deben usar el mismo modo (TwoConstants).
        vol.x       = new ParticleSystem.MinMaxCurve(0f, 0f);
        vol.y       = new ParticleSystem.MinMaxCurve(-fallSpeed * 1.08f, -fallSpeed * 0.92f);
        vol.z       = new ParticleSystem.MinMaxCurve(0f, 0f);

        // ── Emission ──────────────────────────────────────────────────────────
        var emission = _ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;

        // ── Renderer: gotas alargadas según velocidad de caída ─────────────────
        _renderer.renderMode        = ParticleSystemRenderMode.Stretch;
        _renderer.velocityScale     = 0.04f;    // elongación proporcional a la velocidad
        _renderer.lengthScale       = 1.8f;     // elongación base
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows    = false;
        _renderer.sortingFudge      = -1f;

        if (rainMaterial != null)
            _renderer.material = rainMaterial;
    }

    // ── Aplicar preset ────────────────────────────────────────────────────────
    private void ApplyIntensity(RainIntensity preset)
    {
        var (rate, sMin, sMax) = k_Presets[(int)preset];
        var emission = _ps.emission;

        if (preset == RainIntensity.None)
        {
            emission.rateOverTime = 0f;
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            return;
        }

        int maxP = Mathf.CeilToInt(rate * _cachedLifetime * 1.5f);

        var main = _ps.main;
        main.startSize    = new ParticleSystem.MinMaxCurve(sMin, sMax);
        main.maxParticles = maxP;

        emission.rateOverTime = rate;

        if (!_ps.isPlaying) _ps.Play();
    }

    // ── Helper ────────────────────────────────────────────────────────────────
    private void SnapToCamera()
    {
        Vector3 p = _cam.position;
        transform.position = new Vector3(p.x, p.y + height, p.z);
    }

    // ── Editor: preview en Play mode ──────────────────────────────────────────
    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (_ps == null) _ps = GetComponent<ParticleSystem>();
        if (_ps != null) ApplyIntensity(intensity);
    }
}
