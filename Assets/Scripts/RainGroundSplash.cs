using UnityEngine;

/// <summary>
/// RainGroundSplash — Impactos de lluvia sobre el suelo.
///
/// Se crea como objeto RAÍZ de la escena (no hijo de Rain) para que su
/// posición en suelo sea totalmente independiente del movimiento de Rain.
/// RainParticleController lo instancia vía EnsureSplashSystem().
///
/// Contiene dos sistemas de partículas hijos propios:
///   · Ripple  — anillo expansivo plano (HorizontalBillboard).
///   · Spray   — microgotas que salen hacia arriba y caen de vuelta.
///
/// Optimizaciones WebGL:
///   · Heavy: ~30 ripples + ~35 sprays activas simultáneamente.
///   · Sin texturas externas — todo procedural.
///   · Raycast al terreno cada 0.3s (no cada frame).
///   · Shadows OFF en ambos renderers.
/// </summary>
public class RainGroundSplash : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Posición en suelo")]
    [Tooltip("Offset Y desde la cámara al suelo. Con CharacterController height=1.7 → usar -1.65.")]
    [Range(-4f, 0f)]
    public float groundOffset = -1.65f;

    [Tooltip("Radio del área de emisión. Debe coincidir con areaSize/2 del sistema de lluvia.")]
    [Range(5f, 40f)]
    public float areaRadius = 14f;

    [Header("Física de suelo")]
    [Tooltip("Raycast para snappear al terreno real (1 cast cada 0.3s). Desactivar si hay lag.")]
    public bool useRaycast = true;
    public LayerMask groundLayerMask = ~0;

    [Header("Calibración visual")]
    [Tooltip("Tamaño máximo del anillo de ripple (metros) en intensidad Heavy.")]
    [Range(0.3f, 2.0f)]
    public float rippleMaxSizeHeavy = 0.80f;

    [Tooltip("Fuerza de subida del spray (m/s). Mayor = salpique más alto.")]
    [Range(0.5f, 6.0f)]
    public float sprayUpForce = 2.5f;

    // ── Presets ───────────────────────────────────────────────────────────────
    // (rippleRate, rippleSizeMax, sprayRate)
    private static readonly (float rRate, float rMax, float sRate)[] k_Presets =
    {
        (  0f, 0.00f,   0f),   // None
        ( 18f, 0.40f,  30f),   // Light
        ( 50f, 0.60f,  80f),   // Medium
        (100f, 1.00f, 160f),   // Heavy
    };

    // ── Internos ──────────────────────────────────────────────────────────────
    private ParticleSystem         _ripplePs;
    private ParticleSystemRenderer _rippleRenderer;
    private ParticleSystem         _sprayPs;
    private ParticleSystemRenderer _sprayRenderer;

    private Transform _cam;
    private float     _groundY;
    private float     _nextRaycastTime;
    private const float k_RaycastInterval = 0.30f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        BuildRippleSystem();
        BuildSpraySystem();
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
        var (rRate, rMax, sRate) = k_Presets[(int)preset];

        if (preset == RainParticleController.RainIntensity.None)
        {
            _ripplePs.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _sprayPs.Stop(true,  ParticleSystemStopBehavior.StopEmitting);
            return;
        }

        // Ripple
        float scaledRMax = rMax * (rippleMaxSizeHeavy / k_Presets[(int)RainParticleController.RainIntensity.Heavy].rMax);
        SetRipplePreset(rRate, scaledRMax);

        // Spray
        SetSprayPreset(sRate);

        if (!_ripplePs.isPlaying) _ripplePs.Play();
        if (!_sprayPs.isPlaying)  _sprayPs.Play();
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

        // ── Main ──────────────────────────────────────────────────────────────
        var main = _ripplePs.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.startSpeed      = 0f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.35f, 0.50f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);  // tamaño inicial pequeño
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.82f, 0.92f, 1.00f, 0.75f),
                                   new Color(1.00f, 1.00f, 1.00f, 0.95f));
        main.startRotation3D = false;
        main.maxParticles    = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.stopAction      = ParticleSystemStopAction.Disable;

        // ── Shape: disco horizontal en el suelo ────────────────────────────────
        var shape = _ripplePs.shape;
        shape.enabled          = true;
        shape.shapeType        = ParticleSystemShapeType.Circle;
        shape.radius           = areaRadius;
        shape.radiusThickness  = 1f;    // emitir en toda el área, no solo en el borde

        // ── SizeOverLifetime: crece rápido (onda expansiva) ───────────────────
        var sol  = _ripplePs.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0.00f, 0.06f, 0f, 4f),    // empieza muy pequeño
            new Keyframe(0.30f, 0.60f, 2f, 1f),    // crece rápido
            new Keyframe(1.00f, 1.00f, 0.5f, 0f)   // se asienta en max
        );
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // ── ColorOverLifetime: alpha 0.9 → 0 ────────────────────────────────
        var col = _ripplePs.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.85f, 0.94f, 1f), 0.00f),
                new GradientColorKey(new Color(0.93f, 0.97f, 1f), 0.40f),
                new GradientColorKey(Color.white,                  1.00f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.90f, 0.00f),
                new GradientAlphaKey(0.55f, 0.50f),
                new GradientAlphaKey(0.00f, 1.00f),
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // ── Emission ──────────────────────────────────────────────────────────
        var emission = _ripplePs.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;

        // ── Renderer: HorizontalBillboard = siempre plano mirando hacia arriba ─
        _rippleRenderer.renderMode        = ParticleSystemRenderMode.HorizontalBillboard;
        _rippleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _rippleRenderer.receiveShadows    = false;
        _rippleRenderer.sortingFudge      = 0f;
        _rippleRenderer.material          = BuildMaterial(isRipple: true);
    }

    // ── Construcción: Spray ───────────────────────────────────────────────────
    private void BuildSpraySystem()
    {
        var go = new GameObject("Spray");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        // Rotamos -90° en X: la normal del Circle ahora apunta hacia arriba (world Y).
        // Esto hace que las partículas salgan hacia arriba al nacer.
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        _sprayPs       = go.AddComponent<ParticleSystem>();
        _sprayRenderer = go.GetComponent<ParticleSystemRenderer>();

        // ── Main ──────────────────────────────────────────────────────────────
        var main = _sprayPs.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.startSpeed      = new ParticleSystem.MinMaxCurve(
                                   sprayUpForce * 0.3f,
                                   sprayUpForce * 1.0f);
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.15f, 0.30f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.040f, 0.080f);  // 4-8cm: visible desde primera persona
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.80f, 0.91f, 1.00f, 0.65f),
                                   new Color(0.95f, 0.98f, 1.00f, 0.90f));
        main.maxParticles    = 150;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 2.8f;    // caída rápida para mantener realismo
        main.stopAction      = ParticleSystemStopAction.Disable;

        // ── Shape: círculo emite hacia arriba (por la rotación del GameObject) ─
        var shape = _sprayPs.shape;
        shape.enabled              = true;
        shape.shapeType            = ParticleSystemShapeType.Circle;
        shape.radius               = areaRadius;
        shape.radiusThickness      = 1f;
        shape.randomDirectionAmount = 0.35f;   // spread natural, no todas verticales

        // ── ColorOverLifetime: fade rápido ────────────────────────────────────
        var col = _sprayPs.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.00f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // ── Emission ──────────────────────────────────────────────────────────
        var emission = _sprayPs.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;

        // ── Renderer ─────────────────────────────────────────────────────────
        _sprayRenderer.renderMode        = ParticleSystemRenderMode.Billboard;
        _sprayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _sprayRenderer.receiveShadows    = false;
        _sprayRenderer.sortingFudge      = 1f;
        _sprayRenderer.material          = BuildMaterial(isRipple: false);
    }

    // ── Ajustar presets ───────────────────────────────────────────────────────
    private void SetRipplePreset(float rate, float sizeMax)
    {
        // Actualizar emission
        var emission = _ripplePs.emission;
        emission.rateOverTime = rate;

        // Actualizar tamaño máximo de la onda
        var sol = _ripplePs.sizeOverLifetime;
        var curve = new AnimationCurve(
            new Keyframe(0.00f, 0.06f,  0f, 4f),
            new Keyframe(0.30f, 0.55f,  2f, 1f),
            new Keyframe(1.00f, sizeMax, 0.5f, 0f)
        );
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

        // maxParticles dinámico
        var main = _ripplePs.main;
        main.maxParticles = Mathf.Max(10, Mathf.CeilToInt(rate * 0.50f * 1.3f));
    }

    private void SetSprayPreset(float rate)
    {
        var emission = _sprayPs.emission;
        emission.rateOverTime = rate;

        var main = _sprayPs.main;
        main.maxParticles = Mathf.Max(10, Mathf.CeilToInt(rate * 0.20f * 1.3f));
        main.startSpeed   = new ParticleSystem.MinMaxCurve(
                                sprayUpForce * 0.3f,
                                sprayUpForce * 1.0f);
    }

    // ── Materiales procedurales ───────────────────────────────────────────────
    private Material BuildMaterial(bool isRipple)
    {
        var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Particles/Alpha Blended");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");

        var mat = new Material(shader)
        {
            name        = isRipple ? "RainRipple_Procedural" : "RainSpray_Procedural",
            mainTexture = isRipple ? BuildRippleTexture() : BuildDotTexture(),
            color       = Color.white,
        };
        return mat;
    }

    /// <summary>
    /// Textura de anillo (ring) 64×64 px para el ripple.
    /// El anillo tiene bordes suaves para un aspecto más orgánico.
    /// </summary>
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
            ring = Mathf.SmoothStep(0f, 1f, ring);

            tex.SetPixel(x, y, new Color(0.88f, 0.95f, 1.00f, ring * 0.92f));
        }
        tex.Apply();
        return tex;
    }

    /// <summary>Textura de punto suave 16×16 px para el spray.</summary>
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
        {
            _groundY = hit.point.y + 0.03f;   // 3cm sobre el terreno para evitar z-fighting
        }
        else
        {
            _groundY = _cam.position.y + groundOffset;
        }
    }

    private void SnapToGround()
    {
        if (_cam == null) return;
        Vector3 cam = _cam.position;
        transform.position = new Vector3(cam.x, _groundY, cam.z);
        // El spray hijo tiene rotación local -90° (fija) — no se toca aquí
    }
}
