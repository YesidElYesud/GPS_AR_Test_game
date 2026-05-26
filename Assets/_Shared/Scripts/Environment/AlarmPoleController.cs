using System.Collections;
using UnityEngine;

/// <summary>
/// AlarmPoleController — Alarma 3D espacial emitida desde un poste.
///
/// Adjuntar a cada Poste_low que deba sonar. Cada poste tiene su propio
/// AudioSource 3D → Unity atenúa automáticamente el volumen según la
/// distancia del jugador.
///
/// Soporta dos clips distintos: uno para Nivel de Riesgo 3 (Etapa3) y otro
/// para Nivel de Riesgo 4 (Etapa4). Al bajar de nivel la alarma se apaga.
/// Al cambiar entre N3 y N4 hace un fade-out rápido, cambia el clip y hace
/// fade-in del nuevo clip.
///
/// Setup:
///   1. Adjuntar este script a Poste_low.001 y a Poste_low.002.
///   2. Asignar los clips en Alarm Clip N3 y Alarm Clip N4.
///   3. Ajustar Min Distance y Max Distance según el tamaño de la escena.
///   4. Unity añade AudioSource automáticamente.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AlarmPoleController : MonoBehaviour
{
    // ── Clips por nivel de riesgo ─────────────────────────────────────────────
    [Header("Sonido por Nivel de Riesgo")]
    [Tooltip("Clip que suena en bucle cuando el riesgo es N3 (Etapa3).")]
    public AudioClip alarmClipN3;

    [Tooltip("Clip que suena en bucle cuando el riesgo es N4 (Etapa4).")]
    public AudioClip alarmClipN4;

    // ── Audio 3D ──────────────────────────────────────────────────────────────
    [Header("Audio 3D")]
    [Tooltip("Distancia (m) a la que el sonido suena al 100% de volumen.")]
    [Range(1f, 20f)]
    public float minDistance = 5f;

    [Tooltip("Distancia (m) a partir de la cual el sonido se vuelve inaudible.")]
    [Range(10f, 100f)]
    public float maxDistance = 40f;

    // ── Fade ──────────────────────────────────────────────────────────────────
    [Header("Fade")]
    [Range(0f, 4f)]
    [Tooltip("Segundos de fade-in al activarse.")]
    public float fadeInDuration = 1.5f;

    [Range(0f, 4f)]
    [Tooltip("Segundos de fade-out antes de detenerse.")]
    public float fadeOutDuration = 2.5f;

    [Range(0.1f, 2f)]
    [Tooltip("Segundos de fade-out rápido al cambiar de clip (N3↔N4).")]
    public float clipSwitchFadeDuration = 0.5f;

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
    [Tooltip("Velocidad del ciclo de ulular (ciclos/segundo). 0.2 = lento, 0.8 = rápido.")]
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

        float t = Mathf.Sin(Time.time * sirenSpeed * Mathf.PI * 2f) * 0.5f + 0.5f;
        _source.pitch = Mathf.Lerp(sirenPitchMin, sirenPitchMax, t);
    }

    // ── Reacción a cambio de etapa ────────────────────────────────────────────
    private void OnStageChanged(StageManager.Stage prev, StageManager.Stage next)
    {
        if (next == StageManager.Stage.Etapa3)
            SwitchToClip(alarmClipN3);
        else if (next == StageManager.Stage.Etapa4)
            SwitchToClip(alarmClipN4);
        else if (_isActive)
            Deactivate();
    }

    // ── Lógica de clip ────────────────────────────────────────────────────────
    private void SwitchToClip(AudioClip clip)
    {
        if (clip == null)
        {
            if (_isActive) Deactivate();
            return;
        }

        if (!_isActive)
        {
            // Primera activación: asignar clip y hacer fade-in normal
            _source.clip = clip;
            Activate();
            return;
        }

        if (_source.clip == clip) return; // ya suena el clip correcto

        // Cambio entre N3 y N4: fade-out rápido → swap → fade-in
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(SwitchClipRoutine(clip));
    }

    private IEnumerator SwitchClipRoutine(AudioClip newClip)
    {
        // Fade-out rápido del clip actual
        float startVol = _source.volume;
        float elapsed  = 0f;
        while (elapsed < clipSwitchFadeDuration)
        {
            elapsed += Time.deltaTime;
            _source.volume = Mathf.Lerp(startVol, 0f,
                Mathf.Clamp01(elapsed / clipSwitchFadeDuration));
            yield return null;
        }
        _source.volume = 0f;
        _source.Stop();

        // Swap de clip
        _source.clip = newClip;
        _source.pitch  = 1f;
        _source.volume = 0f;
        _source.Play();

        // Fade-in del nuevo clip
        elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            _source.volume = Mathf.Lerp(0f, 1f,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeInDuration)));
            yield return null;
        }
        _source.volume = 1f;
        _fadeRoutine   = null;
    }

    // ── API pública ───────────────────────────────────────────────────────────
    public void Activate()
    {
        if (_isActive) return;
        _isActive = true;

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);

        _source.volume = 0f;
        _source.Play();
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
        _source.clip         = null;
        _source.loop         = true;
        _source.playOnAwake  = false;
        _source.volume       = 0f;
        _source.pitch        = 1f;
        _source.dopplerLevel = 0f;
        _source.spatialBlend = 1f;
        _source.rolloffMode  = AudioRolloffMode.Logarithmic;
        _source.minDistance  = minDistance;
        _source.maxDistance  = maxDistance;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        var src = GetComponent<AudioSource>();
        if (src == null) return;
        src.spatialBlend = 1f;
        src.minDistance  = minDistance;
        src.maxDistance  = maxDistance;
        src.rolloffMode  = AudioRolloffMode.Logarithmic;
        src.dopplerLevel = 0f;
    }
#endif
}
