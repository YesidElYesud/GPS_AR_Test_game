using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RiverDebrisController — Escombros y objetos flotando por la corriente del río.
///
/// Spawna objetos en el primer waypoint y los desplaza a lo largo de una ruta
/// definida por Transforms (equivalente a un spline lineal segmentado).
/// Cada objeto recibe una desviación lateral aleatoria en cada waypoint para
/// simular el movimiento natural de la corriente. Al llegar al último waypoint
/// el objeto se destruye.
///
/// Setup en escena:
///   1. Crear un GameObject vacío "RiverDebrisController" en la escena.
///   2. Adjuntar este script.
///   3. Crear hijos vacíos Waypoint_01, Waypoint_02 … y colocarlos a lo largo
///      del cauce. Arrastrarlos al array "waypoints" en el Inspector.
///   4. Asignar prefabs de escombros en "debrisPrefabs".
///   5. Configurar activateOnStage (ej. Etapa3) y ajustar parámetros.
///
/// Los gizmos muestran la ruta y el margen lateral en la vista de escena.
/// </summary>
public class RiverDebrisController : MonoBehaviour
{
    // ── Ruta ──────────────────────────────────────────────────────────────────
    [Header("Ruta (Waypoints)")]
    [Tooltip("Puntos que definen el cauce. Mínimo 2. Posicionar al nivel del agua.")]
    public Transform[] waypoints;

    // ── Prefabs ───────────────────────────────────────────────────────────────
    [Header("Objetos a spawnear")]
    [Tooltip("Lista de prefabs. Se elige uno aleatorio en cada spawn.")]
    public GameObject[] debrisPrefabs;

    // ── Activación ────────────────────────────────────────────────────────────
    [Header("Activación por etapa")]
    [Tooltip("Etapa en la que el sistema se activa automáticamente.")]
    public StageManager.Stage activateOnStage = StageManager.Stage.Etapa3;

    [Tooltip("Si se marca, el sistema se desactiva al avanzar a la siguiente etapa.")]
    public bool deactivateOnNextStage = false;

    // ── Spawning ──────────────────────────────────────────────────────────────
    [Header("Spawning")]
    [Tooltip("Segundos entre cada spawn.")]
    public float spawnInterval = 2f;

    [Tooltip("Máximo de objetos activos simultáneamente.")]
    [Range(1, 60)]
    public int maxDebrisCount = 8;

    [Tooltip("Si es true, spawnea una oleada inicial al activarse sin esperar el intervalo.")]
    public bool spawnBurstOnActivate = true;

    [Range(1, 10)]
    [Tooltip("Cantidad de objetos en la oleada inicial.")]
    public int burstCount = 3;

    // ── Movimiento ────────────────────────────────────────────────────────────
    [Header("Velocidad")]
    public float minSpeed = 1.5f;
    public float maxSpeed = 3.5f;

    [Header("Margen lateral")]
    [Tooltip("Desviación máxima perpendicular a la dirección del tramo (metros).")]
    public float lateralMargin = 0.4f;

    [Header("Evasión de obstáculos")]
    [Tooltip("Capas que se consideran obstáculo. La malla del agua no necesita excluirse: si no tiene Collider, el raycast la ignora automáticamente.")]
    public LayerMask obstacleLayerMask = Physics.DefaultRaycastLayers;
    [Range(0.3f, 3f)]
    [Tooltip("Distancia (m) a la que el escombro empieza a esquivar. 1.2–1.5 cubre rocas y bordes del puente.")]
    public float avoidanceRange = 1.2f;
    [Range(0f, 6f)]
    [Tooltip("Si el centro del colisionador detectado está a más de esta altura (m) sobre el escombro, se ignora: es un puente u obstáculo elevado. Los escombros pasan por debajo siguiendo los waypoints.")]
    public float bridgeClearance = 2f;

    [Header("Rotación y bamboleo")]
    [Tooltip("Si es true, el objeto gira gradualmente para orientarse en la dirección del flujo.")]
    public bool alignToFlow = true;

    [Tooltip("Velocidad de rotación (yaw) para alinearse al flujo (°/s).")]
    public float rotationSpeed = 90f;

    [Range(0f, 30f)]
    [Tooltip("Inclinación máxima inicial al spawnear (°). 0 = perfectamente plano, 15 = ligeramente volcado.")]
    public float initialTiltRange = 12f;

