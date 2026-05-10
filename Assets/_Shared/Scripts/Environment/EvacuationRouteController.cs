using System.Collections;
using UnityEngine;

/// <summary>
/// Controla la línea verde de evacuación en suelo.
/// Se activa al cerrar el panel del hotspot "Ruta de Evacuación"
/// y se oculta cuando el jugador llega al punto de encuentro.
///
/// Setup en Inspector:
///   routeRoot        → GO padre de los Quads (puede ser este mismo GO).
///                      Si está vacío, busca Renderers en los hijos de este GO.
///   puntoDeEncuentro → Transform del punto de encuentro (para auto-ocultar).
///   hideRadius       → distancia (m) al punto de encuentro para ocultar la ruta.
///
/// IMPORTANTE: el script nunca llama SetActive sobre sí mismo.
/// Los segmentos se ocultan/muestran via Renderer.enabled + fade de alpha.
/// </summary>
public class EvacuationRouteController : MonoBehaviour
{
    public static EvacuationRouteController Instance { get; private set; }

    [Header("Ruta")]
    [Tooltip("GO padre que contiene los Quads. Si está vacío, se usan los hijos de este GO.")]
    [SerializeField] private GameObject routeRoot;

    [Tooltip("Transform del Punto de Encuentro — al acercarse se oculta la línea.")]
    [SerializeField] private Transform puntoDeEncuentro;

    [Tooltip("Distancia (m) al Punto de Encuentro para ocultar automáticamente la línea.")]
    [SerializeField] private float hideRadius = 4f;

    [Header("Fade")]
    [SerializeField] private float fadeInDuration  = 0.8f;
    [SerializeField] private float fadeOutDuration = 0.5f;

    // ── Internos ──────────────────────────────────────────────────────────────
    private Renderer[] _renderers;
    private Material[]  _materials;
    private Coroutine   _fadeRoutine;
    private float       _currentAlpha;
    private bool        _isVisible;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        GameObject root = routeRoot != null ? routeRoot : gameObject;
        _renderers = root.GetComponentsInChildren<Renderer>(true);
        _materials = new Material[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
            _materials[i] = _renderers[i].material; // instancia por renderer

        // Ocultar sin desactivar el GO: solo renderer.enabled = false
        foreach (var r in _renderers) if (r != null) r.enabled = false;
        _currentAlpha = 0f;
    }

    private void Update()
    {
        if (!_isVisible || puntoDeEncuentro == null || Camera.main == null) return;

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

    // ── Interno ───────────────────────────────────────────────────────────────
    private IEnumerator FadeRoutine(float from, float to, float duration)
    {
        // Fade IN → activar renderers antes de animar
        if (to > 0f)
            foreach (var r in _renderers) if (r != null) r.enabled = true;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        SetAlpha(to);

        // Fade OUT → desactivar renderers al terminar (evita drawcalls innecesarios)
        if (to <= 0f)
            foreach (var r in _renderers) if (r != null) r.enabled = false;
    }

    private void SetAlpha(float alpha)
    {
        _currentAlpha = alpha;
        if (_materials == null) return;
        foreach (var mat in _materials)
        {
            if (mat == null) continue;
            Color c = mat.color;
            c.a = alpha;
            mat.color = c;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (puntoDeEncuentro == null) return;
        Gizmos.color = new Color(0.1f, 1f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(puntoDeEncuentro.position, hideRadius);
    }
}
