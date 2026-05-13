using System.Collections;
using UnityEngine;

/// <summary>
/// NPC que recorre hasta 4 paradas definidas en escena, una por nivel de riesgo.
///
/// Flujo por parada:
///   1. NPC espera en la parada N hasta que la etapa coincida con requiredStage.
///   2. El jugador puede interactuar → diálogo → ClosePanel().
///   3. El NPC sigue la ruta de la parada N+1 (waypoints intermedios → standPoint).
///   4. Al llegar, espera la siguiente etapa antes de ser interactuable.
///
/// Cada parada tiene su propia lista de waypoints intermedios para rodear obstáculos.
/// El standPoint es siempre el destino final de esa etapa.
///
/// Setup en Inspector:
///   stops[N].pathWaypoints[]  — Transforms vacíos que forman la ruta hasta standPoint.
///                               Dejar vacío si el camino es libre de obstáculos.
///   stops[N].standPoint       — Destino final donde el NPC espera e interactúa.
///   stops[N].requiredStage    — Etapa necesaria para que el jugador pueda hablar.
///   stops[N].dialogue         — NpcDialogueData de esa parada.
///   interactRadius ≥ 2.5      — La cámara está a ~1.7 m; NPCs a nivel de suelo necesitan margen.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class NPCStageWalker : MonoBehaviour, IHotspotInteractable
{
    // ── Config por parada ──────────────────────────────────────────────────────
    [System.Serializable]
    public class NPCStageStop
    {
        [Tooltip("Puntos intermedios para rodear obstáculos en el trayecto HACIA esta parada.\n" +
                 "Se recorren en orden antes de llegar al standPoint. Dejar vacío si el camino es directo.")]
        public Transform[] pathWaypoints;

        [Tooltip("Posición final donde el NPC se detiene e interactúa en esta parada.")]
        public Transform standPoint;

        [Tooltip("La etapa que debe estar activa para que el jugador pueda hablar aquí.")]
        public StageManager.Stage requiredStage;

        [Tooltip("ScriptableObject NpcDialogueData para el diálogo de esta parada.")]
        public NpcDialogueData dialogue;
    }

    // ── Inspector ──────────────────────────────────────────────────────────────
    [Header("Paradas (en orden)")]
    public NPCStageStop[] stops;

    [Header("Movimiento")]
    public float moveSpeed     = 1.2f;
    public float rotationSpeed = 5f;
    public float arrivalRadius = 0.4f;
    [SerializeField] private float gravity = -15f;

    [Header("Interacción")]
    [Tooltip("Radio en el que aparece el botón HUD. Usar ≥ 2.5 para NPCs al nivel del suelo.")]
    public float interactRadius = 2.5f;

    [Header("Animación")]
    [Tooltip("Nombre del parámetro Float del Animator (0 = idle, > 0 = caminar).")]
    public string speedParameter = "Speed";

    [Header("Al terminar la última parada")]
    [Tooltip("Si true, el NPC hace fade-out y se destruye tras el último diálogo.")]
    public bool disappearAfterFinalDialogue = false;
    public float disappearDelay   = 0.8f;
    public float fadeOutDuration  = 1.5f;

    // ── Estado ────────────────────────────────────────────────────────────────
    private enum WalkerState
    {
        WaitingForStage,   // En posición, etapa incorrecta — NO interactuable
        IdleInteractable,  // En posición, etapa correcta  — interactuable
        WalkingToNext,     // Recorriendo ruta hacia la siguiente parada
        Finished           // Último diálogo completado, NPC queda quieto
    }

    private WalkerState _state            = WalkerState.WaitingForStage;
    private int         _currentStop      = 0;
    private int         _pathWaypointIdx  = 0; // Índice dentro de stops[_currentStop].pathWaypoints

    // ── Componentes ───────────────────────────────────────────────────────────
    private CharacterController _cc;
    private Animator            _anim;
    private Transform           _playerCamera;

    // ── Proximidad ────────────────────────────────────────────────────────────
    private bool  _isNearby;
    private bool  _dialogueOpen;
    private float _verticalVel;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        _cc   = GetComponent<CharacterController>();
        _anim = GetComponent<Animator>();
        _anim.applyRootMotion = false;
    }

    private void Start()
    {
        if (Camera.main != null)
            _playerCamera = Camera.main.transform;

        if (stops == null || stops.Length == 0)
        {
            Debug.LogWarning("[NPCStageWalker] No hay paradas configuradas.", this);
            return;
        }

        SnapToStop(0);

        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged += OnStageChanged;

        EvaluateStateForCurrentStop(StageManager.Instance?.CurrentStage ?? StageManager.Stage.Intro);
    }

    private void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;
        HotspotPromptButton.Instance?.UnregisterHotspot(this);
    }

    private void OnDisable()
    {
        HotspotPromptButton.Instance?.UnregisterHotspot(this);
        _isNearby = false;
    }

    private void Update()
    {
        ApplyGravity();

        switch (_state)
        {
            case WalkerState.IdleInteractable:
                CheckProximity();
                break;
            case WalkerState.WalkingToNext:
                WalkStep();
                break;
        }
    }

    // ── IHotspotInteractable ──────────────────────────────────────────────────

    public void DispatchAction()
    {
        if (_state != WalkerState.IdleInteractable || _dialogueOpen) return;
        if (stops == null || _currentStop >= stops.Length) return;

        var stop = stops[_currentStop];
        if (stop.dialogue == null)
        {
            Debug.LogWarning($"[NPCStageWalker] La parada {_currentStop} no tiene NpcDialogueData.", this);
            return;
        }

        _dialogueOpen = true;
        HotspotPromptButton.Instance?.UnregisterHotspot(this);

        if (NpcDialoguePanel.Instance != null)
            NpcDialoguePanel.Instance.Show(stop.dialogue, this);
        else
            Debug.LogWarning("[NPCStageWalker] NpcDialoguePanel.Instance es null.", this);
    }

    public void ClosePanel()
    {
        _dialogueOpen = false;

        if (_currentStop >= stops.Length - 1)
        {
            // Última parada — terminar
            _state = WalkerState.Finished;
            SetAnimSpeed(0f);
            if (disappearAfterFinalDialogue)
                StartCoroutine(DisappearRoutine());
            return;
        }

        // Avanzar al siguiente stop y comenzar a caminar
        _currentStop++;
        _pathWaypointIdx = 0;
        _state = WalkerState.WalkingToNext;
        SetAnimSpeed(moveSpeed);
    }

    // ── Eventos de etapa ──────────────────────────────────────────────────────
    private void OnStageChanged(StageManager.Stage previous, StageManager.Stage current)
    {
        if (_state == WalkerState.WaitingForStage)
            EvaluateStateForCurrentStop(current);
    }

    private void EvaluateStateForCurrentStop(StageManager.Stage current)
    {
        if (stops == null || _currentStop >= stops.Length) return;
        if (_state == WalkerState.WalkingToNext || _state == WalkerState.Finished) return;

        bool match = ((int)current == (int)stops[_currentStop].requiredStage);

        if (match)
        {
            _state = WalkerState.IdleInteractable;
            SetAnimSpeed(0f);
            // Si el jugador ya estaba en rango, activar el botón sin esperar que se mueva
            CheckProximityImmediate();
        }
        else
        {
            if (_state == WalkerState.IdleInteractable)
            {
                HotspotPromptButton.Instance?.UnregisterHotspot(this);
                _isNearby = false;
            }
            _state = WalkerState.WaitingForStage;
            SetAnimSpeed(0f);
        }
    }

    private void CheckProximityImmediate()
    {
        if (_playerCamera == null) return;
        float dist = Vector3.Distance(_playerCamera.position, transform.position);
        if (dist <= interactRadius && !_isNearby)
        {
            _isNearby = true;
            HotspotPromptButton.Instance?.RegisterHotspot(this);
        }
    }

    // ── Movimiento con waypoints intermedios ──────────────────────────────────
    private void WalkStep()
    {
        if (stops == null || _currentStop >= stops.Length) return;

        Transform target = GetCurrentTarget();
        if (target == null) { OnArrived(); return; }

        Vector3 flatSelf   = FlatXZ(transform.position);
        Vector3 flatTarget = FlatXZ(target.position);
        float   dist       = Vector3.Distance(flatSelf, flatTarget);

        if (dist <= arrivalRadius)
        {
            // ¿Llegamos a un waypoint intermedio o al standPoint?
            var pathWps = stops[_currentStop].pathWaypoints;
            bool onWaypoint = pathWps != null && _pathWaypointIdx < pathWps.Length;

            if (onWaypoint)
                _pathWaypointIdx++; // Siguiente waypoint (o standPoint si ya no hay más)
            else
                OnArrived();        // Llegamos al standPoint

            return;
        }

        Vector3 dir = (flatTarget - flatSelf).normalized;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                rotationSpeed * Time.deltaTime);

        _cc.Move(dir * (moveSpeed * Time.deltaTime));
    }

    /// <summary>
    /// Devuelve el Transform al que el NPC debe dirigirse ahora mismo:
    /// primero agota los pathWaypoints y por último el standPoint.
    /// </summary>
    private Transform GetCurrentTarget()
    {
        var stop   = stops[_currentStop];
        var pathWps = stop.pathWaypoints;

        if (pathWps != null && _pathWaypointIdx < pathWps.Length)
            return pathWps[_pathWaypointIdx];

        return stop.standPoint;
    }

    private void OnArrived()
    {
        SetAnimSpeed(0f);

        // Snap exacto al standPoint para eliminar deriva de CharacterController
        var sp = stops[_currentStop].standPoint;
        if (sp != null)
        {
            _cc.enabled = false;
            transform.position = sp.position;
            _cc.enabled = true;
        }

        // Proximidad parte de cero en cada nueva parada
        _isNearby = false;

        // Evaluar si la etapa ya permite interacción o debe seguir esperando
        _state = WalkerState.WaitingForStage;
        EvaluateStateForCurrentStop(StageManager.Instance?.CurrentStage ?? StageManager.Stage.Intro);
    }

    // ── Proximidad ────────────────────────────────────────────────────────────
    private void CheckProximity()
    {
        if (_playerCamera == null || _dialogueOpen) return;

        float dist   = Vector3.Distance(_playerCamera.position, transform.position);
        bool  nearby = dist <= interactRadius;

        if (nearby && !_isNearby)
        {
            _isNearby = true;
            HotspotPromptButton.Instance?.RegisterHotspot(this);
        }
        else if (!nearby && _isNearby)
        {
            _isNearby = false;
            HotspotPromptButton.Instance?.UnregisterHotspot(this);
        }
    }

    // ── Gravedad ──────────────────────────────────────────────────────────────
    private void ApplyGravity()
    {
        _verticalVel = _cc.isGrounded ? -2f : _verticalVel + gravity * Time.deltaTime;
        _cc.Move(Vector3.up * (_verticalVel * Time.deltaTime));
    }

    // ── Snap inicial ──────────────────────────────────────────────────────────
    private void SnapToStop(int index)
    {
        if (stops == null || index >= stops.Length || stops[index].standPoint == null) return;
        _cc.enabled = false;
        transform.position = stops[index].standPoint.position;
        _cc.enabled = true;
    }

    // ── Fade-out final ────────────────────────────────────────────────────────
    private IEnumerator DisappearRoutine()
    {
        yield return new WaitForSeconds(disappearDelay);

        Renderer[]   renderers = GetComponentsInChildren<Renderer>();
        Material[][] groups    = new Material[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            groups[i] = renderers[i].materials;
            foreach (var m in groups[i]) SetToFadeMode(m);
        }

        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeOutDuration);
            foreach (var g in groups)
                foreach (var m in g)
                    if (m != null) { Color c = m.color; c.a = alpha; m.color = c; }
            yield return null;
        }

        Destroy(gameObject);
    }

    private static void SetToFadeMode(Material m)
    {
        if (m == null) return;
        m.SetFloat("_Mode", 2f);
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
        if (_anim != null && !string.IsNullOrEmpty(speedParameter))
            _anim.SetFloat(speedParameter, speed);
    }

    // ── Gizmos ───────────────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        if (stops == null) return;

        for (int i = 0; i < stops.Length; i++)
        {
            var stop = stops[i];
            if (stop == null) continue;

            // ── Waypoints intermedios ──────────────────────────────────────────
            var wps = stop.pathWaypoints;
            if (wps != null && wps.Length > 0)
            {
                // Línea desde el origen de la ruta (standPoint anterior o posición del NPC)
                Transform routeOrigin = (i == 0)
                    ? transform
                    : (stops[i - 1]?.standPoint != null ? stops[i - 1].standPoint : null);

                for (int j = 0; j < wps.Length; j++)
                {
                    if (wps[j] == null) continue;

                    Gizmos.color = new Color(1f, 0.6f, 0f); // naranja
                    Gizmos.DrawWireSphere(wps[j].position, 0.14f);

                    // Línea al waypoint anterior (u origen de ruta)
                    Transform prev = (j == 0) ? routeOrigin : wps[j - 1];
                    if (prev != null)
                    {
                        Gizmos.color = new Color(1f, 0.8f, 0f, 0.7f);
                        Gizmos.DrawLine(prev.position, wps[j].position);
                    }
                }

                // Línea del último waypoint al standPoint
                if (stop.standPoint != null && wps[wps.Length - 1] != null)
                {
                    Gizmos.color = new Color(1f, 0.8f, 0f, 0.7f);
                    Gizmos.DrawLine(wps[wps.Length - 1].position, stop.standPoint.position);
                }
            }
            else if (i > 0)
            {
                // Sin waypoints: línea directa desde standPoint anterior a este
                var prev = stops[i - 1]?.standPoint;
                if (prev != null && stop.standPoint != null)
                {
                    Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
                    Gizmos.DrawLine(prev.position, stop.standPoint.position);
                }
            }

            // ── StandPoint ────────────────────────────────────────────────────
            if (stop.standPoint == null) continue;

            bool isCurrent = Application.isPlaying && (i == _currentStop);
            bool isLast    = (i == stops.Length - 1);

            Gizmos.color = isCurrent ? Color.green
                         : isLast   ? Color.red
                         :            Color.cyan;

            Gizmos.DrawWireSphere(stop.standPoint.position, isCurrent ? 0.35f : 0.22f);
        }

        // Radio de interacción sobre la posición actual del NPC
        Gizmos.color = new Color(0f, 1f, 0f, 0.10f);
        Gizmos.DrawSphere(transform.position, interactRadius);
    }

    private static Vector3 FlatXZ(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