    [Range(0f, 25f)]
    [Tooltip("Amplitud máxima del bamboleo (°). Simula la turbulencia de la corriente.")]
    public float wobbleAmplitude = 9f;

    [Range(0.1f, 3f)]
    [Tooltip("Velocidad del bamboleo (Perlin noise). Valores bajos = movimiento lento y orgánico.")]
    public float wobbleSpeed = 0.7f;

    // ── Estado interno ────────────────────────────────────────────────────────
    private List<GameObject> _activeDebris = new List<GameObject>();
    private bool _isActive = false;
    private Coroutine _spawnCoroutine;

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    // Start() en lugar de OnEnable(): en Start() todos los Awake() ya corrieron,
    // garantizando que StageManager.Instance no sea null al suscribirse.
    void Start()
    {
        if (StageManager.Instance == null)
        {
            Debug.LogWarning("[RiverDebrisController] StageManager.Instance es null en Start. " +
                             "Los escombros no responderán a cambios de etapa.", this);
            return;
        }

        StageManager.Instance.OnStageChanged += OnStageChanged;

        // Aplicar la etapa actual de inmediato — sin esperar el próximo cambio.
        var current = StageManager.Instance.CurrentStage;
        if (ShouldBeActive(current) && !_isActive)
            Activate();
        else if (!ShouldBeActive(current) && _isActive)
            Deactivate();
    }

    void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;
    }

    // ── Reacción al cambio de etapa ───────────────────────────────────────────
    void OnStageChanged(StageManager.Stage prev, StageManager.Stage next)
    {
        // ShouldBeActive cubre saltos de etapa (ej: N1→N4 también activa los escombros
        // aunque se haya saltado la etapa exacta de activación).
        if (ShouldBeActive(next) && !_isActive)
            Activate();
        else if (!ShouldBeActive(next) && _isActive)
            Deactivate();
    }

    /// <summary>
    /// Devuelve true si el sistema de escombros debe estar activo en la etapa dada.
    /// Activo cuando stage >= activateOnStage, a menos que deactivateOnNextStage
    /// exija que solo esté activo en la etapa exacta de activación.
    /// </summary>
    private bool ShouldBeActive(StageManager.Stage stage)
    {
        bool reachedActivation = (int)stage >= (int)activateOnStage;
        bool stillActive       = !deactivateOnNextStage || stage == activateOnStage;
        return reachedActivation && stillActive;
    }

    // ── API pública ───────────────────────────────────────────────────────────
    public bool IsActive => _isActive;

    public void Activate()
    {
        if (_isActive) return;
        _isActive = true;

        if (spawnBurstOnActivate)
        {
            int burst = Mathf.Min(burstCount, maxDebrisCount);
            for (int i = 0; i < burst; i++)
                SpawnDebris();
        }

        _spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    public void Deactivate()
    {
        if (!_isActive) return;
        _isActive = false;

        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }

        foreach (var d in _activeDebris)
            if (d != null) Destroy(d);

        _activeDebris.Clear();
    }

    // ── Spawn loop ────────────────────────────────────────────────────────────
    IEnumerator SpawnLoop()
    {
        while (_isActive)
        {
            yield return new WaitForSeconds(spawnInterval);

            _activeDebris.RemoveAll(d => d == null);

            if (_activeDebris.Count < maxDebrisCount)
                SpawnDebris();
        }
    }

    void SpawnDebris()
    {
        if (debrisPrefabs == null || debrisPrefabs.Length == 0) return;
        if (waypoints == null || waypoints.Length < 2) return;

        GameObject prefab = debrisPrefabs[Random.Range(0, debrisPrefabs.Length)];
        if (prefab == null) return;

        Vector3 spawnPos = GetWaypointWithLateral(0, 1);

        // Yaw completamente aleatorio + pequeña inclinación inicial en XZ
        Quaternion spawnRot = Quaternion.Euler(
            Random.Range(-initialTiltRange, initialTiltRange),
            Random.Range(0f, 360f),
            Random.Range(-initialTiltRange, initialTiltRange));

        GameObject obj = Instantiate(prefab, spawnPos, spawnRot);

        float speed = Random.Range(minSpeed, maxSpeed);
        RiverDebrisObject debrisObj = obj.AddComponent<RiverDebrisObject>();
        debrisObj.Initialize(waypoints, speed, lateralMargin, alignToFlow, rotationSpeed,
                             wobbleAmplitude, wobbleSpeed, obstacleLayerMask, avoidanceRange, bridgeClearance);

        _activeDebris.Add(obj);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// Devuelve la posición del waypoint[index] con offset lateral aleatorio.
    /// nextIndex se usa para calcular la dirección del tramo y el eje lateral.
    Vector3 GetWaypointWithLateral(int index, int nextIndex)
    {
        Vector3 pos = waypoints[index].position;
        Vector3 next = waypoints[Mathf.Clamp(nextIndex, 0, waypoints.Length - 1)].position;
        Vector3 flow = (next - pos);
        flow.y = 0f;
        if (flow.sqrMagnitude < 0.0001f) return pos;

        Vector3 lateral = Vector3.Cross(flow.normalized, Vector3.up);
        float offset = Random.Range(-lateralMargin, lateralMargin);
        return pos + lateral * offset;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────
    void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Length < 2) return;

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;

            // Nodo
            Gizmos.color = (i == 0) ? Color.green : (i == waypoints.Length - 1 ? Color.red : Color.cyan);
            Gizmos.DrawWireSphere(waypoints[i].position, 0.25f);

            // Línea al siguiente
            if (i < waypoints.Length - 1 && waypoints[i + 1] != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);

                // Margen lateral
                Vector3 flow = (waypoints[i + 1].position - waypoints[i].position);
                flow.y = 0f;
                if (flow.sqrMagnitude > 0.0001f)
                {
                    Vector3 lateral = Vector3.Cross(flow.normalized, Vector3.up) * lateralMargin;
                    Vector3 mid = Vector3.Lerp(waypoints[i].position, waypoints[i + 1].position, 0.5f);
                    Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
                    Gizmos.DrawLine(mid - lateral, mid + lateral);
                }
            }
        }

        // Etiqueta inicio/fin
