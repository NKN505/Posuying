using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// Director de hordas estilo Left4Dead:
// - Mantiene una poblacion de enemigos vivos y la repone continuamente.
// - Cada cierto tiempo lanza un "pico de panico" (mas enemigos, mas rapido).
// - Los enemigos aparecen en el NavMesh, alrededor de un jugador y fuera de su vista.
// - Mezcla: mayoria comunes, algunos especiales.
//
// EN RED: solo se ejecuta en el SERVIDOR. Los enemigos se crean como objetos de red
// para que aparezcan igual en todas las maquinas.
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
    [Tooltip("Angulo del cono de vision del jugador: no aparecen enemigos dentro de el")]
    public float viewConeAngle = 100f;

    [Header("Control")]
    public bool active = true;

    private readonly List<EnemyBehaviour> _alive = new List<EnemyBehaviour>();
    private float _spawnTimer;
    private float _panicTimer;
    private float _panicEndTime = -1f;

    void Start()
    {
        _panicTimer = Random.Range(panicEverySeconds.x, panicEverySeconds.y);
    }

    void Update()
    {
        if (!active) return;

        // Solo el servidor decide cuando y donde aparecen los enemigos
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        // Sin jugadores en partida no hay alrededor de quien spawnear
        if (NetworkPlayer.AllPlayers.Count == 0) return;

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
        // Con varios jugadores, elegimos alrededor de cual aparece este enemigo
        PlayerController target = PickRandomPlayer();
        if (target == null) return;

        if (!TryGetSpawnPosition(target.transform, out Vector3 pos)) return;

        EnemyBehaviour prefab = PickPrefab();
        if (prefab == null) return;

        pos.y += spawnYOffset; // evita que el modelo aparezca enterrado

        EnemyBehaviour enemy = Instantiate(prefab, pos, Quaternion.identity);
        enemy.alwaysAggro = true;              // va siempre a por el jugador
        enemy.prefabIndex = GetIndexOfPrefab(prefab);
        enemy.gameObject.SetActive(true);      // por si el prefab quedo desactivado

        // Compensa el pivote del modelo para que no aparezca enterrado
        NavMeshAgent na = enemy.GetComponent<NavMeshAgent>();
        if (na != null)
            na.baseOffset = agentBaseOffset;

        // Darlo de alta en la red: a partir de aqui existe en todas las maquinas
        NetworkObject netObj = enemy.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("El prefab " + prefab.name + " no tiene NetworkObject: no puede aparecer en red.");
            Destroy(enemy.gameObject);
            return;
        }
        netObj.Spawn();

        // La vida se fija DESPUES de Spawn (antes no existe la variable de red)
        enemy.SetMaxHealth(enemyHealth);

        _alive.Add(enemy);
    }

    private PlayerController PickRandomPlayer()
    {
        var players = NetworkPlayer.AllPlayers;
        for (int i = 0; i < 5 && players.Count > 0; i++)
        {
            var p = players[Random.Range(0, players.Count)];
            if (p != null) return p;
        }
        return null;
    }

    // ---------- Catalogo de prefabs (indice unico para comunes + especiales) ----------

    private int CommonCount => commonPrefabs != null ? commonPrefabs.Length : 0;

    public EnemyBehaviour GetPrefabByIndex(int index)
    {
        if (index < 0) return null;
        if (index < CommonCount) return commonPrefabs[index];

        int specialIndex = index - CommonCount;
        if (specialPrefabs != null && specialIndex < specialPrefabs.Length)
            return specialPrefabs[specialIndex];

        return null;
    }

    public int GetIndexOfPrefab(EnemyBehaviour prefab)
    {
        if (prefab == null) return -1;

        for (int i = 0; i < CommonCount; i++)
            if (commonPrefabs[i] == prefab) return i;

        if (specialPrefabs != null)
            for (int i = 0; i < specialPrefabs.Length; i++)
                if (specialPrefabs[i] == prefab) return CommonCount + i;

        return -1;
    }

    // Recrea un enemigo tal y como estaba antes de cambiar de host
    public EnemyBehaviour RestoreEnemy(int prefabIndex, Vector3 position, float yaw,
                                       float health, float maxHealth)
    {
        EnemyBehaviour prefab = GetPrefabByIndex(prefabIndex);
        if (prefab == null) return null;

        EnemyBehaviour enemy = Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f));
        enemy.alwaysAggro = true;
        enemy.prefabIndex = prefabIndex;
        enemy.gameObject.SetActive(true);

        NavMeshAgent na = enemy.GetComponent<NavMeshAgent>();
        if (na != null) na.baseOffset = agentBaseOffset;

        NetworkObject netObj = enemy.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Destroy(enemy.gameObject);
            return null;
        }
        netObj.Spawn();

        enemy.SetMaxHealth(maxHealth);
        enemy.SetHealth(health);

        _alive.Add(enemy);
        return enemy;
    }

    // Los enemigos vivos, para poder fotografiarlos al guardar el mundo
    public IReadOnlyList<EnemyBehaviour> AliveEnemies => _alive;

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

    private bool TryGetSpawnPosition(Transform around, out Vector3 result)
    {
        for (int i = 0; i < placementTries; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(minSpawnDistance, maxSpawnDistance);
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 candidate = around.position + dir * dist;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
                continue;

            if (IsVisibleToAnyPlayer(hit.position))
                continue; // debe aparecer fuera del campo de vision de TODOS

            result = hit.position;
            return true;
        }

        result = Vector3.zero;
        return false;
    }

    // El servidor no tiene la camara de los demas jugadores, asi que aproximamos
    // su vision con un cono hacia delante + comprobacion de que no haya pared en medio.
    private bool IsVisibleToAnyPlayer(Vector3 worldPos)
    {
        var players = NetworkPlayer.AllPlayers;

        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null) continue;

            Vector3 toPoint = worldPos - p.transform.position;
            toPoint.y = 0f;
            if (toPoint.sqrMagnitude < 0.01f) return true;

            float angle = Vector3.Angle(p.transform.forward, toPoint.normalized);
            if (angle > viewConeAngle * 0.5f) continue;   // fuera de su cono de vision

            // Dentro del cono: si no hay nada de por medio, nos veria aparecer
            if (!Physics.Linecast(p.transform.position + Vector3.up, worldPos + Vector3.up))
                return true;
        }

        return false;
    }

    // Cuantos enemigos hay vivos ahora mismo (util para HUD o depuracion)
    public int AliveCount => _alive.Count;
}
