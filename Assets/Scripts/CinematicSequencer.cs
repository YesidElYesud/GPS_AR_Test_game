using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toma de cámara individual dentro de una secuencia.
/// Define a dónde va la cámara (anchor), cuánto tarda en llegar (moveDuration)
/// y cuánto tiempo se queda quieta antes de pasar a la siguiente toma (holdDuration).
/// </summary>
[System.Serializable]
public class CameraShot
{
    [Tooltip("Transform que define la posición Y orientación de esta toma.\n" +
             "Crea un hijo del CinematicSequencer y colócalo en la escena donde quieres la cámara.")]
    public Transform anchor;

    [Tooltip("Segundos que tarda la cámara en moverse hasta este anchor desde la toma anterior.")]
    [Range(0.2f, 12f)] public float moveDuration = 2f;

    [Tooltip("Segundos que la cámara permanece quieta en este punto antes de pasar a la siguiente toma.")]
    [Range(0f,  10f)]  public float holdDuration = 1.5f;

    [Tooltip("Curva de animación del movimiento. EaseInOut (S-curve) por defecto.\n" +
             "Puedes personalizar cada toma: arranque lento, parada brusca, etc.")]
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
}

/// <summary>
/// CinematicSequencer — secuencia de tomas en tiempo real usando la escena del juego.
///
/// A diferencia de CinematicManager (que reproduce video), este sistema mueve Camera.main
/// a través de una lista de Transforms hijos, mostrando el estado actual de la escena.
///
/// Uso típico: Programación 2 "Conocer clima" — recorre el escenario mostrando
/// la lluvia, la quebrada y el nivel de riesgo N2 en la Etapa2.
///
/// Integración:
///   1. Añadir este componente a un GameObject en la escena.
///   2. Crear GameObjects hijos y posicionarlos donde quieres cada toma de cámara.
///   3. Asignar esos hijos a shots[].anchor en el Inspector.
///   4. Asignar este componente al campo cameraSequencer del HotspotController del Botón2.
///   5. En HotspotData del Botón2: actionType = CameraSequence, sequenceAdvancesStage = true.
///
/// UI requerida en AR_Canvas:
///   - SequenceSkipButton: Button inicialmente inactivo → asignar a skipButton.
///   - SequenceFadeOverlay: Image negra full-screen, alpha=0, inicialmente activa → asignar a fadeOverlay.
///     (RectTransform stretch 0,0,0,0; color negro; alpha=0; z-order: encima de todo excepto HUD)
/// </summary>
public class CinematicSequencer : MonoBehaviour
{
    [Header("Tomas de Cámara")]
    [Tooltip("Lista de tomas en orden de reproducción.\n" +
             "Crea hijos de este GameObject para cada anchor y asígnalos aquí.")]
    public CameraShot[] shots;

    [Header("Transición")]
    [Tooltip("Duración del fade negro al inicio y al final de la secuencia.")]
    [Range(0f, 1f)] public float fadeDuration = 0.35f;

    [Header("UI (opcional)")]
    [Tooltip("Botón 'Saltar' que aparece sobre la pantalla durante la secuencia.\n" +
             "Llama a RequestSkip() desde su OnClick. Inicialmente inactivo.")]
    public GameObject skipButton;

    [Tooltip("Image negra a pantalla completa para el fade. Alpha = 0 en reposo.\n" +
             "Debe estar en el Canvas encima de los paneles del juego.")]
    public Image fadeOverlay;

    // ── Estado ────────────────────────────────────────────────────────────────
    public  bool IsPlaying      { get; private set; }
    private bool _skipRequested;
    private HotspotController _caller;
    private bool _advancesStage;
    private Transform _cam;

    // ── Arranque ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        // Garantizar fade invisible al inicio
        if (fadeOverlay != null)
            SetFadeAlpha(0f);
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Inicia la secuencia. Llamado desde HotspotController.
    /// </summary>
    /// <param name="caller">Hotspot que activó la secuencia (para cerrar al terminar).</param>
    /// <param name="advancesStage">Si true, llama a StageManager.NextStage() al finalizar.</param>
    public void Play(HotspotController caller, bool advancesStage)
    {
        if (IsPlaying)
        {
            Debug.LogWarning("[CinematicSequencer] Ya hay una secuencia en curso. Ignorando.");
            return;
        }

        if (shots == null || shots.Length == 0)
        {
            Debug.LogWarning("[CinematicSequencer] No hay tomas definidas en 'shots'.", this);
            caller?.ClosePanel();
            return;
        }

        _caller        = caller;
        _advancesStage = advancesStage;
        StartCoroutine(RunSequence());
    }

    /// <summary>
    /// Llamado por el botón "Saltar" (asignar en Inspector del botón → OnClick).
    /// </summary>
    public void RequestSkip() => _skipRequested = true;

