using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class CameraShot
{
    [Tooltip("Transform que define la posición Y orientación de esta toma.\n" +
             "Crea un hijo del CinematicSequencer y colócalo en la escena donde quieres la cámara.")]
    public Transform anchor;

    [Tooltip("Segundos que tarda la cámara en moverse hasta este anchor desde la toma anterior.")]
    [Range(0.2f, 12f)] public float moveDuration = 2f;

    [Tooltip("Segundos que la cámara permanece quieta en este punto antes de pasar a la siguiente toma.")]
    [Range(0f, 10f)] public float holdDuration = 1.5f;

    [Tooltip("Curva de animación del movimiento. EaseInOut (S-curve) por defecto.\n" +
             "Puedes personalizar cada toma: arranque lento, parada brusca, etc.")]
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
}

public class CinematicSequencer : MonoBehaviour
{
    [Header("Tomas de Cámara")]
    [Tooltip("Lista de tomas en orden de reproducción.\n" +
             "Crea hijos de este GameObject para cada anchor y asígnalos aquí.")]
    public CameraShot[] shots;

    [Header("Transición")]
    [Tooltip("Duración del fade negro al inicio y al final de la secuencia.")]
    [Range(0f, 1f)] public float fadeDuration = 0.35f;

    [Header("Movimiento de Dron")]
    [Tooltip("Activa deriva orgánica procedural sobre posición y rotación (ruido Perlin).")]
    public bool droneMode = true;

    [Tooltip("Amplitud máxima de la deriva de posición en metros. 0.08 = sutil, 0.20 = pronunciado.")]
    [Range(0f, 0.4f)] public float driftAmplitude = 0.08f;

    [Tooltip("Velocidad del ruido Perlin. Más bajo = movimiento más lento y orgánico.")]
    [Range(0.05f, 1.5f)] public float driftSpeed = 0.35f;

    [Tooltip("Amplitud máxima de la deriva de rotación en grados. Pitch + roll suave.")]
    [Range(0f, 3f)] public float rotationDrift = 0.6f;

    [Tooltip("Fracción del movimiento de dron que se aplica durante el viaje entre tomas.\n" +
             "0 = solo en pausa (cámara quieta entre shots), 1 = igual que en pausa.")]
    [Range(0f, 1f)] public float movementTurbulence = 0.4f;

    [Header("UI (opcional)")]
    [Tooltip("Botón 'Saltar' que aparece sobre la pantalla durante la secuencia.\n" +
             "Llama a RequestSkip() desde su OnClick. Inicialmente inactivo.")]
    public GameObject skipButton;

    [Tooltip("Image negra a pantalla completa para el fade. Alpha = 0 en reposo.\n" +
             "Debe estar en el Canvas encima de los paneles del juego.")]
    public Image fadeOverlay;

    [Header("Joystick")]
    [Tooltip("Panel del joystick táctil. Se oculta al iniciar y se restaura al terminar o saltar.")]
    public GameObject joystickPanel;

    [Header("Indicador de Progreso")]
    [Tooltip("Image circular que se llena conforme avanza la secuencia.\n" +
             "Configurar en Inspector: Image Type=Filled · Fill Method=Radial360 · Fill Origin=Top · Clockwise=true.\n" +
             "Empieza inactiva; el script la activa y desactiva automáticamente.")]
    public Image progressRing;

    // ── Estado ────────────────────────────────────────────────────────────────
    public  bool IsPlaying      { get; private set; }
    private bool _skipRequested;
    private HotspotController _caller;
    private bool _advancesStage;
    private Transform _cam;

    // Ruido procedural (dron) — acumulan durante toda la secuencia
    private float _noiseTime;
    private float _noiseSeedX;
    private float _noiseSeedZ;

