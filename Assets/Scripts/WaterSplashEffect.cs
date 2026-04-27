using UnityEngine;

/// <summary>
/// Efecto de salpicadura de agua contra roca — cuatro capas de partículas:
///   Jet   : chorro principal ascendente (StretchedBillboard, elongado)
///   Crown : corona lateral que se abre y cae de vuelta al agua
///   Mist  : niebla fina que flota arriba del impacto
///   Foam  : espuma blanca burbujeante en la base
///
/// SETUP:
///   1. Posicionar el GO donde la roca toca el agua.
///   2. Ajustar Spray Direction con la flecha del Gizmo.
///   3. WaterSplashManager escala la intensidad automáticamente por etapa.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class WaterSplashEffect : MonoBehaviour
{
    public enum SplashIntensity { Off, Light, Normal, Heavy }

    [Header("Dirección del spray")]
    [Tooltip("Hacia dónde sube el chorro. (0,1,0.3) = arriba con inclinación hacia la cámara.")]
    [SerializeField] private Vector3 sprayDirection = new Vector3(0f, 1f, 0.3f);

    [Tooltip("Apertura del cono del jet en grados. El cono de corona es siempre 65°.")]
    [SerializeField] [Range(5f, 55f)] private float spreadAngle = 22f;

    [Header("Color")]
    [Tooltip("Color base del agua. Cámbialo a marrón en Etapa3-4 con SetColor().")]
    [SerializeField] private Color splashColor = new Color(0.78f, 0.92f, 1f, 0.95f);

    [Header("Estado")]
    [SerializeField] private SplashIntensity intensity = SplashIntensity.Normal;

    // ── Sub-sistemas ──────────────────────────────────────────────────────────
    private ParticleSystem _psJet;    // chorro principal (este componente)
    private ParticleSystem _psCrown;  // corona lateral
    private ParticleSystem _psMist;   // niebla flotante
    private ParticleSystem _psFoam;   // espuma base

    // ── Presets por intensidad ────────────────────────────────────────────────
    private struct Preset
    {
        public float jetRate,   crownRate,   mistRate,   foamRate;
        public float jetSpeed,  crownSpeed;
        public float jetSize,   crownSize,   mistSize,   foamSize;
        public float jetLife,   crownLife,   mistLife,   foamLife;
        public short burstCount;
        public float burstInterval;
    }

    private static readonly Preset[] _presets =
    {
        // [0] Off — todo desactivado
        default,

        // [1] Light — río en calma, choque suave
        new Preset
        {
            jetRate=18,   crownRate=8,   mistRate=10,  foamRate=50,
            jetSpeed=2.2f, crownSpeed=1.0f,
            jetSize=0.06f, crownSize=0.05f, mistSize=0.22f, foamSize=0.04f,
            jetLife=0.55f, crownLife=0.50f, mistLife=1.1f,  foamLife=0.28f,
            burstCount=0,  burstInterval=0f
        },

        // [2] Normal — lluvia leve, caudal moderado
        new Preset
        {
            jetRate=55,   crownRate=30,  mistRate=30,  foamRate=110,
            jetSpeed=5.0f, crownSpeed=2.5f,
            jetSize=0.09f, crownSize=0.07f, mistSize=0.28f, foamSize=0.05f,
            jetLife=0.65f, crownLife=0.55f, mistLife=1.3f,  foamLife=0.33f,
            burstCount=10, burstInterval=2.0f
        },

        // [3] Heavy — tormenta, caudal violento
        new Preset
        {
            jetRate=120,  crownRate=80,  mistRate=65,  foamRate=240,
            jetSpeed=9.5f, crownSpeed=4.8f,
            jetSize=0.13f, crownSize=0.10f, mistSize=0.34f, foamSize=0.06f,
            jetLife=0.80f, crownLife=0.65f, mistLife=1.5f,  foamLife=0.40f,
            burstCount=22, burstInterval=1.0f
        },
    };

    // ── Ciclo de vida ─────────────────────────────────────────────────────────
    void Awake()
    {
        EnsureSubSystems();
        ApplySettings();
    }

    // ── API pública (compatible con WaterSplashManager) ───────────────────────

    public void SetIntensity(SplashIntensity newIntensity)
    {
        intensity = newIntensity;
        ApplySettings();
    }

    public void SetFlowDirection(Vector3 dir)
    {
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

    // ── Creación de sub-sistemas ──────────────────────────────────────────────
    private void EnsureSubSystems()
    {
        _psJet   = GetComponent<ParticleSystem>();
        _psCrown = GetOrCreateChildPS("_Crown");
        _psMist  = GetOrCreateChildPS("_Mist");
        _psFoam  = GetOrCreateChildPS("_Foam");
    }

    private ParticleSystem GetOrCreateChildPS(string suffix)
    {
        string childName = gameObject.name + suffix;
        Transform existing = transform.Find(childName);
        if (existing != null)
        {
            var ep = existing.GetComponent<ParticleSystem>();
            if (ep != null) return ep;
        }
        var go = new GameObject(childName);
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;
        return go.AddComponent<ParticleSystem>();
    }

    // ── Aplicar configuración ─────────────────────────────────────────────────
    private void ApplySettings()
    {
        if (_psJet == null) EnsureSubSystems();

        bool off = intensity == SplashIntensity.Off;
        Preset p = off ? default : _presets[(int)intensity];

        Vector3 dir = sprayDirection.sqrMagnitude > 0.001f
                      ? sprayDirection.normalized : Vector3.up;

        ConfigureJet  (_psJet,   p, dir, off);
        ConfigureCrown(_psCrown, p, dir, off);
        ConfigureMist (_psMist,  p,      off);
        ConfigureFoam (_psFoam,  p,      off);

        if (!Application.isPlaying) return;
        PlayOrStop(_psJet,   off);
        PlayOrStop(_psCrown, off);
        PlayOrStop(_psMist,  off);
        PlayOrStop(_psFoam,  off);
    }

    // ── JET — chorro principal ascendente ─────────────────────────────────────
    // Partículas elongadas (StretchedBillboard) que suben con fuerza y caen.
    // Son el elemento más visible del choque.
    private void ConfigureJet(ParticleSystem ps, Preset p, Vector3 dir, bool off)
    {
        var main = ps.main;
        main.loop            = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
        main.startLifetime   = new ParticleSystem.MinMaxCurve(p.jetLife * 0.65f, p.jetLife);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(p.jetSpeed * 0.65f, p.jetSpeed);
        main.startSize       = new ParticleSystem.MinMaxCurve(p.jetSize  * 0.5f,  p.jetSize);
        main.maxParticles    = Mathf.Max(1, off ? 1 : Mathf.RoundToInt(p.jetRate * p.jetLife * 2.5f));

        Color c0 = splashColor;
        Color c1 = new Color(c0.r, c0.g, c0.b, c0.a * 0.35f);
        main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);

        var emission = ps.emission;
        emission.enabled      = !off;
        emission.rateOverTime = p.jetRate;
        SetBursts(emission, p);

        // Cono estrecho — jet concentrado que sube casi vertical
        var shape = ps.shape;
        shape.enabled         = true;
        shape.shapeType       = ParticleSystemShapeType.Cone;
        shape.angle           = spreadAngle;
        shape.radius          = 0.05f;
        shape.radiusThickness = 1f;
        shape.rotation        = Quaternion.FromToRotation(Vector3.up, dir).eulerAngles;

        // Noise: turbulencia orgánica del agua
        var noise = ps.noise;
        noise.enabled     = !off;
        noise.strength    = new ParticleSystem.MinMaxCurve(0.3f, 0.65f);
        noise.frequency   = 1.8f;
        noise.scrollSpeed = 0.5f;
        noise.quality     = ParticleSystemNoiseQuality.Medium;

        // Size over lifetime: aparece rápido, se encoge
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size    = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f,    0.0f, 0f, 8f),
            new Keyframe(0.10f, 1.0f),
            new Keyframe(0.70f, 0.5f),
            new Keyframe(1f,    0.0f)));

        // Color over lifetime: aparece rápido, se desvanece al final
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color   = MakeAlphaGradient(0f, 0.08f, 0.9f,  0.55f, 0.9f,  0.0f, 1.0f);

        // StretchedBillboard: el jet se ve elongado según la velocidad de la partícula
        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode        = ParticleSystemRenderMode.StretchedBillboard;
        rend.velocityScale     = 0.14f;
        rend.lengthScale       = 1.3f;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows    = false;
    }

    // ── CROWN — corona lateral ────────────────────────────────────────────────
    // Cono muy abierto emitiendo solo desde el borde (radiusThickness=0).
    // Las partículas salen a 65° del eje, arcan hacia afuera y caen al agua.
    // Crea el efecto de "corona" visible en chorros reales de agua contra roca.
    private void ConfigureCrown(ParticleSystem ps, Preset p, Vector3 dir, bool off)
    {
        var main = ps.main;
        main.loop            = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = new ParticleSystem.MinMaxCurve(1.5f, 2.0f);
        main.startLifetime   = new ParticleSystem.MinMaxCurve(p.crownLife * 0.55f, p.crownLife);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(p.crownSpeed * 0.45f, p.crownSpeed);
        main.startSize       = new ParticleSystem.MinMaxCurve(p.crownSize  * 0.4f,  p.crownSize);
        main.maxParticles    = Mathf.Max(1, off ? 1 : Mathf.RoundToInt(p.crownRate * p.crownLife * 2f));

        Color c0 = new Color(splashColor.r, splashColor.g, splashColor.b, splashColor.a * 0.85f);
        Color c1 = new Color(c0.r, c0.g, c0.b, c0.a * 0.25f);
        main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);

        var emission = ps.emission;
        emission.enabled      = !off;
        emission.rateOverTime = p.crownRate;
        // La corona también recibe bursts (la mitad del jet)
        if (!off && p.burstCount > 0 && p.burstInterval > 0f)
            emission.SetBursts(new[] {
                new ParticleSystem.Burst(0f, (short)(p.burstCount / 2), (short)(p.burstCount / 2),
                                         int.MaxValue, p.burstInterval) });
        else
            emission.SetBursts(new ParticleSystem.Burst[0]);

        // Cono ancho emitiendo solo desde el borde — crea la forma de corona
        var shape = ps.shape;
        shape.enabled         = true;
        shape.shapeType       = ParticleSystemShapeType.Cone;
        shape.angle           = 65f;
        shape.radius          = 0.12f;
        shape.radiusThickness = 0f;  // solo desde el borde exterior → corona
        shape.rotation        = Quaternion.FromToRotation(Vector3.up, dir).eulerAngles;

        var noise = ps.noise;
        noise.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size    = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f,    0.15f),
            new Keyframe(0.20f, 1.0f),
            new Keyframe(1f,    0.0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color   = MakeAlphaGradient(0f, 0.05f, 1.0f, 0.55f, 0.7f, 0.0f, 1.0f);

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode        = ParticleSystemRenderMode.StretchedBillboard;
        rend.velocityScale     = 0.08f;
        rend.lengthScale       = 1.1f;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows    = false;
    }

    // ── MIST — niebla flotante ────────────────────────────────────────────────
    // Partículas grandes y muy transparentes que se disuelven hacia arriba.
    // Dan volumen y atmósfera alrededor del impacto.
    private void ConfigureMist(ParticleSystem ps, Preset p, bool off)
    {
        var main = ps.main;
        main.loop            = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.06f, 0.04f);
        main.startLifetime   = new ParticleSystem.MinMaxCurve(p.mistLife * 0.65f, p.mistLife);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.08f, 0.35f);
        main.startSize       = new ParticleSystem.MinMaxCurve(p.mistSize * 0.55f, p.mistSize * 1.5f);
        main.maxParticles    = Mathf.Max(1, off ? 1 : Mathf.RoundToInt(p.mistRate * p.mistLife * 1.5f));

        // Niebla casi blanca con tinte del agua
        float r = splashColor.r * 0.85f + 0.15f;
        float g = splashColor.g * 0.85f + 0.15f;
        float b = splashColor.b * 0.85f + 0.15f;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 1f, 1f, 0.30f),
            new Color(r,  g,  b,  0.15f));

        var emission = ps.emission;
        emission.enabled      = !off;
        emission.rateOverTime = p.mistRate;
        emission.SetBursts(new ParticleSystem.Burst[0]);

        // Hemisferio apuntando arriba — la niebla sube del impacto
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius    = 0.18f;

        // Noise suave: la niebla flota orgánicamente
        var noise = ps.noise;
        noise.enabled     = !off;
        noise.strength    = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
        noise.frequency   = 0.7f;
        noise.scrollSpeed = 0.18f;
        noise.quality     = ParticleSystemNoiseQuality.Low;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size    = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f,    0.0f),
            new Keyframe(0.30f, 1.0f),
            new Keyframe(1f,    0.0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color   = MakeAlphaGradient(0f, 0.0f, 0.55f, 0.25f, 0.45f, 0.0f, 1.0f);

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode        = ParticleSystemRenderMode.Billboard;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows    = false;
    }

    // ── FOAM — espuma base ────────────────────────────────────────────────────
    // Muchas partículas pequeñas y blancas que burbujean en el punto de impacto.
    // Alta tasa de emisión + vida corta = zona de espuma churning perpetua.
    private void ConfigureFoam(ParticleSystem ps, Preset p, bool off)
    {
        var main = ps.main;
        main.loop            = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0f, 0.08f);
        main.startLifetime   = new ParticleSystem.MinMaxCurve(p.foamLife * 0.4f, p.foamLife);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.04f, 0.30f);
        main.startSize       = new ParticleSystem.MinMaxCurve(p.foamSize * 0.4f, p.foamSize * 1.6f);
        main.maxParticles    = Mathf.Max(1, off ? 1 : Mathf.RoundToInt(p.foamRate * p.foamLife * 2.2f));

        // Espuma: blanca pura con variación de alpha
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 1f, 1f, 0.92f),
            new Color(0.94f, 0.97f, 1f, 0.50f));

        var emission = ps.emission;
        emission.enabled      = !off;
        emission.rateOverTime = p.foamRate;
        emission.SetBursts(new ParticleSystem.Burst[0]);

        // Círculo plano en la base: la espuma se forma justo donde el agua choca
        var shape = ps.shape;
        shape.enabled         = true;
        shape.shapeType       = ParticleSystemShapeType.Circle;
        shape.radius          = 0.20f;
        shape.radiusThickness = 1f;

        var noise = ps.noise;
        noise.enabled = false;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size    = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f,    0.0f),
            new Keyframe(0.10f, 1.0f),
            new Keyframe(0.65f, 0.75f),
            new Keyframe(1f,    0.0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color   = MakeAlphaGradient(0f, 0.04f, 1.0f, 0.55f, 0.9f, 0.0f, 1.0f);

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode        = ParticleSystemRenderMode.Billboard;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows    = false;
    }

    // ── Utilidades ────────────────────────────────────────────────────────────

    private static void SetBursts(ParticleSystem.EmissionModule emission, Preset p)
    {
        if (p.burstCount > 0 && p.burstInterval > 0f)
            emission.SetBursts(new[] {
                new ParticleSystem.Burst(0f, p.burstCount, p.burstCount, int.MaxValue, p.burstInterval) });
        else
            emission.SetBursts(new ParticleSystem.Burst[0]);
    }

    // Gradiente con 3 stops de alpha: t0=a0, t1=a1, t2=a2, t3=a3 (los tiempos son fijos)
    private static ParticleSystem.MinMaxGradient MakeAlphaGradient(
        float a0, float t1, float a1, float t2, float a2, float a3, float t3)
    {
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] {
                new GradientAlphaKey(a0, 0f),
                new GradientAlphaKey(a1, t1),
                new GradientAlphaKey(a2, t2),
                new GradientAlphaKey(a3, t3)
            });
        return new ParticleSystem.MinMaxGradient(grad);
    }

    private static void PlayOrStop(ParticleSystem ps, bool off)
    {
        if (ps == null) return;
        if (off)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        else if (!ps.isPlaying)
            ps.Play();
    }

    // Gizmo: flecha azul mostrando la dirección del spray
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
        if (_psJet == null) EnsureSubSystems();
        ApplySettings();
    }
#endif
}