    // ── Corrutina principal ───────────────────────────────────────────────────
    private IEnumerator RunSequence()
    {
        IsPlaying      = true;
        _skipRequested = false;
        _cam           = Camera.main.transform;

        // 1. Bloquear input del jugador y ceder control de la cámara
        var arCtrl = Camera.main.GetComponent<ARCameraController>();
        arCtrl?.SetAerialMode(true);
        StageManager.Instance?.SetPlayerInputBlocked(true);

        // 2. Fade a negro (ocultar el corte brusco al primer anchor)
        yield return StartCoroutine(DoFade(0f, 1f, fadeDuration));

        // 3. Mostrar skip button
        if (skipButton != null) skipButton.SetActive(true);

        // 4. Saltar inmediatamente al anchor de la primera toma (sin lerp)
        //    El fade negro ya cubre el salto, así que no se ve el corte.
        if (shots.Length > 0 && shots[0].anchor != null)
        {
            _cam.position = shots[0].anchor.position;
            _cam.rotation = shots[0].anchor.rotation;
        }

        // 5. Fade desde negro al mundo (revelar primera toma)
        yield return StartCoroutine(DoFade(1f, 0f, fadeDuration));

        // 6. Ejecutar cada toma
        for (int i = 0; i < shots.Length; i++)
        {
            CameraShot shot = shots[i];
            if (shot.anchor == null)
            {
                Debug.LogWarning($"[CinematicSequencer] Shot [{i}] no tiene anchor asignado. Saltando.", this);
                continue;
            }

            if (_skipRequested) break;

            // La primera toma ya se posicionó arriba — empezamos a lerp desde la 2ª
            if (i > 0)
                yield return StartCoroutine(MoveToAnchor(shot));

            if (_skipRequested) break;

            // Mantener en este punto
            float held = 0f;
            while (held < shot.holdDuration && !_skipRequested)
            {
                held += Time.deltaTime;
                yield return null;
            }
        }

        // 7. Ocultar skip button
        if (skipButton != null) skipButton.SetActive(false);

        // 8. Fade a negro (cubrir el retorno a la cámara del jugador)
        yield return StartCoroutine(DoFade(0f, 1f, fadeDuration));

        // 9. Restaurar cámara del jugador
        arCtrl?.SetAerialMode(false);
        StageManager.Instance?.SetPlayerInputBlocked(false);

        // 10. Notificar al hotspot que puede cerrar su estado
        _caller?.ClosePanel();

        // 11. Avanzar etapa si corresponde (DESPUÉS de restaurar input)
        if (_advancesStage && StageManager.Instance != null)
            StageManager.Instance.NextStage();

        // 12. Fade de vuelta al mundo (revelar la vista del jugador)
        yield return StartCoroutine(DoFade(1f, 0f, fadeDuration));

        IsPlaying = false;
        _caller   = null;
    }

    // ── Movimiento suave hacia un anchor ─────────────────────────────────────
    private IEnumerator MoveToAnchor(CameraShot shot)
    {
        Vector3    startPos = _cam.position;
        Quaternion startRot = _cam.rotation;
        Vector3    endPos   = shot.anchor.position;
        Quaternion endRot   = shot.anchor.rotation;

        float elapsed = 0f;

        while (elapsed < shot.moveDuration && !_skipRequested)
        {
            elapsed += Time.deltaTime;
            float t = shot.ease.Evaluate(Mathf.Clamp01(elapsed / shot.moveDuration));

            _cam.position = Vector3.Lerp(startPos, endPos, t);
            _cam.rotation = Quaternion.Slerp(startRot, endRot, t);

            yield return null;
        }

        // Garantizar posición exacta al terminar
        if (!_skipRequested)
        {
            _cam.position = endPos;
            _cam.rotation = endRot;
        }
    }

    // ── Fade ──────────────────────────────────────────────────────────────────
    private IEnumerator DoFade(float fromAlpha, float toAlpha, float duration)
    {
        if (fadeOverlay == null || duration <= 0f)
        {
            SetFadeAlpha(toAlpha);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetFadeAlpha(Mathf.Lerp(fromAlpha, toAlpha, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }

        SetFadeAlpha(toAlpha);
    }

    private void SetFadeAlpha(float alpha)
    {
        if (fadeOverlay == null) return;
        Color c = fadeOverlay.color;
        fadeOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }

    // ── Gizmos: visualizar los anchors en la escena ───────────────────────────
    private void OnDrawGizmos()
    {
        if (shots == null) return;

        for (int i = 0; i < shots.Length; i++)
        {
            if (shots[i].anchor == null) continue;

            Vector3 pos = shots[i].anchor.position;
            Vector3 fwd = shots[i].anchor.forward;

            // Ícono de cámara (esfera pequeña + línea de dirección)
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireSphere(pos, 0.25f);
            Gizmos.DrawRay(pos, fwd * 1.2f);

            // Línea de conexión entre tomas consecutivas
            if (i < shots.Length - 1 && shots[i + 1].anchor != null)
            {
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
                Gizmos.DrawLine(pos, shots[i + 1].anchor.position);
            }

#if UNITY_EDITOR
            // Etiqueta con el número de la toma
            UnityEditor.Handles.Label(pos + Vector3.up * 0.4f,
                $"Shot {i + 1}\n{shots[i].moveDuration}s mov / {shots[i].holdDuration}s hold",
                new GUIStyle { fontSize = 9, normal = { textColor = new Color(0.2f, 0.9f, 1f) } });
#endif
        }
    }
}