#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.green;
        if (waypoints[0] != null)
            UnityEditor.Handles.Label(waypoints[0].position + Vector3.up * 0.5f, "SPAWN");
        if (waypoints[waypoints.Length - 1] != null)
            UnityEditor.Handles.Label(waypoints[waypoints.Length - 1].position + Vector3.up * 0.5f, "FIN");
#endif
    }
}

// ── RiverDebrisObject ─────────────────────────────────────────────────────────
/// Componente añadido en runtime a cada objeto instanciado.
/// Gestiona movimiento por waypoints + bamboleo orgánico + evasión de obstáculos.
public class RiverDebrisObject : MonoBehaviour
{
    private Transform[] _waypoints;
    private float _speed;
    private float _lateralMargin;
    private bool  _alignToFlow;
    private float _rotationSpeed;
    private float _wobbleAmplitude;
    private float _wobbleSpeed;
    private LayerMask _obstacleMask;
    private float     _avoidRange;
    private float     _bridgeClearance;

    // Semillas independientes para cada eje → movimiento no periódico ni sincronizado
    private float _seedX;
    private float _seedZ;

    private int     _targetIndex;
    private Vector3 _targetPos;
    private const float _waypointReachDist = 0.3f;

    public void Initialize(Transform[] waypoints, float speed, float lateralMargin,
                           bool alignToFlow, float rotationSpeed,
                           float wobbleAmplitude, float wobbleSpeed,
                           LayerMask obstacleMask, float avoidRange, float bridgeClearance)
    {
        _waypoints       = waypoints;
        _speed           = speed;
        _lateralMargin   = lateralMargin;
        _alignToFlow     = alignToFlow;
        _rotationSpeed   = rotationSpeed;
        _wobbleAmplitude = wobbleAmplitude;
        _wobbleSpeed     = wobbleSpeed;
        _obstacleMask    = obstacleMask;
        _avoidRange      = avoidRange;
        _bridgeClearance = bridgeClearance;

        // Semillas aleatorias: cada objeto bambolea diferente
        _seedX = Random.Range(0f, 100f);
        _seedZ = Random.Range(0f, 100f);

        _targetIndex = 1;
        _targetPos   = ComputeTarget(_targetIndex);
    }

