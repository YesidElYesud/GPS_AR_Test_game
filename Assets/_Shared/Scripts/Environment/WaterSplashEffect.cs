using UnityEngine;

/// <summary>
/// Efecto de salpicadura de agua contra roca — dos capas de partículas:
///   Jet   : chorro principal ascendente (StretchedBillboard, elongado)
///   Crown : corona lateral que se abre y cae de vuelta al agua
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

    // ── Presets por intensidad ────────────────────────────────────────────────
    private struct Preset
    {
        public float jetRate,   crownRate;
        public float jetSpeed,  crownSpeed;
        public float jetSize,   crownSize;
        public float jetLife,   crownLife;
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
            jetRate=18,   crownRate=8,
            jetSpeed=2.2f, crownSpeed=1.0f,
            jetSize=0.06f, crownSize=0.05f,
            jetLife=0.55f, crownLife=0.50f,
            burstCount=0,  burstInterval=0f
        },

        // [2] Normal — lluvia leve, caudal moderado
        new Preset
        {
            jetRate=55,   crownRate=30,
            jetSpeed=5.0f, crownSpeed=2.5f,
            jetSize=0.09f, crownSize=0.07f,
            jetLife=0.65f, crownLife=0.55f,
            burstCount=10, burstInterval=2.0f
        },

        // [3] Heavy — tormenta, caudal violento
        new Preset
        {
            jetRate=120,  crownRate=80,
            jetSpeed=9.5f, crownSpeed=4.8f,
            jetSize=0.13f, crownSize=0.10f,
            jetLife=0.80f, crownLife=0.65f,
            burstCount=22, burstInterval=1.0f
        },
    };

    // ── Ciclo de vida ─────────────────────────────────────────────────────────
    void Awake()
    {
        EnsureSubSystems();
        DestroyLegacyChildren();
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
    }

    // Destruye hijos _Mist y _Foam si quedaron de versiones anteriores
    private void DestroyLegacyChildren()
    {
        foreach (string suffix in new[] { "_Mist", "_Foam" })
        {
            Transform t = transform.Find(gameObject.name + suffix);
            if (t != null) Destroy(t.gameObject);
        }
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

        if (!Application.isPlaying) return;
        PlayOrStop(_psJet,   off);
        PlayOrStop(_psCrown, off);
    }

    // ── JET — chorro principal ascendente ─────────────────────────────────────
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

        var shape = ps.shape;
        shape.enabled         = true;
        shape.shapeType       = ParticleSystemShapeType.Cone;
        shape.angle           = spreadAngle;
        shape.radius          = 0.05f;
        shape.radiusThickness = 1f;
        shape.rotation        = Quaternion.FromToRotation(Vector3.up, dir).eulerAngles;

        var noise = ps.noise;
        noise.enabled     = !off;
        noise.strength    = new ParticleSystem.MinMaxCurve(0.3f, 0.65f);
        noise.frequency   = 1.8f;
        noise.scrollSpeed = 0.5f;
        noise.quality     = ParticleSystemNoiseQuality.Medium;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size    = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f,    0.0f, 0f, 8f),
            new Keyframe(0.10f, 1.0f),
            new Keyframe(0.70f, 0.5f),
            new Keyframe(1f,    0.0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color   = MakeAlphaGradient(0f, 0.08f, 0.9f,  0.55f, 0.9f,  0.0f, 1.0f);

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode        = ParticleSystemRenderMode.Stretch;
        rend.velocityScale     = 0.14f;
        rend.lengthScale       = 1.3f;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows    = false;
    }

    // ── CROWN — corona lateral ────────────────────────────────────────────────
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
        if (!off && p.burstCount > 0 && p.burstInterval > 0f)
            emission.SetBursts(new[] {
                new ParticleSystem.Burst(0f, (short)(p.burstCount / 2), (short)(p.burstCount / 2),
                                         int.MaxValue, p.burstInterval) });
        else
            emission.SetBursts(new ParticleSystem.Burst[0]);

        var shape = ps.shape;
        shape.enabled         = true;
        shape.shapeType       = ParticleSystemShapeType.Cone;
        shape.angle           = 65f;
        shape.radius          = 0.12f;
        shape.radiusThickness = 0f;
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
        rend.renderMode        = ParticleSystemRenderMode.Stretch;
        rend.velocityScale     = 0.08f;
        rend.lengthScale       = 1.1f;
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
        // Limpiar siempre las partículas existentes para que el cambio de intensidad
        // sea inmediato. Sin esto, las partículas de intensidad alta persisten hasta
        // que expira su lifetime aunque la tasa de emisión ya sea la nueva (baja).
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (!off) ps.Play();
    }

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
