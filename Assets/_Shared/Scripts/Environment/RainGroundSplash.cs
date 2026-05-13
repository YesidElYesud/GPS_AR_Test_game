using UnityEngine;

/// <summary>
/// RainGroundSplash — Impactos de lluvia sobre el suelo.
///
/// Tres capas de efecto:
///   · Ripple  — anillo expansivo plano (HorizontalBillboard).
///   · Spray   — gotas medianas que salen hacia arriba y caen.
///   · Sparks  — microchispas rápidas y brillantes (efecto de impacto).
///
/// Creado automáticamente por RainParticleController como objeto raíz.
/// Radio de splash (splashRadius = 5m) independiente del área de lluvia:
/// concentra los impactos cerca del jugador para máxima visibilidad.
/// </summary>
public class RainGroundSplash : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Posición en suelo")]
    [Tooltip("Offset Y desde la cámara al suelo (CharacterController height=1.7 → -1.65).")]
    [Range(-4f, 0f)]
    public float groundOffset = -1.65f;

    [Tooltip("Radio del área de impactos centrada en el jugador. Más pequeño = más denso y visible.")]
    [Range(2f, 20f)]
    public float splashRadius = 5f;

    [Header("Física de suelo")]
    public bool useRaycast = true;
    public LayerMask groundLayerMask = ~0;

    [Header("Visual")]
    [Tooltip("Textura de gota (B&W con alpha) para las partículas de spray. Null = punto procedural.")]
    public Texture2D dropTexture;

    [Tooltip("Tamaño máximo del anillo ripple (m) en Heavy.")]
    [Range(0.3f, 2.0f)]
    public float rippleMaxSizeHeavy = 0.80f;

    [Tooltip("Velocidad de subida del spray (m/s).")]
    [Range(1f, 8f)]
    public float sprayUpForce = 4.0f;

    // ── Presets: (rippleRate, rippleSizeMax, sprayRate, sparksRate) ───────────
    private static readonly (float rRate, float rMax, float sRate, float spkRate)[] k_Presets =
    {
        (  0f, 0.00f,   0f,   0f),   // None
        ( 40f, 0.40f,  80f, 120f),   // Light
        ( 80f, 0.60f, 200f, 300f),   // Medium
        (150f, 1.00f, 400f, 600f),   // Heavy
    };

    // ── Internos ──────────────────────────────────────────────────────────────
    private ParticleSystem         _ripplePs;
    private ParticleSystemRenderer _rippleRenderer;
    private ParticleSystem         _sprayPs;
    private ParticleSystemRenderer _sprayRenderer;
    private ParticleSystem         _sparksPs;
    private ParticleSystemRenderer _sparksRenderer;

    private Transform _cam;
    private float     _groundY;
    private float     _nextRaycastTime;
    private const float k_RaycastInterval = 0.30f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        BuildRippleSystem();
        BuildSpraySystem();
        BuildSparksSystem();
    }

    private void Start()
    {
        var cam = Camera.main;
        if (cam != null) _cam = cam.transform;

        RefreshGroundY(force: true);
        SnapToGround();
    }

    private void LateUpdate()
    {
        if (_cam == null)
        {
            var cam = Camera.main;
            if (cam != null) { _cam = cam.transform; RefreshGroundY(force: true); }
            else return;
        }
        RefreshGroundY(force: false);
        SnapToGround();
    }

    // ── API pública ────────────────────────────────────────────────────────────
    public void SetIntensity(RainParticleController.RainIntensity preset)
    {
        var (rRate, rMax, sRate, spkRate) = k_Presets[(int)preset];

        if (preset == RainParticleController.RainIntensity.None)
        {
            _ripplePs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _sprayPs.Stop(true,  ParticleSystemStopBehavior.StopEmittingAndClear);
            _sparksPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        float scaledRMax = rMax * (rippleMaxSizeHeavy / k_Presets[(int)RainParticleController.RainIntensity.Heavy].rMax);
        SetRipplePreset(rRate, scaledRMax);
        SetSprayPreset(sRate);
        SetSparksPreset(spkRate);

        // stopAction=None garantiza que los GOs hijo no se auto-deshabiliten.
        if (!_ripplePs.gameObject.activeSelf) _ripplePs.gameObject.SetActive(true);
        if (!_sprayPs.gameObject.activeSelf)  _sprayPs.gameObject.SetActive(true);
        if (!_sparksPs.gameObject.activeSelf) _sparksPs.gameObject.SetActive(true);

        // Limpiar partículas del preset anterior para que el cambio sea inmediato
        _ripplePs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _sprayPs.Stop(true,  ParticleSystemStopBehavior.StopEmittingAndClear);
        _sparksPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _ripplePs.Play();
        _sprayPs.Play();
        _sparksPs.Play();
    }

    /// <summary>Llamado por RainParticleController si el usuario asigna una textura de gota.</summary>
    public void ApplyDropTexture(Texture2D tex)
    {
        dropTexture = tex;
        if (_sprayRenderer != null)
            _sprayRenderer.material = BuildSprayMaterial();
    }

    // ── Construcción: Ripple ──────────────────────────────────────────────────
    private void BuildRippleSystem()
    {
        var go = new GameObject("Ripple");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        _ripplePs       = go.AddComponent<ParticleSystem>();
        _rippleRenderer = go.GetComponent<ParticleSystemRenderer>();

        var main = _ripplePs.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.startSpeed      = 0f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.82f, 0.92f, 1.00f, 0.80f),
                                   new Color(1.00f, 1.00f, 1.00f, 1.00f));
        main.maxParticles    = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.stopAction      = ParticleSystemStopAction.None;

        var shape = _ripplePs.shape;
        shape.enabled         = true;
        shape.shapeType       = ParticleSystemShapeType.Circle;
        shape.radius          = splashRadius;
        shape.radiusThickness = 1f;

        var sol  = _ripplePs.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0.00f, 0.06f, 0f, 4f),
            new Keyframe(0.30f, 0.60f, 2f, 1f),
            new Keyframe(1.00f, 1.00f, 0.5f, 0f)
        );
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var col = _ripplePs.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.85f, 0.94f, 1f), 0.00f),
                new GradientColorKey(new Color(0.93f, 0.97f, 1f), 0.40f),
                new GradientColorKey(Color.white,                  1.00f),
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.95f, 0.00f),
                new GradientAlphaKey(0.55f, 0.50f),
                new GradientAlphaKey(0.00f, 1.00f),
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var emission = _ripplePs.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;

        _rippleRenderer.renderMode        = ParticleSystemRenderMode.HorizontalBillboard;
        _rippleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _rippleRenderer.receiveShadows    = false;
        _rippleRenderer.sortingFudge      = 0f;
        _rippleRenderer.material          = BuildRippleMaterial();
    }

    // ── Construcción: Spray ───────────────────────────────────────────────────
    private void BuildSpraySystem()
    {
        var go = new GameObject("Spray");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        _sprayPs       = go.AddComponent<ParticleSystem>();
        _sprayRenderer = go.GetComponent<ParticleSystemRenderer>();

        var main = _sprayPs.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.startSpeed      = new ParticleSystem.MinMaxCurve(sprayUpForce * 0.3f, sprayUpForce * 1.0f);
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.80f, 0.91f, 1.00f, 0.70f),
                                   new Color(0.95f, 0.98f, 1.00f, 0.95f));
        main.maxParticles    = 200;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 2.8f;
        main.stopAction      = ParticleSystemStopAction.None;

        var shape = _sprayPs.shape;
        shape.enabled               = true;
        shape.shapeType             = ParticleSystemShapeType.Circle;
        shape.radius                = splashRadius;
        shape.radiusThickness       = 1f;
        shape.randomDirectionAmount = 0.45f;

        var col = _sprayPs.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.90f, 0f), new GradientAlphaKey(0.00f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var emission = _sprayPs.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;

        _sprayRenderer.renderMode        = ParticleSystemRenderMode.Billboard;
        _sprayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _sprayRenderer.receiveShadows    = false;
        _sprayRenderer.sortingFudge      = 1f;
        _sprayRenderer.material          = BuildSprayMaterial();
    }

    // ── Construcción: Sparks (microchispas de impacto) ────────────────────────
    private void BuildSparksSystem()
    {
        var go = new GameObject("Sparks");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        // -90° en X: el Circle emite partículas hacia arriba (world Y)
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        _sparksPs       = go.AddComponent<ParticleSystem>();
        _sparksRenderer = go.GetComponent<ParticleSystemRenderer>();

        var main = _sparksPs.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.startSpeed      = new ParticleSystem.MinMaxCurve(4f, 11f);
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.006f, 0.020f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.85f, 0.95f, 1.00f, 0.90f),
                                   new Color(1.00f, 1.00f, 1.00f, 1.00f));
        main.maxParticles    = 250;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 4.5f;
        main.stopAction      = ParticleSystemStopAction.None;

        // Radio ligeramente menor que spray para concentrar las chispas
        float sparksRadius = Mathf.Min(splashRadius * 0.7f, 4f);
        var shape = _sparksPs.shape;
        shape.enabled               = true;
        shape.shapeType             = ParticleSystemShapeType.Circle;
        shape.radius                = sparksRadius;
        shape.radiusThickness       = 1f;
        // Alta aleatoriedad: las chispas salen en todas direcciones, no solo arriba
        shape.randomDirectionAmount = 0.75f;

        var col = _sparksPs.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.white,                    0.00f),
                new GradientColorKey(new Color(0.78f, 0.92f, 1.00f), 1.00f),
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1.00f, 0.00f),
                new GradientAlphaKey(0.60f, 0.35f),
                new GradientAlphaKey(0.00f, 1.00f),
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var emission = _sparksPs.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;

        // Stretch: las chispas son pequeños trazos luminosos, no puntos
        _sparksRenderer.renderMode        = ParticleSystemRenderMode.Stretch;
        _sparksRenderer.velocityScale     = 0.004f;
        _sparksRenderer.lengthScale       = 1.0f;
        _sparksRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _sparksRenderer.receiveShadows    = false;
        _sparksRenderer.sortingFudge      = 2f;
        _sparksRenderer.material          = BuildDotMaterial();
    }

    // ── Ajustar presets ───────────────────────────────────────────────────────
    private void SetRipplePreset(float rate, float sizeMax)
    {
        var emission = _ripplePs.emission;
        emission.rateOverTime = rate;

        var sol   = _ripplePs.sizeOverLifetime;
        var curve = new AnimationCurve(
            new Keyframe(0.00f, 0.06f,  0f, 4f),
            new Keyframe(0.30f, 0.55f,  2f, 1f),
            new Keyframe(1.00f, sizeMax, 0.5f, 0f)
        );
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var main = _ripplePs.main;
        main.maxParticles = Mathf.Max(10, Mathf.CeilToInt(rate * 0.55f * 1.3f));
    }

    private void SetSprayPreset(float rate)
    {
        var emission = _sprayPs.emission;
        emission.rateOverTime = rate;

        var main = _sprayPs.main;
        main.maxParticles = Mathf.Max(10, Mathf.CeilToInt(rate * 0.28f * 1.3f));
        main.startSpeed   = new ParticleSystem.MinMaxCurve(sprayUpForce * 0.3f, sprayUpForce * 1.0f);
    }

    private void SetSparksPreset(float rate)
    {
        var emission = _sparksPs.emission;
        emission.rateOverTime = rate;

        var main = _sparksPs.main;
        main.maxParticles = Mathf.Max(10, Mathf.CeilToInt(rate * 0.18f * 1.3f));
    }

    // ── Materiales ────────────────────────────────────────────────────────────
    private static Shader FindParticleShader()
    {
        var s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s == null || !s.isSupported) s = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
        if (s == null || !s.isSupported) s = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (s == null || !s.isSupported) s = Shader.Find("Sprites/Default");
        return s;
    }

    private static Material MakeMat(string name, Texture2D tex)
    {
        var mat = new Material(FindParticleShader()) { name = name, mainTexture = tex };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     Color.white);
        if (mat.HasProperty("_Surface"))   mat.SetFloat("_Surface",   1f);
        if (mat.HasProperty("_Blend"))     mat.SetFloat("_Blend",     0f);
        mat.renderQueue = 3000;
        return mat;
    }

    private Material BuildRippleMaterial()  => MakeMat("RainRipple_Proc",  BuildRippleTexture());
    private Material BuildSprayMaterial()   => MakeMat("RainSpray_Proc",   (dropTexture != null) ? dropTexture : BuildDotTexture());
    private Material BuildDotMaterial()     => MakeMat("RainSparks_Proc",  BuildDotTexture());

    private Texture2D BuildRippleTexture()
    {
        const int Res = 64;
        var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            name       = "RainRipple_Tex",
        };

        float half       = Res * 0.5f;
        float outerR     = half * 0.95f;
        float innerR     = half * 0.58f;
        float ringCenter = (outerR + innerR) * 0.5f;
        float ringWidth  = (outerR - innerR) * 0.5f;

        for (int y = 0; y < Res; y++)
        for (int x = 0; x < Res; x++)
        {
            float dx   = x - half;
            float dy   = y - half;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float ring = 1f - Mathf.Clamp01(Mathf.Abs(dist - ringCenter) / ringWidth);
            ring       = Mathf.SmoothStep(0f, 1f, ring);
            tex.SetPixel(x, y, new Color(0.88f, 0.95f, 1.00f, ring * 0.95f));
        }
        tex.Apply();
        return tex;
    }

    private Texture2D BuildDotTexture()
    {
        const int Res = 16;
        var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            name       = "RainSpray_Tex",
        };

        float half = Res * 0.5f;
        float r    = half * 0.88f;

        for (int y = 0; y < Res; y++)
        for (int x = 0; x < Res; x++)
        {
            float dx   = x - half;
            float dy   = y - half;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float a    = Mathf.SmoothStep(r, r * 0.4f, dist);
            tex.SetPixel(x, y, new Color(0.90f, 0.96f, 1.00f, a));
        }
        tex.Apply();
        return tex;
    }

    // ── Posicionamiento en suelo ───────────────────────────────────────────────
    private void RefreshGroundY(bool force)
    {
        if (_cam == null) return;
        if (!force && Time.time < _nextRaycastTime) return;
        _nextRaycastTime = Time.time + k_RaycastInterval;

        if (useRaycast && Physics.Raycast(_cam.position, Vector3.down, out var hit, 20f, groundLayerMask))
            _groundY = hit.point.y + 0.03f;
        else
            _groundY = _cam.position.y + groundOffset;
    }

    private void SnapToGround()
    {
        if (_cam == null) return;
        Vector3 cam        = _cam.position;
        transform.position = new Vector3(cam.x, _groundY, cam.z);
    }
}
