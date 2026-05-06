using System.Collections;
using UnityEngine;

/// <summary>
/// NPCWaypointWalker — Movimiento autónomo de NPC por waypoints tras un diálogo correcto.
///
/// Flujo:
///   1. NpcDialoguePanel llama StartWalking() al obtener respuesta correcta.
///   2. El NPC busca el waypoint más cercano del array y sigue la secuencia en orden.
///   3. Al llegar al último waypoint, hace fade-out y se destruye.
///
/// Requiere: CharacterController + Animator en el mismo GameObject.
/// Root Motion del Animator se desactiva automáticamente (Mixamo lo usa y pelea con el CC).
///
/// Setup en Inspector:
///   - waypoints[]: arreglo de Transforms vacíos colocados en la escena como ruta.
///   - speedParameter: nombre del Float en el Animator Controller (default "Speed").
///   - moveSpeed: ~1.2 para Mixamo "Standard Walk".
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class NPCWaypointWalker : MonoBehaviour
{
    [Header("Ruta")]
    [Tooltip("Puntos en escena que definen la ruta. El NPC irá al más cercano primero y seguirá en orden.")]
    [SerializeField] private Transform[] waypoints;

    [Header("Movimiento")]
    [SerializeField] private float moveSpeed         = 1.2f;
    [SerializeField] private float waypointRadius    = 0.5f;
    [SerializeField] private float rotationSpeed     = 5f;
    [SerializeField] private float gravity           = -15f;

    [Header("Animator")]
    [Tooltip("Nombre del parámetro Float en el Animator Controller que controla la velocidad.")]
    [SerializeField] private string speedParameter   = "Speed";

    [Header("Desaparición al final")]
    [SerializeField] private float disappearDelay    = 0.8f;
    [SerializeField] private float fadeOutDuration   = 1.5f;

    // ── Estado interno ────────────────────────────────────────────────────────
    private CharacterController _cc;
    private Animator            _animator;
    private int                 _targetIndex = -1;
    private bool                _isWalking   = false;
    private float               _verticalVel = 0f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        _cc                     = GetComponent<CharacterController>();
        _animator               = GetComponent<Animator>();
        _animator.applyRootMotion = false;
    }

    private void Update()
    {
        ApplyGravity();
        if (_isWalking) WalkStep();
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>Inicia el recorrido. Conectado desde NpcDialoguePanel via HotspotController.</summary>
    public void StartWalking()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            Debug.LogWarning("[NPCWaypointWalker] Sin waypoints asignados.", this);
            return;
        }

        _targetIndex = FindNearestWaypointIndex();
        _isWalking   = true;
        SetAnimSpeed(moveSpeed);
    }

    // ── Gravedad ──────────────────────────────────────────────────────────────
    private void ApplyGravity()
    {
        if (_cc.isGrounded)
            _verticalVel = -2f;
        else
            _verticalVel += gravity * Time.deltaTime;

        _cc.Move(Vector3.up * (_verticalVel * Time.deltaTime));
    }

    // ── Paso de caminado ──────────────────────────────────────────────────────
    private void WalkStep()
    {
        if (_targetIndex < 0 || _targetIndex >= waypoints.Length)
        {
            FinishWalk();
            return;
        }

        Transform wp = waypoints[_targetIndex];
        if (wp == null) { _targetIndex++; return; }

        // Proyectar en plano XZ para no escalar cuestas con la dirección
        Vector3 flatSelf   = FlatXZ(transform.position);
        Vector3 flatTarget = FlatXZ(wp.position);
        Vector3 dir        = (flatTarget - flatSelf).normalized;
        float   dist       = Vector3.Distance(flatSelf, flatTarget);

        // Rotación suave hacia el destino
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                rotationSpeed * Time.deltaTime);

        // Desplazamiento horizontal (la gravedad ya agrega el componente Y en ApplyGravity)
        _cc.Move(dir * (moveSpeed * Time.deltaTime));

        if (dist <= waypointRadius)
        {
            _targetIndex++;
            if (_targetIndex >= waypoints.Length)
                FinishWalk();
        }
    }

    // ── Llegó al final ────────────────────────────────────────────────────────
    private void FinishWalk()
    {
        _isWalking = false;
        SetAnimSpeed(0f);
        StartCoroutine(DisappearRoutine());
    }

    // ── Waypoint más cercano ──────────────────────────────────────────────────
    private int FindNearestWaypointIndex()
    {
        int   nearest = 0;
        float minDist = float.MaxValue;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            float d = Vector3.Distance(transform.position, waypoints[i].position);
            if (d < minDist) { minDist = d; nearest = i; }
        }
        return nearest;
    }

    // ── Fade out y destrucción ────────────────────────────────────────────────
    private IEnumerator DisappearRoutine()
    {
        yield return new WaitForSeconds(disappearDelay);

        Renderer[] renderers = GetComponentsInChildren<Renderer>();

        // Instanciar materiales y cambiar a modo Fade (sin tocar el asset original)
        Material[][] matGroups = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            matGroups[i] = renderers[i].materials; // Unity crea instancias al usar .materials
            foreach (Material m in matGroups[i])
                SetStandardToFade(m);
        }

        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeOutDuration);
            foreach (var group in matGroups)
                foreach (var m in group)
                    if (m != null) { Color c = m.color; c.a = alpha; m.color = c; }
            yield return null;
        }

        Destroy(gameObject);
    }

    private void SetStandardToFade(Material m)
    {
        if (m == null) return;
        m.SetFloat("_Mode", 2);
        m.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite",    0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
    }

    // ── Animator ──────────────────────────────────────────────────────────────
    private void SetAnimSpeed(float speed)
    {
        if (_animator != null && !string.IsNullOrEmpty(speedParameter))
            _animator.SetFloat(speedParameter, speed);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            bool isLast = i == waypoints.Length - 1;
            Gizmos.color = isLast ? Color.red : Color.cyan;
            Gizmos.DrawSphere(waypoints[i].position, isLast ? 0.35f : 0.2f);
            if (!isLast && waypoints[i + 1] != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
            }
        }
    }

    // ── Util ──────────────────────────────────────────────────────────────────
    private static Vector3 FlatXZ(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
