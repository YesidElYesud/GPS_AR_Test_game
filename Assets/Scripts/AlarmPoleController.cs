using System.Collections;
using UnityEngine;

/// <summary>
/// AlarmPoleController — Alarma 3D espacial emitida desde un poste.
///
/// Adjuntar a cada Poste_low que deba sonar. Cada poste tiene su propio
/// AudioSource 3D → Unity atenúa automáticamente el volumen según la
/// distancia del jugador. Al alejarse del poste el sonido baja; al acercarse
/// sube, generando la sensación de proximidad real.
///
/// El efecto "sirena" modula el pitch con una onda seno suave para imitar
/// el ulular de una alarma civil de emergencia.
///
/// Setup:
///   1. Adjuntar este script a Poste_low.001 y a Poste_low.002.
///   2. Asignar 'security-alarm.mp3' al campo Alarm Clip.
///   3. Ajustar Min Distance y Max Distance según el tamaño de la escena.
///   4. Unity añade AudioSource automáticamente.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AlarmPoleController : MonoBehaviour
{
    // ── Clip ──────────────────────────────────────────────────────────────────
    [Header("Sonido")]
    [Tooltip("Clip de alarma. Asignar 'security-alarm.mp3'.")]
    public AudioClip alarmClip;

    // ── Audio 3D ──────────────────────────────────────────────────────────────
    [Header("Audio 3D")]
    [Tooltip("Distancia (m) a la que el sonido suena al 100% de volumen.")]
    [Range(1f, 20f)]
    public float minDistance = 5f;

    [Tooltip("Distancia (m) a partir de la cual el sonido se vuelve inaudible.")]
    [Range(10f, 100f)]
    public float maxDistance = 40f;

    // ── Activación ────────────────────────────────────────────────────────────
    [Header("Activación por etapa")]
    [Tooltip("Etapa desde la que la alarma se activa (inclusive).")]
    public StageManager.Stage activateFromStage = StageManager.Stage.Etapa4;

    [Tooltip("Etapa hasta la que la alarma permanece activa (inclusive).")]
    public StageManager.Stage activateUntilStage = StageManager.Stage.Etapa4;

    // ── Fade ──────────────────────────────────────────────────────────────────
    [Header("Fade")]
    [Range(0f, 4f)]
    [Tooltip("Segundos de fade-in al activarse.")]
    public float fadeInDuration = 1.5f;

    [Range(0f, 4f)]
    [Tooltip("Segundos de fade-out antes de detenerse.")]
    public float fadeOutDuration = 2.5f;

    // ── Efecto sirena ─────────────────────────────────────────────────────────
    [Header("Efecto sirena (pitch)")]
    [Tooltip("Modula el pitch para imitar el ulular de una sirena de emergencia.")]
    public bool sirenEffect = true;

    [Range(0.75f, 1.0f)]
    [Tooltip("Pitch mínimo de la sirena.")]
    public float sirenPitchMin = 0.88f;

    [Range(1.0f, 1.30f)]
    [Tooltip("Pitch máximo de la sirena.")]
    public float sirenPitchMax = 1.12f;

    [Range(0.05f, 1f)]
    [Tooltip("Velocidad del ciclo de ulular (ciclos/segundo). 0.2 = ulular lento, 0.8 = rápido.")]
    public float sirenSpeed = 0.22f;

    // ── Internos ──────────────────────────────────────────────────────────────
    private AudioSource _source;
    private Coroutine   _fadeRoutine;
    private bool        _isActive;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        ConfigureAudioSource();
    }

    private void Start()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged += OnStageChanged;
    }

    private void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;
    }

    private void Update()
    {
        if (!_isActive || !sirenEffect) return;

        // Onda seno suave: pitch sube y baja imitando sirena
        float t = Mathf.Sin(Time.time * sirenSpeed * Mathf.PI * 2f) * 0.5f + 0.5f;
        _source.pitch = Mathf.Lerp(sirenPitchMin, sirenPitchMax, t);
    }

    // ── Reacción a cambio de etapa ────────────────────────────────────────────
    private void OnStageChanged(StageManager.Stage prev, StageManager.Stage next)
    {
        bool inRange = (int)next >= (int)activateFromStage &&
                       (int)next <= (int)activateUntilStage;

        if (inRange)
            Activate();
        else if (_isActive)
            Deactivate();
    }

    // ── API pública ───────────────────────────────────────────────────────────
    public void Activate()
    {
        if (_isActive) return;
        _isActive = true;

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);

        if (!_source.isPlaying)
        {
            _source.volume = 0f;
            _source.Play();
        }

        _fadeRoutine = StartCoroutine(FadeIn());
    }

    public void Deactivate()
    {
        if (!_isActive) return;
        _isActive = false;

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeOutAndStop());
    }

    // ── Corrutinas de fade ────────────────────────────────────────────────────
    private IEnumerator FadeIn()
    {
        float start   = _source.volume;
        float elapsed = 0f;

        while (elapsed < fadeInDuration)
        {
            elapsed      += Time.deltaTime;
            _source.volume = Mathf.Lerp(start, 1f,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeInDuration)));
            yield return null;
        }

        _source.volume = 1f;
        _fadeRoutine   = null;
    }

    private IEnumerator FadeOutAndStop()
    {
        // Pitch de vuelta a neutro durante el fade-out
        _source.pitch = 1f;

        float start   = _source.volume;
        float elapsed = 0f;

        while (elapsed < fadeOutDuration)
        {
            elapsed      += Time.deltaTime;
            _source.volume = Mathf.Lerp(start, 0f,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeOutDuration)));
            yield return null;
        }

        _source.volume = 0f;
        _source.Stop();
        _fadeRoutine   = null;
    }

    // ── Configuración del AudioSource ─────────────────────────────────────────
    private void ConfigureAudioSource()
    {
        _source.clip          = alarmClip;
        _source.loop          = true;
        _source.playOnAwake   = false;
        _source.volume        = 0f;
        _source.pitch         = 1f;
        _source.dopplerLevel  = 0f;               // el poste no se mueve, sin doppler
        _source.spatialBlend  = 1f;               // 100% 3D espacial
        _source.rolloffMode   = AudioRolloffMode.Logarithmic; // atenuación natural
        _source.minDistance   = minDistance;
        _source.maxDistance   = maxDistance;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Refleja cambios del Inspector en tiempo real (sin Play)
        var src = GetComponent<AudioSource>();
        if (src == null) return;
        src.spatialBlend = 1f;
        src.minDistance  = minDistance;
        src.maxDistance  = maxDistance;
        src.rolloffMode  = AudioRolloffMode.Logarithmic;
        src.dopplerLevel = 0f;
        if (alarmClip != null) src.clip = alarmClip;
    }
#endif
}
