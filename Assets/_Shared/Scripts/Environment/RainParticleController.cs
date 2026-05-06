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
    [Range(10f, 60f)] public float areaSize = 22f;
    [Range(8f,  40f)] public float height   = 22f;

    [Header("Viento")]
    [Range(0f, 360f)] public float windAngle = 215f;
    [Range(0f, 5f)]   public float windSpeed = 1.5f;

    [Header("Visual")]
    [Tooltip("Textura de gota (B&W con alpha). Null = streak procedural.")]
    public Texture2D dropTexture;
    [Tooltip("Material completo. Asignar solo si quieres control total; si null, se genera automáticamente.")]
    public Material rainMaterial;

    public bool followCamera = true;

    // Presets: (rate, dropWidth, lengthScale, velocityScale, fallSpeed, windMultiplier, areaScale)
    // areaScale: fracción de areaSize — Heavy usa área más pequeña para mayor densidad visual frente a cámara
    private static readonly (float rate, float width, float len, float vel, float spd, float windMult, float areaMult)[] k_Presets =
    {
        (    0f, 0.000f, 0.0f, 0.000f,  0f, 0.0f, 1.00f),   // None
        (  200f, 0.018f, 1.6f, 0.008f, 18f, 1.0f, 1.00f),   // Light
        (  550f, 0.030f, 2.2f, 0.013f, 26f, 1.6f, 0.85f),   // Medium
        ( 1600f, 0.062f, 3.8f, 0.024f, 36f, 2.5f, 0.60f),   // Heavy — torrencial
    };

    // Color de gota por intensidad (más oscuro/opaco cuanto más fuerte)
    private static readonly (Color min, Color max)[] k_Colors =
    {
        (Color.clear,                                    Color.clear),
        (new Color(0.72f, 0.86f, 1.00f, 0.50f),  new Color(0.90f, 0.96f, 1.00f, 0.85f)),  // Light
        (new Color(0.65f, 0.78f, 0.95f, 0.65f),  new Color(0.85f, 0.92f, 1.00f, 0.95f)),  // Medium
        (new Color(0.50f, 0.65f, 0.82f, 0.78f),  new Color(0.70f, 0.82f, 0.95f, 1.00f)),  // Heavy
    };

    // ── Internos ──────────────────────────────────────────────────────────────
    private ParticleSystem         _ps;
    private ParticleSystemRenderer _renderer;
    private Transform              _cam;
    private float                  _currentRate;
    private float                  _currentSpeed;
    private float                  _currentArea;
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
        // maxParticles calculado desde Heavy (peor caso)
        var (heavyRate, _, _, _, heavySpd, _, _) = k_Presets[(int)RainIntensity.Heavy];
        float heavyLifetime = (height / heavySpd) + 0.4f;
        int maxP = Mathf.CeilToInt(heavyRate * heavyLifetime * 1.2f);

        // Baseline desde Light
        var (_, _, lightLen, lightVel, lightSpd, _, lightAreaMult) = k_Presets[(int)RainIntensity.Light];
        float baseLifetime = (height / lightSpd) + 0.4f;
        float lightArea    = lightAreaMult * areaSize;

        var main             = _ps.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.startSpeed      = 0f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(baseLifetime * 0.82f, baseLifetime * 1.18f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.018f, 0.030f);
        main.startColor      = new ParticleSystem.MinMaxGradient(k_Colors[(int)RainIntensity.Light].min,
                                                                   k_Colors[(int)RainIntensity.Light].max);
        main.maxParticles    = maxP;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.stopAction      = ParticleSystemStopAction.Disable;

        var shape       = _ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = new Vector3(lightArea, 0.2f, lightArea);
        _currentArea    = lightArea;

        ApplyVelocityOverLifetime(lightSpd, 1.0f);

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
        _renderer.velocityScale     = lightVel;
        _renderer.lengthScale       = lightLen;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows    = false;
        _renderer.sortingFudge      = -1f;

        _renderer.material = (rainMaterial != null) ? rainMaterial : BuildRainMaterial();
    }

    private void ApplyVelocityOverLifetime(float spd, float windMult)
    {
        float rad = windAngle * Mathf.Deg2Rad;
        float wx  = Mathf.Sin(rad) * windSpeed * windMult;
        float wz  = Mathf.Cos(rad) * windSpeed * windMult;

        var vol     = _ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space   = ParticleSystemSimulationSpace.World;
        vol.x       = new ParticleSystem.MinMaxCurve(wx * 0.88f, wx * 1.12f);
        vol.y       = new ParticleSystem.MinMaxCurve(-spd * 1.08f, -spd * 0.92f);
        vol.z       = new ParticleSystem.MinMaxCurve(wz * 0.88f, wz * 1.12f);
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
        var (rate, width, len, vel, spd, windMult, areaMult) = k_Presets[(int)preset];
        var emission = _ps.emission;

        if (preset == RainIntensity.None)
        {
            emission.rateOverTime = 0f;
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _currentRate  = 0f;
            _currentSpeed = 0f;
            _currentArea  = 0f;
            return;
        }

        float lifetime = (height / spd) + 0.4f;
        float area     = areaMult * areaSize;

        var main           = _ps.main;
        main.startSize     = new ParticleSystem.MinMaxCurve(width * 0.65f, width * 1.35f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.82f, lifetime * 1.18f);
        main.startColor    = new ParticleSystem.MinMaxGradient(k_Colors[(int)preset].min, k_Colors[(int)preset].max);
        main.maxParticles  = Mathf.CeilToInt(rate * lifetime * 1.2f);

        _renderer.lengthScale   = len;
        _renderer.velocityScale = vel;

        ApplyVelocityOverLifetime(spd, windMult);

        var sh    = _ps.shape;
        sh.scale  = new Vector3(area, 0.2f, area);

        emission.rateOverTime = rate;
        _currentRate          = rate;
        _currentSpeed         = spd;
        _currentArea          = area;

        if (!_ps.isPlaying) _ps.Play();
    }

    private IEnumerator IntensityTransition(RainIntensity target)
    {
        var (targetRate, width, len, vel, targetSpd, windMult, areaMult) = k_Presets[(int)target];
        float startRate  = _currentRate;
        float startSpeed = _currentSpeed;
        bool  lerpSpeed  = startSpeed > 0f && targetSpd > 0f;
        float elapsed    = 0f;

        if (target != RainIntensity.None)
        {
            float lifetime   = (height / targetSpd) + 0.4f;
            float targetArea = areaMult * areaSize;

            var main           = _ps.main;
            main.startSize     = new ParticleSystem.MinMaxCurve(width * 0.65f, width * 1.35f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.82f, lifetime * 1.18f);
            main.startColor    = new ParticleSystem.MinMaxGradient(k_Colors[(int)target].min, k_Colors[(int)target].max);
            main.maxParticles  = Mathf.CeilToInt(targetRate * lifetime * 1.2f);
            _renderer.lengthScale   = len;
            _renderer.velocityScale = vel;

            // El área nueva se aplica desde el primer frame: las gotas viejas (área anterior)
            // mueren solas en su lifetime; las nuevas ya nacen en el área concentrada.
            var sh   = _ps.shape;
            sh.scale = new Vector3(targetArea, 0.2f, targetArea);
            _currentArea = targetArea;

            if (!lerpSpeed) ApplyVelocityOverLifetime(targetSpd, windMult);
            if (!_ps.isPlaying) _ps.Play();
        }

        var emission = _ps.emission;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / transitionDuration));

            emission.rateOverTime = Mathf.Lerp(startRate, targetRate, t);
            _currentRate          = emission.rateOverTime.constant;

            if (lerpSpeed)
                ApplyVelocityOverLifetime(Mathf.Lerp(startSpeed, targetSpd, t), windMult);

            yield return null;
        }

        emission.rateOverTime = targetRate;
        _currentRate          = targetRate;
        _currentSpeed         = targetSpd;

        if (target == RainIntensity.None)
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        else if (lerpSpeed)
            ApplyVelocityOverLifetime(targetSpd, windMult);

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
