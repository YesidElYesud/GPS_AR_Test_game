using System.Collections;
using UnityEngine;

/// <summary>
/// Reproduce aleatoriamente clips de animación idle sobre un Animator.
/// Al terminar cada clip elige uno al azar (puede repetir o evitar repetir).
/// </summary>
[RequireComponent(typeof(Animator))]
public class RandomIdleAnimator : MonoBehaviour
{
    [Tooltip("Clips idle a rotar aleatoriamente. Asigna los 3 clips de Mixamo.")]
    [SerializeField] private AnimationClip[] idleClips;

    [Tooltip("Si true, nunca repite el mismo clip dos veces seguidas.")]
    [SerializeField] private bool avoidRepeat = true;

    [Tooltip("Pausa mínima en segundos entre clips (0 = sin pausa).")]
    [SerializeField, Min(0f)] private float minPauseBetweenClips = 0f;

    [Tooltip("Pausa máxima en segundos entre clips.")]
    [SerializeField, Min(0f)] private float maxPauseBetweenClips = 1.5f;

    private Animator _anim;
    private int      _lastIndex = -1;

    private void Awake() => _anim = GetComponent<Animator>();

    private void Start()
    {
        if (idleClips == null || idleClips.Length == 0)
        {
            Debug.LogWarning($"[RandomIdleAnimator] '{gameObject.name}': no hay clips asignados.", this);
            return;
        }
        StartCoroutine(IdleLoop());
    }

    private IEnumerator IdleLoop()
    {
        while (true)
        {
            int idx = PickIndex();
            _lastIndex = idx;

            AnimationClip clip = idleClips[idx];
            _anim.Play(clip.name, 0, 0f);

            // Esperar a que el clip termine
            yield return null;  // un frame para que el Animator registre el Play
            yield return new WaitForSeconds(clip.length);

            // Pausa opcional antes del siguiente clip
            float pause = Random.Range(minPauseBetweenClips, maxPauseBetweenClips);
            if (pause > 0f)
                yield return new WaitForSeconds(pause);
        }
    }

    private int PickIndex()
    {
        if (idleClips.Length == 1) return 0;

        int idx;
        do { idx = Random.Range(0, idleClips.Length); }
        while (avoidRepeat && idx == _lastIndex);
        return idx;
    }
}
