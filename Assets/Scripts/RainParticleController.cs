using System.Collections;
using UnityEngine;

/// <summary>
/// RainParticleController — Sistema de lluvia para WebGL.
///
/// Correcciones respecto a versiones anteriores:
///   · velocityScale reducido drásticamente (antes 0.05 → ahora 0.007):
///     las gotas eran de ~85cm de largo; ahora son ~15cm (realistas).
///   · RainGroundSplash se crea como objeto RAÍZ (no hijo):
///     evita que el movimiento de Rain arrastre la posición del splash.
///   · Start() propaga la intensidad inicial al splash system.
///
/// Setup:
///   1. Crear GameObject "Rain" en escena.
///   2. Adjuntar este script.
///   3. Presionar Play → RainGroundSplash se crea automáticamente como
///      objeto raíz en la escena.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class RainParticleController : MonoBehaviour
{
    public enum RainIntensity { None, Light, Medium, Heavy }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Intensidad")]
    public RainIntensity intensity = RainIntensity.Light;

    [Tooltip("Duración del fundido al cambiar intensidad (seg).")]
    [Range(0f, 3f)]
    public float transitionDuration = 0.8f;

    [Header("Área")]
    [Range(10f, 60f)] public float areaSize   = 30f;
    [Range(8f,  40f)] public float height     = 22f;
    [Range(5f,  35f)] public float fallSpeed  = 16f;

    [Header("Viento")]
    [Tooltip("Dirección del viento en grados (0=Norte, 90=Este).")]
    [Range(0f, 360f)] public float windAngle = 215f;
    [Tooltip("Velocidad lateral. 0=vertical pura. 1.5=inclinación sutil.")]
    [Range(0f, 5f)]   public float windSpeed = 1.5f;

    [Header("Visual")]
    [Tooltip("Null = material generado proceduralmente.")]
    public Material rainMaterial;

    public bool followCamera = true;

    // ── Presets: (rate, dropWidth, lengthScale, velocityScale) ───────────────
    // Longitud resultante = dropWidth × lengthScale + fallSpeed × velocityScale
    //   Light : 0.020 × 1.3 + 16 × 0.006 = 0.026 + 0.096 = ~12cm  ✓
    //   Medium: 0.030 × 1.4 + 16 × 0.007 = 0.042 + 0.112 = ~15cm  ✓
    //   Heavy : 0.040 × 1.5 + 16 × 0.008 = 0.060 + 0.128 = ~19cm  ✓
    private static readonly (float rate, float width, float len, float vel)[] k_Presets =
    {
        (  0f, 0.000f, 0.0f, 0.000f),   // None
        ( 90f, 0.020f, 1.3f, 0.006f),   // Light
        (240f, 0.030f, 1.4f, 0.007f),   // Medium
        (480f, 0.040f, 1.5f, 0.008f),   // Heavy
    };

    // ── Internos ──────────────────────────────────────────────────────────────
    private ParticleSystem         _ps;
    private ParticleSystemRenderer _renderer;
    private Transform              _cam;
    private float                  _cachedLifetime;
    private float                  _currentRate;
    private Coroutine              _transitionRoutine;
    private RainGroundSplash       _splash;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        _ps               = GetComponent<ParticleSystem>();
        _renderer         = GetComponent<ParticleSystemRenderer>();
        transform.rotation = Quaternion.identity;

        BuildParticleSystem();
        EnsureSplashSystem();
    }

    private void Start()
    {
        var cam = Camera.main;
        if (cam != null) _cam = cam.transform;

        SnapToCamera();

        // Aplicar intensidad inicial + sincronizar splash
        ApplyIntensityImmediate(intensity);
        _splash?.SetIntensity(intensity);
    }

    private void LateUpdate()
    {
        if (followCamera && _cam != null)
            SnapToCamera();
    }

    // ── API pública ────────────────────────────────────────────────────────────
    public void SetIntensity(RainIntensity newIntensity)
    {
        intensity = newIntensity;

        if (_transitionRoutine != null) StopCoroutine(_transitionRoutine);

        if (transitionDuration > 0f)
            _transitionRoutine = StartCoroutine(IntensityTransition(newIntensity));
        else
            ApplyIntensityImmediate(newIntensity);

        _splash?.SetIntensity(newIntensity);
    }

    public void SetDensityNormalized(float t)
        => SetIntensity((RainIntensity)Mathf.RoundToInt(Mathf.Clamp01(t) * 3f));

    // ── Construir PS ──────────────────────────────────────────────────────────
    private void BuildParticleSystem()
    {
        _cachedLifetime = (height / fallSpeed) + 0.4f;
        int maxP = Mathf.CeilToInt(k_Presets[(int)RainIntensity.Heavy].rate * _cachedLifetime * 1.15f);

        // Main
        var main             = _ps.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.startSpeed      = 0f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(_cachedLifetime * 0.82f, _cachedLifetime * 1.18f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.020f, 0.040f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.72f, 0.86f, 1.00f, 0.30f),
                                   new Color(0.90f, 0.96f, 1.00f, 0.65f));
        main.maxParticles    = maxP;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.stopAction      = ParticleSystemStopAction.Disable;

        // Shape: caja plana (emisor horizontal)
        var shape       = _ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = new Vector3(areaSize, 0.2f, areaSize);

        // VelocityOverLifetime: caída + viento
        float rad = windAngle * Mathf.Deg2Rad;
        float wx  = Mathf.Sin(rad) * windSpeed;
        float wz  = Mathf.Cos(rad) * windSpeed;

        var vol     = _ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space   = ParticleSystemSimulationSpace.World;
        vol.x       = new ParticleSystem.MinMaxCurve(wx * 0.88f, wx * 1.12f);
        vol.y       = new ParticleSystem.MinMaxCurve(-fallSpeed * 1.08f, -fallSpeed * 0.92f);
        vol.z       = new ParticleSystem.MinMaxCurve(wz * 0.88f, wz * 1.12f);

        // ColorOverLifetime: fade suave en el último 20% del lifetime
        var colLife     = _ps.colorOverLifetime;
        colLife.enabled = true;
        var grad        = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(0.78f, 0.89f, 1f), 0.00f),
                new GradientColorKey(new Color(0.92f, 0.97f, 1f), 0.50f),
                new GradientColorKey(Color.white,                  1.00f),
            },
            new[] {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(1.00f, 0.80f),
                new GradientAlphaKey(0.00f, 1.00f),
            }
        );
        colLife.color = new ParticleSystem.MinMaxGradient(grad);

        // Emission
        var emission          = _ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;

        // Renderer: Stretch alineado con velocidad
        _renderer.renderMode        = ParticleSystemRenderMode.Stretch;
        _renderer.velocityScale     = k_Presets[(int)RainIntensity.Medium].vel;
        _renderer.lengthScale       = k_Presets[(int)RainIntensity.Medium].len;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows    = false;
        _renderer.sortingFudge      = -1f;

        _renderer.material = (rainMaterial != null) ? rainMaterial : BuildRainMaterial();
    }

    // ── Material procedural ────────────────────────────────────────────────────
    private Material BuildRainMaterial()
    {
        var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null || !shader.isSupported)
            shader = Shader.Find("Particles/Alpha Blended");
        if (shader == null || !shader.isSupported)
            shader = Shader.Find("Sprites/Default");

        var mat         = new Material(shader) { name = "RainDrop_Auto" };
        mat.mainTexture = BuildStreakTexture();
        mat.color       = Color.white;
        return mat;
    }

    /// <summary>
    /// Textura 4×32 px: gradiente que da forma de streak de lluvia.
    /// El eje Y se alinea con la dirección de velocidad en Stretch mode.
    /// </summary>
    private Texture2D BuildStreakTexture()
    {
        const int W = 4, H = 32;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            name       = "RainStreak_Auto",
        };
        for (int y = 0; y < H; y++)
        {
            float t      = y / (float)(H - 1);
            float alpha  = Mathf.SmoothStep(0f, 1f, t * 3.5f) * Mathf.SmoothStep(0f, 1f, 1f - t * 0.25f);
            alpha        = Mathf.Clamp01(alpha) * 0.80f;
            float bright = 0.80f + 0.16f * Mathf.Sin(t * Mathf.PI);
            var   c      = new Color(bright * 0.82f, bright * 0.92f, bright * 1.00f, alpha);
            for (int x = 0; x < W; x++) tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    // ── Splash system (objeto RAÍZ, no hijo) ──────────────────────────────────
    /// <summary>
    /// Crea RainGroundSplash como objeto raíz de la escena para que su
    /// posicionamiento en suelo sea independiente del movimiento de Rain.
    /// </summary>
    private void EnsureSplashSystem()
    {
        _splash = FindObjectOfType<RainGroundSplash>();
        if (_splash != null) return;

        var go  = new GameObject("RainGroundSplash");
        // Sin padre: objeto raíz independiente
        _splash = go.AddComponent<RainGroundSplash>();
        _splash.areaRadius = areaSize * 0.5f;   // sincronizar radio con el emisor de lluvia
    }

    // ── Intensidad ────────────────────────────────────────────────────────────
    private void ApplyIntensityImmediate(RainIntensity preset)
    {
        var (rate, width, len, vel) = k_Presets[(int)preset];
        var emission = _ps.emission;

        if (preset == RainIntensity.None)
        {
            emission.rateOverTime = 0f;
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _currentRate = 0f;
            return;
        }

        var main          = _ps.main;
        main.startSize    = new ParticleSystem.MinMaxCurve(width * 0.65f, width * 1.35f);
        main.maxParticles = Mathf.CeilToInt(rate * _cachedLifetime * 1.15f);

        _renderer.lengthScale   = len;
        _renderer.velocityScale = vel;
        emission.rateOverTime   = rate;
        _currentRate            = rate;

        if (!_ps.isPlaying) _ps.Play();
    }

    private IEnumerator IntensityTransition(RainIntensity target)
    {
        var (targetRate, width, len, vel) = k_Presets[(int)target];
        float startRate = _currentRate;
        float elapsed   = 0f;

        if (target != RainIntensity.None)
        {
            var main          = _ps.main;
            main.startSize    = new ParticleSystem.MinMaxCurve(width * 0.65f, width * 1.35f);
            main.maxParticles = Mathf.CeilToInt(targetRate * _cachedLifetime * 1.15f);
            _renderer.lengthScale   = len;
            _renderer.velocityScale = vel;
            if (!_ps.isPlaying) _ps.Play();
        }

        var emission = _ps.emission;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / transitionDuration));
            emission.rateOverTime = Mathf.Lerp(startRate, targetRate, t);
            _currentRate          = emission.rateOverTime.constant;
            yield return null;
        }

        emission.rateOverTime = targetRate;
        _currentRate          = targetRate;

        if (target == RainIntensity.None)
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        _transitionRoutine = null;
    }

    private void SnapToCamera()
    {
        Vector3 p      = _cam.position;
        transform.position = new Vector3(p.x, p.y + height, p.z);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (_ps == null) _ps = GetComponent<ParticleSystem>();
        if (_ps != null) SetIntensity(intensity);
    }
}
