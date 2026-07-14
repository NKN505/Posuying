using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Director de hordas estilo Left4Dead:
// - Mantiene una poblacion de enemigos vivos y la repone continuamente.
// - Cada cierto tiempo lanza un "pico de panico" (mas enemigos, mas rapido).
// - Los enemigos aparecen en el NavMesh, alrededor del jugador y fuera de su vista.
// - Mezcla: mayoria comunes, algunos especiales.
public class HordeDirector : MonoBehaviour
{
    [Header("Enemigos (prefabs)")]
    public EnemyBehaviour[] commonPrefabs;    // normales, lentos
    public EnemyBehaviour[] specialPrefabs;   // rapidos, saltarines
    [Range(0f, 1f)] public float specialChance = 0.15f;
    [Tooltip("Vida de cada enemigo spawneado (baja = facil de matar)")]
    public float enemyHealth = 60f;

    [Header("Poblacion")]
    public int baseAlive = 8;         // objetivo de vivos en calma
    public int maxAlive = 30;         // tope absoluto
    public float spawnInterval = 1f;  // cada cuanto intenta reponer
    public int spawnBatch = 2;        // cuantos por intento

    [Header("Pico de panico")]
    public Vector2 panicEverySeconds = new Vector2(20f, 40f);
    public int panicExtraAlive = 18;
    public float panicDuration = 12f;
    public float panicSpawnInterval = 0.4f;

    [Header("Aparicion alrededor del jugador")]
    public float minSpawnDistance = 12f;
    public float maxSpawnDistance = 28f;
    public float navSampleRadius = 5f;
    public int placementTries = 12;
    [Tooltip("Altura extra al aparecer (poco efecto: el NavMeshAgent reajusta la altura)")]
    public float spawnYOffset = 1f;
    [Tooltip("Compensa que el modelo quede enterrado. Sube este valor hasta que el enemigo aparezca de pie.")]
    public float agentBaseOffset = 0.9f;

    [Header("Control")]
    public bool active = true;

    private Transform _player;
    private Camera _cam;
    private readonly List<EnemyBehaviour> _alive = new List<EnemyBehaviour>();
    private float _spawnTimer;
    private float _panicTimer;
    private float _panicEndTime = -1f;

    void Start()
    {
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) _player = p.transform;
        _cam = Camera.main;

        _panicTimer = Random.Range(panicEverySeconds.x, panicEverySeconds.y);
    }

    void Update()
    {
        if (!active || _player == null) return;

        PruneDead();
        UpdatePanic();

        float interval = IsPanicking ? panicSpawnInterval : spawnInterval;
        _spawnTimer -= Time.deltaTime;
        if (_spawnTimer <= 0f)
        {
            _spawnTimer = interval;
            TrySpawnBatch();
        }
    }

    private bool IsPanicking => Time.time < _panicEndTime;

    private int CurrentTarget => baseAlive + (IsPanicking ? panicExtraAlive : 0);

    private void UpdatePanic()
    {
        if (IsPanicking) return;

        _panicTimer -= Time.deltaTime;
        if (_panicTimer <= 0f)
        {
            _panicEndTime = Time.time + panicDuration;
            _panicTimer = panicDuration + Random.Range(panicEverySeconds.x, panicEverySeconds.y);
            Debug.Log("PANICO! La horda se intensifica");
        }
    }

    private void PruneDead()
    {
        for (int i = _alive.Count - 1; i >= 0; i--)
            if (_alive[i] == null)
                _alive.RemoveAt(i);
    }

    private void TrySpawnBatch()
    {
        int target = Mathf.Min(CurrentTarget, maxAlive);
        int toSpawn = Mathf.Min(spawnBatch, target - _alive.Count);

        for (int i = 0; i < toSpawn; i++)
            SpawnOne();
    }

    private void SpawnOne()
    {
        if (!TryGetSpawnPosition(out Vector3 pos)) return;

        EnemyBehaviour prefab = PickPrefab();
        if (prefab == null) return;

        pos.y += spawnYOffset; // evita que el modelo aparezca enterrado

        EnemyBehaviour enemy = Instantiate(prefab, pos, Quaternion.identity);
        enemy.SetHealth(enemyHealth);          // vida baja para matarlo facil
        enemy.alwaysAggro = true;              // va siempre a por el jugador
        enemy.gameObject.SetActive(true);      // por si el prefab quedo desactivado

        // Compensa el pivote del modelo para que no aparezca enterrado
        NavMeshAgent na = enemy.GetComponent<NavMeshAgent>();
        if (na != null)
            na.baseOffset = agentBaseOffset;

        _alive.Add(enemy);
    }

    private EnemyBehaviour PickPrefab()
    {
        bool special = specialPrefabs != null && specialPrefabs.Length > 0 && Random.value < specialChance;
        EnemyBehaviour[] pool = special ? specialPrefabs : commonPrefabs;

        // Si el pool elegido esta vacio, usar el otro
        if (pool == null || pool.Length == 0)
            pool = (commonPrefabs != null && commonPrefabs.Length > 0) ? commonPrefabs : specialPrefabs;

        if (pool == null || pool.Length == 0) return null;
        return pool[Random.Range(0, pool.Length)];
    }

    private bool TryGetSpawnPosition(out Vector3 result)
    {
        for (int i = 0; i < placementTries; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(minSpawnDistance, maxSpawnDistance);
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 candidate = _player.position + dir * dist;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
                continue;

            if (IsVisible(hit.position))
                continue; // debe aparecer fuera del campo de vision del jugador

            result = hit.position;
            return true;
        }

        result = Vector3.zero;
        return false;
    }

    private bool IsVisible(Vector3 worldPos)
    {
        if (_cam == null) return false;

        Vector3 vp = _cam.WorldToViewportPoint(worldPos + Vector3.up * 1f);
        return vp.z > 0f
            && vp.x > -0.05f && vp.x < 1.05f
            && vp.y > -0.05f && vp.y < 1.05f;
    }

    // Cuantos enemigos hay vivos ahora mismo (util para HUD o depuracion)
    public int AliveCount => _alive.Count;

    void OnDrawGizmosSelected()
    {
        if (_player == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(_player.position, minSpawnDistance);
        Gizmos.DrawWireSphere(_player.position, maxSpawnDistance);
    }
}
