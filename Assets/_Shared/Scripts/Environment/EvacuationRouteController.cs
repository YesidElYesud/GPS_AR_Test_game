using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EvacuationRouteController v2 — LineRenderer con curva Catmull-Rom.
///
/// Dibuja una línea suave a través de los waypoints asignados.
/// La animación combina dos efectos:
///   • UV scroll   → la textura se desplaza a lo largo de la línea, dando la sensación
///                   de que las flechas "fluyen" hacia el punto de encuentro.
///   • Pulso ancho → el ancho de la línea "respira" suavemente con una onda seno.
///
/// Setup en Inspector:
///   1. Añade un componente LineRenderer al mismo GO (se añade solo por RequireComponent).
///   2. Crea GameObjects vacíos como hijos o en la escena y asígnalos a waypoints[].
///      El orden define la dirección: primer waypoint = origen, último = punto de encuentro.
///   3. (Opcional) Crea un Material con shader Unlit/Transparent y una textura de flechas
///      repetitiva. Asígnalo a lineMaterial. Si está vacío, se crea uno sólido en runtime.
///   4. Asigna puntoDeEncuentro para que la línea se oculte al llegar.
///
/// API pública:
///   Show()  — muestra la línea con fade-in. Llamado desde HotspotController.ClosePanel().
///   Hide()  — oculta la línea con fade-out.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class EvacuationRouteController : MonoBehaviour
{
    public static EvacuationRouteController Instance { get; private set; }

    // ── Ruta ──────────────────────────────────────────────────────────────────
    [Header("Ruta — Waypoints")]
    [Tooltip("Transforms vacíos que definen el camino, en orden desde el origen hasta el Punto de Encuentro.")]
    public Transform[] waypoints;

    [Tooltip("Número de subdivisiones Catmull-Rom entre cada par de waypoints. Más = curva más suave.")]
    [Range(2, 20)]
    public int smoothSteps = 8;

    // ── Punto de encuentro ────────────────────────────────────────────────────
    [Header("Punto de Encuentro")]
    [Tooltip("Al acercarse a este Transform la línea se oculta automáticamente.")]
    public Transform puntoDeEncuentro;

    [Tooltip("Radio (m) para el auto-ocultamiento.")]
    public float hideRadius = 4f;

    // ── Apariencia ────────────────────────────────────────────────────────────
    [Header("Apariencia")]
    [Tooltip("Ancho base de la línea en metros.")]
    public float lineWidth = 0.5f;

    [Tooltip("Color de la línea. El canal A es la opacidad máxima.")]
    public Color lineColor = new Color(0.15f, 0.95f, 0.35f, 0.85f);

    [Tooltip("Material Unlit/Transparent con textura de flechas repetitiva.\n" +
             "Si está vacío se genera uno sólido en runtime.")]
    public Material lineMaterial;

    // ── Animación ─────────────────────────────────────────────────────────────
    [Header("Animación")]
    [Tooltip("Velocidad de desplazamiento de la textura (flechas fluyendo). 0 = sin scroll.")]
    public float scrollSpeed = 0.6f;

    [Tooltip("Velocidad del pulso de ancho.")]
    public float pulseSpeed = 1.4f;

    [Tooltip("Amplitud del pulso de ancho (0 = sin pulso, 0.3 = ±30% del lineWidth).")]
    [Range(0f, 0.5f)]
    public float pulseAmplitude = 0.15f;

    // ── Fade ──────────────────────────────────────────────────────────────────
    [Header("Fade")]
    public float fadeInDuration  = 0.8f;
    public float fadeOutDuration = 0.5f;

    // ── Internos ──────────────────────────────────────────────────────────────
    private LineRenderer _lr;
    private Coroutine    _fadeRoutine;
    private float        _currentAlpha;
    private bool         _isVisible;
    private float        _scrollOffset;
    private float        _pulseTime;
    private bool         _hasTexture;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _lr = GetComponent<LineRenderer>();
        ConfigureLineRenderer();
        BuildPath();

        _lr.enabled = false;
        ApplyAlpha(0f);
    }

    private void ConfigureLineRenderer()
    {
        _lr.useWorldSpace     = true;
        _lr.numCornerVertices = 4;
        _lr.numCapVertices    = 4;
        _lr.textureMode       = LineTextureMode.Tile;
        _lr.generateLightingData = false;
        _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _lr.receiveShadows    = false;

        if (lineMaterial != null)
        {
            _lr.material = lineMaterial;
            _hasTexture  = true;
        }
        else
        {
            // Material sólido de respaldo — no requiere textura
            var mat = new Material(Shader.Find("Sprites/Default"))
            {
                color = lineColor
            };
            _lr.material = mat;
            _hasTexture  = false;
        }

        _lr.startWidth = lineWidth;
        _lr.endWidth   = lineWidth;
        ApplyAlpha(0f);
    }

    private void Update()
    {
        if (!_isVisible) return;

        // ── UV scroll: flechas fluyendo hacia el destino ──────────────────────
        if (_hasTexture && scrollSpeed > 0f)
        {
            _scrollOffset += scrollSpeed * Time.deltaTime;
            _lr.material.SetTextureOffset("_MainTex", new Vector2(-_scrollOffset, 0f));
        }

        // ── Pulso de ancho ────────────────────────────────────────────────────
        if (pulseAmplitude > 0f)
        {
            _pulseTime += pulseSpeed * Time.deltaTime;
            float pulse = 1f + pulseAmplitude * Mathf.Sin(_pulseTime * Mathf.PI * 2f);
            _lr.startWidth = lineWidth * pulse;
            _lr.endWidth   = lineWidth * pulse;
        }

        // ── Auto-ocultar al llegar al Punto de Encuentro ──────────────────────
        if (puntoDeEncuentro == null || Camera.main == null) return;
        Vector3 flat = puntoDeEncuentro.position - Camera.main.transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude <= hideRadius * hideRadius)
            Hide();
    }

    // ── API pública ───────────────────────────────────────────────────────────
    public void Show()
    {
        if (_isVisible) return;
        _isVisible = true;
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeRoutine(_currentAlpha, 1f, fadeInDuration));
    }

    public void Hide()
    {
        if (!_isVisible) return;
        _isVisible = false;
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeRoutine(_currentAlpha, 0f, fadeOutDuration));
    }

    // ── Construcción de la curva Catmull-Rom ──────────────────────────────────
    private void BuildPath()
    {
        if (waypoints == null || waypoints.Length < 2)
        {
            _lr.positionCount = 0;
            Debug.LogWarning("[EvacuationRouteController] Se necesitan al menos 2 waypoints.", this);
            return;
        }

        var points = new List<Vector3>();

        for (int i = 0; i < waypoints.Length - 1; i++)
        {
            if (waypoints[i] == null || waypoints[i + 1] == null) continue;

            // Puntos fantasma en los extremos: replican el primero/último waypoint
            Vector3 p0 = waypoints[Mathf.Max(0, i - 1)] != null
                ? waypoints[Mathf.Max(0, i - 1)].position
                : waypoints[i].position;

            Vector3 p1 = waypoints[i].position;
            Vector3 p2 = waypoints[i + 1].position;

            int nextNext = Mathf.Min(waypoints.Length - 1, i + 2);
            Vector3 p3 = waypoints[nextNext] != null
                ? waypoints[nextNext].position
                : waypoints[i + 1].position;

            for (int step = 0; step < smoothSteps; step++)
            {
                float t = step / (float)smoothSteps;
                points.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        // Añadir el último waypoint
        if (waypoints[waypoints.Length - 1] != null)
            points.Add(waypoints[waypoints.Length - 1].position);

        _lr.positionCount = points.Count;
        _lr.SetPositions(points.ToArray());
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
              2f * p1
            + (-p0 + p2)                        * t
            + ( 2f*p0 - 5f*p1 + 4f*p2 - p3)    * t2
            + (-p0 + 3f*p1 - 3f*p2 + p3)        * t3
        );
    }

    // ── Fade ──────────────────────────────────────────────────────────────────
    private IEnumerator FadeRoutine(float from, float to, float duration)
    {
        if (to > 0f) _lr.enabled = true;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            ApplyAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        ApplyAlpha(to);

        if (to <= 0f) _lr.enabled = false;
    }

    private void ApplyAlpha(float alpha)
    {
        _currentAlpha = alpha;
        if (_lr == null || _lr.material == null) return;

        Color c = lineColor;
        c.a = lineColor.a * alpha;
        _lr.startColor = c;
        _lr.endColor   = c;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        // Líneas directas entre waypoints (preview rápido)
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.7f);
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            Gizmos.DrawWireSphere(waypoints[i].position, i == 0 ? 0.35f : 0.22f);

            if (i > 0 && waypoints[i - 1] != null)
                Gizmos.DrawLine(waypoints[i - 1].position, waypoints[i].position);
        }

        // Radio del punto de encuentro
        if (puntoDeEncuentro != null)
        {
            Gizmos.color = new Color(0.1f, 1f, 0.3f, 0.25f);
            Gizmos.DrawSphere(puntoDeEncuentro.position, hideRadius);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (waypoints == null) return;

        // Mostrar índices y la curva aproximada en selección
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.9f);
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            // Dibujar número de waypoint como esfera de tamaño creciente
            Gizmos.DrawWireSphere(waypoints[i].position, 0.1f * (i + 1));
        }
    }
}