    // ── Arranque ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (fadeOverlay != null)
            SetFadeAlpha(0f);
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Inicia la secuencia. Llamado desde HotspotController.
    /// </summary>
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
    /// Llamado por el botón "Saltar" (OnClick en Inspector).
    /// </summary>
    public void RequestSkip() => _skipRequested = true;

    // ── Corrutina principal ───────────────────────────────────────────────────
    private IEnumerator RunSequence()
    {
        IsPlaying      = true;
        _skipRequested = false;
        _cam           = Camera.main.transform;

        // Semillas aleatorias: cada reproducción tiene una deriva única
        _noiseTime  = Random.Range(0f, 100f);
        _noiseSeedX = Random.Range(0f, 100f);
        _noiseSeedZ = Random.Range(0f, 100f);

        // Guardar posición y rotación del jugador antes de cualquier movimiento
        Vector3    savedPos = _cam.position;
        Quaternion savedRot = _cam.rotation;

        // Ocultar joystick
        bool joystickWasActive = joystickPanel != null && joystickPanel.activeSelf;
        if (joystickPanel != null) joystickPanel.SetActive(false);

        // Calcular duración total y arrancar anillo de progreso
        float totalDuration = 0f;
        foreach (var s in shots) totalDuration += s.moveDuration + s.holdDuration;
        Coroutine ringRoutine = null;
        if (progressRing != null)
        {
            progressRing.fillAmount = 0f;
            progressRing.gameObject.SetActive(true);
            ringRoutine = StartCoroutine(UpdateProgressRing(totalDuration));
        }

        // Bloquear input del jugador y ceder control de la cámara
        var arCtrl = Camera.main.GetComponent<ARCameraController>();
        arCtrl?.SetAerialMode(true);
        StageManager.Instance?.SetPlayerInputBlocked(true);

        // Fade a negro (cubre el salto al primer anchor)
        yield return StartCoroutine(DoFade(0f, 1f, fadeDuration));

        if (skipButton != null) skipButton.SetActive(true);

        // Saltar inmediatamente al anchor de la primera toma bajo el fade
        if (shots.Length > 0 && shots[0].anchor != null)
        {
            _cam.position = shots[0].anchor.position;
            _cam.rotation = shots[0].anchor.rotation;
        }

        // Revelar primera toma
        yield return StartCoroutine(DoFade(1f, 0f, fadeDuration));

        // Ejecutar cada toma
        for (int i = 0; i < shots.Length; i++)
        {
            CameraShot shot = shots[i];
            if (shot.anchor == null)
            {
                Debug.LogWarning($"[CinematicSequencer] Shot [{i}] no tiene anchor asignado. Saltando.", this);
                continue;
            }

            if (_skipRequested) break;

            // La primera toma ya se posicionó arriba — lerp solo desde la 2ª en adelante
            if (i > 0)
                yield return StartCoroutine(MoveToAnchor(shot));

            if (_skipRequested) break;

            // Hold — deriva de dron completa mientras la cámara "flota" en el anchor
            float held = 0f;
            while (held < shot.holdDuration && !_skipRequested)
            {
                held       += Time.deltaTime;
                _noiseTime += Time.deltaTime;

                if (droneMode)
                {
                    GetDroneNoise(driftAmplitude, rotationDrift, out Vector3 dp, out Quaternion dr);
                    _cam.position = shot.anchor.position + dp;
                    _cam.rotation = shot.anchor.rotation * dr;
                }

                yield return null;
            }
        }

        // Ocultar botón y anillo
        if (skipButton != null) skipButton.SetActive(false);
        if (ringRoutine != null) StopCoroutine(ringRoutine);
        if (progressRing != null) progressRing.gameObject.SetActive(false);

        // Fade a negro (cubre el retorno a la cámara del jugador)
        yield return StartCoroutine(DoFade(0f, 1f, fadeDuration));

        // Restaurar posición del jugador bajo el fade
        var cc = Camera.main.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        _cam.position = savedPos;
        _cam.rotation = savedRot;
        if (cc != null) cc.enabled = true;

        arCtrl?.SetAerialMode(false);
        StageManager.Instance?.SetPlayerInputBlocked(false);

        if (joystickWasActive && joystickPanel != null) joystickPanel.SetActive(true);

        _caller?.ClosePanel();

        if (_advancesStage && StageManager.Instance != null)
            StageManager.Instance.NextStage();

        // Revelar vista del jugador
        yield return StartCoroutine(DoFade(1f, 0f, fadeDuration));

        IsPlaying = false;
        _caller   = null;
    }

