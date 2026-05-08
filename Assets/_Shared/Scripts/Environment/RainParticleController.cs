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
    [Range(10f, 80f)] public float areaSize = 38f;
    [Range(8f,  40f)] public float height   = 22f;
    [Tooltip("Metros que el área de spawn se desplaza hacia adelante de la cámara. Evita que la lluvia quede atrás al caminar.")]
    [Range(0f, 12f)]  public float forwardBias = 5f;

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
    // Valores de stretch (len/width) inspirados en el VFX tutorial: streaks muy finos y largos (ratio ~1:10)
    private static readonly (float rate, float width, float len, float vel, float spd, float windMult, float areaMult)[] k_Presets =
    {
        (    0f, 0.000f,  0.0f, 0.000f,  0f, 0.0f, 1.00f),   // None
        (  420f, 0.010f,  5.0f, 0.010f, 24f, 1.0f, 1.00f),   // Light
        ( 1000f, 0.015f,  7.0f, 0.016f, 27f, 1.6f, 0.92f),   // Medium
        ( 2800f, 0.022f, 10.0f, 0.022f, 30f, 2.5f, 0.75f),   // Heavy — torrencial
    };

    // Color blanco puro con variación mínima de opacidad, igual que el VFX tutorial (gotas blancas, no azuladas)
    private static readonly (Color min, Color max)[] k_Colors =
    {
        (Color.clear,                                    Color.clear),
        (new Color(0.92f, 0.96f, 1.00f, 0.45f),  new Color(1.00f, 1.00f, 1.00f, 0.80f)),  // Light
        (new Color(0.90f, 0.95f, 1.00f, 0.60f),  new Color(1.00f, 1.00f, 1.00f, 0.92f)),  // Medium
        (new Color(0.88f, 0.93f, 1.00f, 0.75f),  new Color(1.00f, 1.00f, 1.00f, 1.00f)),  // Heavy
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
        // Alpha: fade-in rápido (0→1 en 10% del lifetime), hold, fade-out al final — igual que el VFX tutorial
        grad.SetKeys(
            new[] {
                new GradientColorKey(Color.white, 0.00f),
                new GradientColorKey(Color.white, 1.00f),
            },
            new[] {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(1.00f, 0.10f),
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
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");

        var mat         = new Material(shader) { name = "RainDrop_Auto" };
        mat.mainTexture = (dropTexture != null) ? dropTexture : BuildStreakTexture();
        if (mat.HasProperty("_BaseColor"))  mat.SetColor("_BaseColor",  Color.white);
        if (mat.HasProperty("_Color"))      mat.SetColor("_Color",      Color.white);
        if (mat.HasProperty("_Surface"))    mat.SetFloat("_Surface",    1f); // Transparent en URP
        if (mat.HasProperty("_Blend"))      mat.SetFloat("_Blend",      0f); // Alpha blend
        mat.renderQueue = 3000;
        return mat;
    }

    private Texture2D BuildStreakTexture()
    {
        // Textura inspirada en Gota.png del tutorial: 8×64 blanca, fade suave en tips (10% arriba/abajo), perfil gaussiano en X
        const int W = 8, H = 64;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            name       = "RainStreak_Auto",
        };
        float halfW = (W - 1) * 0.5f;
        for (int y = 0; y < H; y++)
        {
            float t = y / (float)(H - 1);
            // Alpha: fade-in en 10%, hold, fade-out en último 20% — igual que el VFX tutorial
            float alphaY = t < 0.10f ? t / 0.10f
                         : t > 0.80f ? 1f - (t - 0.80f) / 0.20f
                         : 1f;
            alphaY = Mathf.SmoothStep(0f, 1f, alphaY);

            for (int x = 0; x < W; x++)
            {
                // Perfil gaussiano en X: más brillante en el centro, casi invisible en los bordes
                float dx     = (x - halfW) / halfW;
                float alphaX = Mathf.Exp(-dx * dx * 4f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alphaY * alphaX));
            }
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
        Vector3 p   = _cam.position;
        // Desplaza el área hacia adelante para que las gotas caigan frente al jugador, no detrás
        Vector3 fwd = Vector3.ProjectOnPlane(_cam.forward, Vector3.up);
        if (fwd.sqrMagnitude > 0.001f) fwd.Normalize();
        Vector3 bias = fwd * forwardBias;
        transform.position = new Vector3(p.x + bias.x, p.y + height, p.z + bias.z);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (_ps == null) _ps = GetComponent<ParticleSystem>();
        if (_ps != null) SetIntensity(intensity);
    }
}
