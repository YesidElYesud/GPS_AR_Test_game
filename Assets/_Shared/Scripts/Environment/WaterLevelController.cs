using System.Collections;
using UnityEngine;

public class WaterLevelController : MonoBehaviour
{
    [System.Serializable]
    public class StageWaterLevel
    {
        public StageManager.Stage stage;
        [Tooltip("localPosition destino de la malla de agua")]
        public Vector3 targetPosition;
        [Tooltip("Segundos hasta alcanzar esa posición")]
        public float transitionDuration = 4f;
    }

    [Header("Referencias")]
    [SerializeField] private Transform waterMesh;

    [Header("Niveles por etapa")]
    [SerializeField] private StageWaterLevel[] stageConfigs;

    private Coroutine _transition;

    private void Start()
    {
        if (waterMesh == null)
        {
            Debug.LogWarning("[WaterLevelController] waterMesh no asignado. Asigna la malla del río en el Inspector.", this);
            return;
        }

        if (StageManager.Instance != null)
        {
            StageManager.Instance.OnStageChanged += OnStageChanged;
            // Aplicar posición inmediata para la etapa inicial (sin transición)
            foreach (var cfg in stageConfigs)
            {
                if (cfg.stage == StageManager.Instance.CurrentStage)
                {
                    waterMesh.localPosition = cfg.targetPosition;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Mueve el río al nivel configurado para una etapa concreta.
    /// Útil para previsualización en el hub sin modificar StageManager.
    /// </summary>
    public void ForceStage(StageManager.Stage stage, float overrideDuration = -1f)
    {
        if (waterMesh == null) return;
        foreach (var cfg in stageConfigs)
        {
            if (cfg.stage == stage)
            {
                float dur = overrideDuration >= 0f ? overrideDuration : cfg.transitionDuration;
                MoveToLevel(cfg.targetPosition, dur);
                return;
            }
        }
    }

    private void OnDestroy()
    {
        if (StageManager.Instance != null)
            StageManager.Instance.OnStageChanged -= OnStageChanged;
    }

    private void OnStageChanged(StageManager.Stage previous, StageManager.Stage newStage)
    {
        foreach (var cfg in stageConfigs)
        {
            if (cfg.stage == newStage)
            {
                MoveToLevel(cfg.targetPosition, cfg.transitionDuration);
                return;
            }
        }
        // Ninguna entrada en stageConfigs coincide con la nueva etapa.
        // Si el nivel del agua no cambia, revisa que stageConfigs[] tenga
        // una entrada para cada Stage que deba producir movimiento.
        Debug.LogWarning($"[WaterLevelController] Sin config para {newStage} — el nivel del agua no cambiará. " +
                         $"Agrega una entrada en stageConfigs[] para esta etapa.", this);
    }

    private void MoveToLevel(Vector3 target, float duration)
    {
        if (_transition != null) StopCoroutine(_transition);
        _transition = StartCoroutine(TransitionRoutine(target, duration));
    }

    private IEnumerator TransitionRoutine(Vector3 target, float duration)
    {
        Vector3 start = waterMesh.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            waterMesh.localPosition = Vector3.Lerp(start, target, t);
            yield return null;
        }

        waterMesh.localPosition = target;
    }

    [ContextMenu("Preview: aplicar posición del primer config")]
    private void PreviewFirst()
    {
        if (waterMesh != null && stageConfigs.Length > 0)
            waterMesh.localPosition = stageConfigs[0].targetPosition;
    }
}