    // ── Movimiento suave hacia un anchor (con turbulencia opcional) ───────────
    private IEnumerator MoveToAnchor(CameraShot shot)
    {
        // Si droneMode, la posición de inicio incluye el offset de dron del frame anterior;
        // usar el anchor limpio evitaría el "salto" al rearrancar — tomamos la pos actual.
        Vector3    startPos = _cam.position;
        Quaternion startRot = _cam.rotation;
        Vector3    endPos   = shot.anchor.position;
        Quaternion endRot   = shot.anchor.rotation;

        float elapsed = 0f;

        while (elapsed < shot.moveDuration && !_skipRequested)
        {
            elapsed    += Time.deltaTime;
            _noiseTime += Time.deltaTime;
            float t = shot.ease.Evaluate(Mathf.Clamp01(elapsed / shot.moveDuration));

            Vector3    lerpPos = Vector3.Lerp(startPos, endPos, t);
            Quaternion lerpRot = Quaternion.Slerp(startRot, endRot, t);

            if (droneMode && movementTurbulence > 0f)
            {
                GetDroneNoise(
                    driftAmplitude  * movementTurbulence,
                    rotationDrift   * movementTurbulence,
                    out Vector3 dp, out Quaternion dr);
                _cam.position = lerpPos + dp;
                _cam.rotation = lerpRot * dr;
            }
            else
            {
                _cam.position = lerpPos;
                _cam.rotation = lerpRot;
            }

            yield return null;
        }

        // Asentar en el anchor exacto al terminar el viaje
        // (el hold loop retomará la deriva en el primer frame siguiente)
        if (!_skipRequested)
        {
            _cam.position = endPos;
            _cam.rotation = endRot;
        }
    }