    void Update()
    {
        if (_waypoints == null || _targetIndex >= _waypoints.Length) return;

        Vector3 toTarget = _targetPos - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude < _waypointReachDist)
        {
            _targetIndex++;
            if (_targetIndex >= _waypoints.Length)
            {
                Destroy(gameObject);
                return;
            }
            _targetPos = ComputeTarget(_targetIndex);
            toTarget   = _targetPos - transform.position;
            toTarget.y = 0f;
        }

        // Dirección de movimiento: XZ con evasión de obstáculos, Y preserva la
        // pendiente real del cauce (componente Y de la dirección al waypoint).
        Vector3 moveDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;
        moveDir = ApplyObstacleAvoidance(moveDir);

        Vector3 toTarget3D = _targetPos - transform.position;
        float ySlope = toTarget3D.sqrMagnitude > 0.0001f ? toTarget3D.normalized.y : 0f;
        Vector3 dir3D = new Vector3(moveDir.x, ySlope, moveDir.z).normalized;
        transform.position += dir3D * (_speed * Time.deltaTime);

        // ── Rotación: flujo + bamboleo ────────────────────────────────────────

        // 1. Yaw base = dirección 3D real (incluye evasión y pendiente del cauce)
        Quaternion flowRot = _alignToFlow && dir3D.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(dir3D)
            : Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        // 2. Bamboleo: ruido Perlin en pitch (X) y roll (Z) independientes
        float noiseX = Mathf.PerlinNoise(_seedX + Time.time * _wobbleSpeed, 0f) * 2f - 1f;
        float noiseZ = Mathf.PerlinNoise(0f, _seedZ + Time.time * _wobbleSpeed) * 2f - 1f;
        Quaternion wobble = Quaternion.Euler(
            noiseX * _wobbleAmplitude,
            0f,
            noiseZ * _wobbleAmplitude);

        // 3. Combinar: el bamboleo se aplica en el espacio local del flujo
        Quaternion targetRot = flowRot * wobble;
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRot, _rotationSpeed * Time.deltaTime);
    }

    // Desvía moveDir perpendicular al flujo si hay un obstáculo adelante.
    // Elige el lado más despejado con dos raycasts diagonales.
    // La malla del agua no interfiere: si no tiene Collider, el raycast la ignora.
    private Vector3 ApplyObstacleAvoidance(Vector3 moveDir)
    {
        if (_avoidRange <= 0f || moveDir.sqrMagnitude < 0.01f) return moveDir;

        // Origen elevado 0.4 m para evitar raycasts dentro del propio collider del escombro
        Vector3 origin = transform.position + Vector3.up * 0.4f;

        if (!Physics.Raycast(origin, moveDir, out RaycastHit hit, _avoidRange, _obstacleMask))
            return moveDir;

        // Estructura elevada (puente, voladizo): el centro del colisionador
        // está significativamente más alto que el escombro → pasar por debajo,
        // no esquivar hacia los lados.
        if (hit.collider.bounds.center.y - transform.position.y > _bridgeClearance)
            return moveDir;

        // Obstáculo al nivel del agua (roca, pared): decidir lado con dos raycasts diagonales
        Vector3 right = Vector3.Cross(Vector3.up, moveDir).normalized;
        bool rightFree = !Physics.Raycast(origin, (moveDir + right * 0.8f).normalized,
                                          _avoidRange * 0.75f, _obstacleMask);
        float side    = rightFree ? 1f : -1f;

        // Urgencia aumenta cuanto más cerca está el obstáculo
        float urgency = 1f - Mathf.Clamp01(hit.distance / _avoidRange);
        return (moveDir + right * (side * urgency * 1.8f)).normalized;
    }

    Vector3 ComputeTarget(int index)
    {
        Vector3 pos = _waypoints[index].position;

        int prevIdx = Mathf.Max(index - 1, 0);
        Vector3 flow = pos - _waypoints[prevIdx].position;
        flow.y = 0f;

        if (flow.sqrMagnitude < 0.0001f && index + 1 < _waypoints.Length)
            flow = _waypoints[index + 1].position - pos;
        flow.y = 0f;

        if (flow.sqrMagnitude < 0.0001f) return pos;

        Vector3 lateral = Vector3.Cross(flow.normalized, Vector3.up);
        float offset = Random.Range(-_lateralMargin, _lateralMargin);
        return pos + lateral * offset;
    }
}
