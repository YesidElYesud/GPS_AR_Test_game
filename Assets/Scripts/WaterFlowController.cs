using UnityEngine;

/// <summary>
/// Controla la animación de flujo de agua sobre una malla con el shader Custom/WaterFlow.
/// También funciona en modo fallback con cualquier shader estándar (scroll de _MainTex).
///
/// Uso:
///   1. Asignar el script al GameObject "agua" (o "rio") del Terrain_V2.
///   2. Si el material usa Custom/WaterFlow → dejar fallbackMode = false.
///   3. Si el material usa otro shader → activar fallbackMode = true (solo scrollea _MainTex).
///   4. Ajustar flowDirection en el Inspector para controlar la dirección de la corriente.
///
/// API pública (usada por WaterColorController en Fase 4):
///   SetFlowDirection(Vector2)  — cambia dirección en tiempo de ejecución
///   SetSpeed(float)            — cambia velocidad
///   SetColor(Color)            — cambia color del agua (integración con Etapa4)
///   LerpColor(Color, Color, t) — interpola color entre dos valores
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class WaterFlowController : MonoBehaviour
{
    // ─── Parámetros de flujo ───────────────────────────────────────────────
    [Header("Dirección de corriente")]
    [Tooltip("Vector 2D que indica hacia dónde fluye el agua. Se normaliza automáticamente.")]
    [SerializeField] private Vector2 flowDirection = new Vector2(1f, 0f);

    [Tooltip("Velocidad de desplazamiento de la textura.")]
    [SerializeField] [Range(0f, 1f)] private float flowSpeed = 0.08f;

    // ─── Color ────────────────────────────────────────────────────────────
    [Header("Color")]
    [SerializeField] private Color waterColor = new Color(0.18f, 0.48f, 0.75f, 0.75f);

    // ─── Modo de compatibilidad ────────────────────────────────────────────
    [Header("Compatibilidad")]
    [Tooltip("TRUE: scroll manual de _MainTex (cualquier shader). " +
             "FALSE: usa propiedades de Custom/WaterFlow (mejor calidad).")]
    [SerializeField] private bool fallbackMode = false;

    // ─── Interno ──────────────────────────────────────────────────────────
    private Material _mat;

    // IDs de propiedades del shader (evita strings en Update)
    private static readonly int PropFlowDir   = Shader.PropertyToID("_FlowDirection");
    private static readonly int PropFlowSpeed = Shader.PropertyToID("_FlowSpeed");
    private static readonly int PropColor     = Shader.PropertyToID("_Color");

    // ──────────────────────────────────────────────────────────────────────
    void Awake()
    {
        // .material crea instancia propia → cambios no afectan otros objetos
        _mat = GetComponent<MeshRenderer>().material;
        PushToMaterial();
    }

    void Update()
    {
        if (!fallbackMode) return;

        // Fallback: desplazar offset de _MainTex manualmente (funciona con
        // Standard, URP/Lit, o cualquier shader con _MainTex).
        Vector2 dir    = NormalizedDir();
        Vector2 offset = dir * (flowSpeed * Time.time);
        _mat.SetTextureOffset("_MainTex", offset);
    }

    // ─── API pública ──────────────────────────────────────────────────────

    /// <summary>Cambia la dirección del flujo en tiempo de ejecución.</summary>
    public void SetFlowDirection(Vector2 dir)
    {
        flowDirection = dir;
        PushToMaterial();
    }

    /// <summary>Cambia la velocidad del flujo.</summary>
    public void SetSpeed(float speed)
    {
        flowSpeed = Mathf.Max(0f, speed);
        PushToMaterial();
    }

    /// <summary>Cambia el color del agua. Útil para WaterColorController (Etapa4).</summary>
    public void SetColor(Color color)
    {
        waterColor = color;
        if (!fallbackMode)
            _mat.SetColor(PropColor, color);
    }

    /// <summary>Interpola el color entre <paramref name="from"/> y <paramref name="to"/>.</summary>
    public void LerpColor(Color from, Color to, float t)
        => SetColor(Color.Lerp(from, to, Mathf.Clamp01(t)));

    // ─── Interno ──────────────────────────────────────────────────────────

    private void PushToMaterial()
    {
        if (_mat == null || fallbackMode) return;

        Vector2 dir = NormalizedDir();
        _mat.SetVector(PropFlowDir,   new Vector4(dir.x, dir.y, 0f, 0f));
        _mat.SetFloat (PropFlowSpeed, flowSpeed);
        _mat.SetColor (PropColor,     waterColor);
    }

    private Vector2 NormalizedDir()
    {
        return flowDirection.sqrMagnitude > 0.0001f
            ? flowDirection.normalized
            : Vector2.right;
    }

#if UNITY_EDITOR
    // Actualización en vivo desde el Inspector (sin Play)
    void OnValidate()
    {
        if (_mat == null)
        {
            var r = GetComponent<MeshRenderer>();
            if (r != null) _mat = r.sharedMaterial;
        }
        PushToMaterial();
    }
#endif
}