    // ── Ruido Perlin tipo gimbal de dron ──────────────────────────────────────
    // Genera un offset de posición y rotación con movimiento continuo y orgánico.
    // El eje Y tiene menos amplitud (los drones son más estables en altitud).
    // El yaw tiene poca influencia para no desorientar el encuadre.
    private void GetDroneNoise(float posAmp, float rotAmpDeg,
                               out Vector3 posOffset, out Quaternion rotOffset)
    {
        float t = _noiseTime * driftSpeed;

        // Posición: deriva lateral y profundidad; vertical más contenida
        float px = (Mathf.PerlinNoise(t              + _noiseSeedX, 0.00f) * 2f - 1f) * posAmp;
        float py = (Mathf.PerlinNoise(t + 2.1f       + _noiseSeedX, 0.50f) * 2f - 1f) * posAmp * 0.35f;
        float pz = (Mathf.PerlinNoise(t              + _noiseSeedZ, 1.00f) * 2f - 1f) * posAmp;

        // Rotación: pitch y roll dominan; yaw muy sutil para no girar el plano
        float pitch = (Mathf.PerlinNoise(t * 0.70f + _noiseSeedX, 3.0f) * 2f - 1f) * rotAmpDeg;
        float yaw   = (Mathf.PerlinNoise(t * 0.50f + _noiseSeedZ, 4.0f) * 2f - 1f) * rotAmpDeg * 0.20f;
        float roll  = (Mathf.PerlinNoise(t * 0.60f + _noiseSeedX, 5.0f) * 2f - 1f) * rotAmpDeg * 0.40f;

        posOffset = new Vector3(px, py, pz);
        rotOffset = Quaternion.Euler(pitch, yaw, roll);
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

    // ── Anillo de progreso ────────────────────────────────────────────────────
    private IEnumerator UpdateProgressRing(float totalDuration)
    {
        float elapsed = 0f;
        while (elapsed < totalDuration)
        {
            elapsed += Time.deltaTime;
            progressRing.fillAmount = Mathf.Clamp01(elapsed / totalDuration);
            yield return null;
        }
        progressRing.fillAmount = 1f;
    }

    // ── Loop mode — usado por SceneOverviewController ─────────────────────────

    /// <summary>
    /// Inicia la secuencia en bucle indefinido.
    /// No guarda/restaura posición del jugador, no muestra skip/progress/fade,
    /// no avanza etapa. El caller (SceneOverviewController) gestiona todo eso.
    /// Llama a StopLoop() para detenerlo.
    /// </summary>
    public void PlayLoop()
    {
        if (IsPlaying)
        {
            Debug.LogWarning("[CinematicSequencer] Ya hay una secuencia en curso. Ignorando PlayLoop.", this);
            return;
        }
        if (shots == null || shots.Length == 0)
        {
            Debug.LogWarning("[CinematicSequencer] shots[] vacío. No se puede iniciar bucle.", this);
            return;
        }

        _skipRequested = false;
        _cam           = Camera.main.transform;

        _noiseTime  = Random.Range(0f, 100f);
        _noiseSeedX = Random.Range(0f, 100f);
        _noiseSeedZ = Random.Range(0f, 100f);

        // Snap inmediato al primer anchor (el caller habrá hecho fade-to-black antes)
        if (shots[0].anchor != null)
        {
            _cam.position = shots[0].anchor.position;
            _cam.rotation = shots[0].anchor.rotation;
        }

        StartCoroutine(RunLoop());
    }

    /// <summary>Detiene el bucle en la próxima oportunidad. El caller gestiona el fade de salida.</summary>
    public void StopLoop() => _skipRequested = true;

    private IEnumerator RunLoop()
    {
        IsPlaying = true;
        int  shotIndex = 0;
        bool firstShot = true;   // primer shot ya fue posicionado en PlayLoop()

        while (!_skipRequested)
        {
            var shot = shots[shotIndex];

            if (shot.anchor == null)
            {
                shotIndex = (shotIndex + 1) % shots.Length;
                continue;
            }

            // Viajar al anchor — excepto el primero (ya posicionado)
            if (!firstShot)
                yield return StartCoroutine(MoveToAnchor(shot));
            firstShot = false;

            if (_skipRequested) break;

            // Hold en el anchor con deriva de dron
            float held = 0f;
            while (held < shot.holdDuration && !_skipRequested)
            {
                held       += Time.deltaTime;
                _noiseTime += Time.deltaTime;

                if (droneMode)
                {
                    GetDroneNoise(driftAmplitude, rotationDrift, out Vector3 dp, out Quaternion dr);
                    _cam.position = shot.anchor.position + dp;
                    _cam.rotation = shot.anchor.rotation * dr;
                }

                yield return null;
            }

            shotIndex = (shotIndex + 1) % shots.Length;
        }

        IsPlaying = false;
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

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireSphere(pos, 0.25f);
            Gizmos.DrawRay(pos, fwd * 1.2f);

            if (i < shots.Length - 1 && shots[i + 1].anchor != null)
            {
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
                Gizmos.DrawLine(pos, shots[i + 1].anchor.position);
            }

#if UNITY_EDITOR
            UnityEditor.Handles.Label(pos + Vector3.up * 0.4f,
                $"Shot {i + 1}\n{shots[i].moveDuration}s mov / {shots[i].holdDuration}s hold",
                new GUIStyle { fontSize = 9, normal = { textColor = new Color(0.2f, 0.9f, 1f) } });
#endif
        }
    }
}
