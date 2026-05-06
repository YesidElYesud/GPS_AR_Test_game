using System.Collections;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class RainParticleController : MonoBehaviour
{
    public enum RainIntensity { None, Light, Medium, Heavy }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Intensidad")]
    public RainIntensity intensity = RainIntensity.Light;

    [Range(0f, 3f)]
    public float transitionDuration = 0.8f;

    [Header("Área")]
    [Range(10f, 60f)] public float areaSize  = 22f;
    [Range(8f,  40f)] public float height    = 22f;
    [Range(5f,  35f)] public float fallSpeed = 16f;

    [Header("Viento")]
    [Range(0f, 360f)] public float windAngle = 215f;
    [Range(0f, 5f)]   public float windSpeed = 1.5f;

    [Header("Visual")]
    [Tooltip("Textura de gota (B&W con alpha). Null = streak procedural.")]
    public Texture2D dropTexture;
    [Tooltip("Material completo. Asignar solo si quieres control total; si null, se genera automáticamente.")]
    public Material rainMaterial;

    public bool followCamera = true;

    // ── Presets: (rate, dropWidth, lengthScale, velocityScale) ──────────────
    private static readonly (float rate, float width, float len, float vel)[] k_Presets =
    {
        (   0f, 0.000f, 0.0f, 0.000f),   // None
        ( 180f, 0.020f, 1.3f, 0.006f),   // Light   (era 90)
        ( 450f, 0.030f, 1.4f, 0.007f),   // Medium  (era 240)
        ( 900f, 0.040f, 1.5f, 0.008f),   // Heavy   (era 480)
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
        int maxP = Mathf.CeilToInt(k_Presets[(int)RainIntensity.Heavy].rate * _cachedLifetime * 1.2f);

        var main             = _ps.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.startSpeed      = 0f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(_cachedLifetime * 0.82f, _cachedLifetime * 1.18f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.020f, 0.040f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.72f, 0.86f, 1.00f, 0.60f),
                                   new Color(0.90f, 0.96f, 1.00f, 0.95f));
        main.maxParticles    = maxP;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.stopAction      = ParticleSystemStopAction.Disable;

        var shape       = _ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = new Vector3(areaSize, 0.2f, areaSize);

        float rad = windAngle * Mathf.Deg2Rad;
        float wx  = Mathf.Sin(rad) * windSpeed;
        float wz  = Mathf.Cos(rad) * windSpeed;

        var vol     = _ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space   = ParticleSystemSimulationSpace.World;
        vol.x       = new ParticleSystem.MinMaxCurve(wx * 0.88f, wx * 1.12f);
        vol.y       = new ParticleSystem.MinMaxCurve(-fallSpeed * 1.08f, -fallSpeed * 0.92f);
        vol.z       = new ParticleSystem.MinMaxCurve(wz * 0.88f, wz * 1.12f);

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

        var emission          = _ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;

        _renderer.renderMode        = ParticleSystemRenderMode.Stretch;
        _renderer.velocityScale     = k_Presets[(int)RainIntensity.Medium].vel;
        _renderer.lengthScale       = k_Presets[(int)RainIntensity.Medium].len;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows    = false;
        _renderer.sortingFudge      = -1f;

        _renderer.material = (rainMaterial != null) ? rainMaterial : BuildRainMaterial();
    }

    // ── Material ──────────────────────────────────────────────────────────────
    private Material BuildRainMaterial()
    {
        var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Particles/Alpha Blended");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");

        var mat         = new Material(shader) { name = "RainDrop_Auto" };
        mat.mainTexture = (dropTexture != null) ? dropTexture : BuildStreakTexture();
        mat.color       = Color.white;
        return mat;
    }

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
            alpha        = Mathf.Clamp01(alpha) * 0.85f;
            float bright = 0.80f + 0.16f * Mathf.Sin(t * Mathf.PI);
            var   c      = new Color(bright * 0.82f, bright * 0.92f, bright * 1.00f, alpha);
            for (int x = 0; x < W; x++) tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    // ── Splash system ─────────────────────────────────────────────────────────
    private void EnsureSplashSystem()
    {
        _splash = FindObjectOfType<RainGroundSplash>();
        if (_splash != null) return;

        var go  = new GameObject("RainGroundSplash");
        _splash = go.AddComponent<RainGroundSplash>();
        // El splash tiene su propio splashRadius (5m por defecto).
        // Si el usuario asignó una textura de gota, se pasa al splash para el spray.
        if (dropTexture != null)
            _splash.ApplyDropTexture(dropTexture);
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
        main.maxParticles = Mathf.CeilToInt(rate * _cachedLifetime * 1.2f);

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
            main.maxParticles = Mathf.CeilToInt(targetRate * _cachedLifetime * 1.2f);
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
        Vector3 p          = _cam.position;
        transform.position = new Vector3(p.x, p.y + height, p.z);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (_ps == null) _ps = GetComponent<ParticleSystem>();
        if (_ps != null) SetIntensity(intensity);
    }
}
