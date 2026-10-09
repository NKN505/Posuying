using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// Director de hordas estilo Left4Dead:
// - Mantiene una poblacion de enemigos vivos y la repone continuamente.
// - Cada cierto tiempo lanza un "pico de panico" (mas enemigos, mas rapido).
// - Los enemigos salen de los EnemySpawnPoint de la escena: uno que ningun jugador
//   este viendo y con ruta hasta el jugador. Si la escena no tiene puntos, aparecen
//   en el NavMesh alrededor de un jugador y fuera de su vista (metodo antiguo).
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
    [Tooltip("Si esta marcado, cada enemigo conserva la vida de su prefab (el Tanque " +
             "tiene 6000) en vez de recibir 'Enemy Health'. Desmarcado = como hasta ahora.")]
    public bool usarVidaDelPrefab = false;

    [Header("Poblacion")]
    public int baseAlive = 8;         // objetivo de vivos en calma
    public int maxAlive = 30;         // tope absoluto
    public float spawnInterval = 1f;  // cada cuanto intenta reponer
    public int spawnBatch = 2;        // cuantos por intento

    [Header("Dificultad segun el equipo")]
    [Tooltip("Ajusta la horda al numero de jugadores y a como van")]
    public bool dificultadAdaptativa = true;
    [Tooltip("Enemigos de mas por cada jugador adicional, en proporcion (0,5 = +50 % por jugador)")]
    public float extraPorJugador = 0.5f;
    [Range(0.4f, 1f)]
    [Tooltip("Cuando el equipo va muy mal, la horda se queda en esta fraccion")]
    public float alivioSiVaisMal = 0.7f;

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
    [Tooltip("Altura de los ojos del jugador sobre sus pies, para la linea de vision")]
    public float alturaOjos = 1.8f;

    [Header("Puntos de spawn (EnemySpawnPoint)")]
    [Tooltip("Si la escena tiene EnemySpawnPoint, los enemigos salen de ellos. " +
             "Sin puntos en la escena se usa el metodo antiguo (posicion al azar alrededor del jugador).")]
    public bool usarPuntosDeSpawn = true;
    [Tooltip("Distancia minima entre el punto elegido y CUALQUIER jugador")]
    public float distanciaMinimaPunto = 12f;
    [Tooltip("Distancia maxima entre el punto elegido y el jugador al que va a por")]
    public float distanciaMaximaPunto = 60f;
    [Tooltip("Segundos antes de que un mismo punto pueda volver a usarse")]
    public float esperaEntreUsos = 4f;
    [Tooltip("Solo usar puntos desde los que haya ruta por el NavMesh hasta el jugador " +
             "(descarta los que estan al otro lado de un muro o de un porton cerrado)")]
    public bool exigirRuta = true;
    [Tooltip("Cuantos puntos se comprueban a fondo (ruta incluida) por intento, para no gastar CPU")]
    public int puntosPorIntento = 8;

    [Header("Retirada de enemigos que se quedan atras")]
    [Tooltip("Quita los enemigos que ya no pueden alcanzar a nadie. Sin esto, los que se " +
             "quedaban en un tercio anterior seguian contando como vivos, llenaban el cupo " +
             "de la horda y en el tercio nuevo no aparecia ninguno.")]
    public bool retirarRezagados = true;
    [Tooltip("A mas de esta distancia de TODOS los jugadores se retira siempre")]
    public float distanciaRetirada = 75f;
    [Tooltip("Si esta en un tercio en el que no queda ningun jugador, se retira a partir de esta distancia")]
    public float distanciaRetiradaOtroTercio = 30f;
    [Tooltip("Sin ruta hasta ningun jugador (porton cerrado, muro) y mas lejos que esto, empieza a contar")]
    public float distanciaSinRuta = 15f;
    [Tooltip("Segundos seguidos sin ruta antes de retirarlo")]
    public float segundosSinRuta = 6f;
    [Tooltip("Cuantos enemigos se revisan por segundo (la comprobacion de ruta cuesta CPU)")]
    public float revisionesPorSegundo = 12f;

    [Header("Control")]
    public bool active = true;

    private readonly List<EnemyBehaviour> _alive = new List<EnemyBehaviour>();
    private float _spawnTimer;
    private float _panicTimer;
    private float _panicEndTime = -1f;

    void Awake()
    {
        _ruta = new NavMeshPath();
    }

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

        // En la sala de espera todavia no hay partida
        if (!SalaEspera.EnJuego) return;

        PruneDead();
        AjustarDificultad();
        UpdatePanic();
        RevisarRezagados();
        AlCambiarDeTercio();

        float interval = IsPanicking ? panicSpawnInterval : spawnInterval;
        _spawnTimer -= Time.deltaTime;
        if (_spawnTimer <= 0f)
        {
            _spawnTimer = interval;
            TrySpawnBatch();
        }
    }

    private bool IsPanicking => Time.time < _panicEndTime;

    // Cuantos enemigos vivos se buscan ahora: la base, mas el pico de panico, y
    // todo ello ajustado a como es y como va el equipo.
    private int CurrentTarget =>
        Mathf.RoundToInt((baseAlive + (IsPanicking ? panicExtraAlive : 0)) * FactorDificultad);

    /// <summary>Multiplicador de la horda ahora mismo (1 = un jugador en buen estado).</summary>
    public float FactorDificultad { get; private set; } = 1f;
    private float _siguienteAjuste;

    // Mas jugadores, mas enemigos; y si el equipo va muy mal (poca vida, casi sin
    // vidas, gente en el suelo) se afloja un poco para que pueda rehacerse.
    private void AjustarDificultad()
    {
        if (!dificultadAdaptativa) { FactorDificultad = 1f; return; }
        if (Time.time < _siguienteAjuste) return;
        _siguienteAjuste = Time.time + 2f;

        int jugadores = 0, enPie = 0;
        float vida = 0f;
        foreach (var p in NetworkPlayer.AllPlayers)
        {
            if (p == null || p.GetComponent<NetworkPlayer>() == null) continue;
            jugadores++;

            var estado = p.GetComponent<PlayerDownedState>();
            if (estado != null && !estado.CanAct) continue;
            enPie++;
            vida += Mathf.Clamp01(p.GetHealth() / Mathf.Max(1f, p.GetMaxHealth()));
        }
        if (jugadores == 0) { FactorDificultad = 1f; return; }

        float porJugadores = 1f + extraPorJugador * (jugadores - 1);

        float vidaMedia = enPie > 0 ? vida / enPie : 0f;
        bool apurados = vidaMedia < 0.35f || enPie < jugadores ||
                        (MatchManager.Instance != null && MatchManager.Instance.Lives <= 1);
        float objetivo = porJugadores * (apurados ? alivioSiVaisMal : 1f);

        // Cambia poco a poco: que no desaparezca media horda de golpe
        FactorDificultad = Mathf.MoveTowards(FactorDificultad, objetivo, 0.15f);
    }

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
        // Los muertos tambien salen de la cuenta: el cadaver se queda un rato en
        // el suelo y antes seguia ocupando sitio en la horda (y se guardaba en la
        // migracion de host como un enemigo vivo).
        for (int i = _alive.Count - 1; i >= 0; i--)
            if (_alive[i] == null || _alive[i].IsDead)
            {
                if (!ReferenceEquals(_alive[i], null)) _sinRutaDesde.Remove(_alive[i]);
                _alive.RemoveAt(i);
            }
    }

    // ---------- Retirada de rezagados ----------

    private readonly Dictionary<EnemyBehaviour, float> _sinRutaDesde = new Dictionary<EnemyBehaviour, float>();
    private int _indiceRevision;
    private float _revisionesPendientes;
    private int _ultimoTercio = 1;

    // Al llegar el equipo a un tercio nuevo la horda reacciona al momento: se
    // revisa a todos de golpe (los del tercio anterior dejan sitio) y se repone ya.
    private void AlCambiarDeTercio()
    {
        if (ProgresoTercios.Instance == null) return;

        int tercio = ProgresoTercios.Instance.TercioAlcanzado;
        if (tercio == _ultimoTercio) return;
        _ultimoTercio = tercio;

        _revisionesPendientes += _alive.Count;
        _spawnTimer = 0f;
    }

    // Revisa unos pocos enemigos cada frame, por turnos, para repartir el coste.
    private void RevisarRezagados()
    {
        if (!retirarRezagados || _alive.Count == 0) return;

        _revisionesPendientes += revisionesPorSegundo * Time.deltaTime;
        int n = Mathf.Min((int)_revisionesPendientes, _alive.Count);
        _revisionesPendientes -= n;

        for (int k = 0; k < n && _alive.Count > 0; k++)
        {
            _indiceRevision = (_indiceRevision + 1) % _alive.Count;
            EnemyBehaviour enemigo = _alive[_indiceRevision];
            if (enemigo == null || !enemigo.IsSpawned || enemigo.IsDead) continue;

            if (DebeRetirarse(enemigo))
            {
                _alive.RemoveAt(_indiceRevision);
                _sinRutaDesde.Remove(enemigo);
                // Retirarlo no es matarlo: no deja cadaver ni suelta objetos
                enemigo.NetworkObject.Despawn(true);
            }
        }
    }

    private bool DebeRetirarse(EnemyBehaviour enemigo)
    {
        Vector3 pos = enemigo.transform.position;
        var players = NetworkPlayer.AllPlayers;

        // El jugador mas cercano, y si queda alguno en el tercio del enemigo
        int tercioEnemigo = ZonaTercio.TercioEn(pos);
        bool jugadorEnSuTercio = false;
        PlayerController cercano = null;
        float mejorSqr = float.MaxValue;

        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] == null) continue;
            Vector3 pj = players[i].transform.position;

            float d = DistanciaPlanaSqr(pos, pj);
            if (d < mejorSqr) { mejorSqr = d; cercano = players[i]; }
            if (ZonaTercio.TercioEn(pj) == tercioEnemigo) jugadorEnSuTercio = true;
        }
        if (cercano == null) return false;

        // Muy lejos de todos: fuera, se vea o no (a esa distancia no se distingue)
        if (mejorSqr > distanciaRetirada * distanciaRetirada) return true;

        // De aqui en adelante nunca desaparece delante de alguien
        if (IsVisibleToAnyPlayer(pos)) { _sinRutaDesde.Remove(enemigo); return false; }

        // Se ha quedado en un tercio que el equipo ya ha dejado atras
        if (tercioEnemigo != 0 && !jugadorEnSuTercio &&
            mejorSqr > distanciaRetiradaOtroTercio * distanciaRetiradaOtroTercio)
            return true;

        // Encerrado: no puede llegar hasta nadie (porton cerrado, muro, otra isla del NavMesh)
        if (mejorSqr > distanciaSinRuta * distanciaSinRuta && !HayRutaHastaAlguien(pos))
        {
            if (!_sinRutaDesde.TryGetValue(enemigo, out float desde))
            {
                _sinRutaDesde[enemigo] = Time.time;
                return false;
            }
            return Time.time - desde >= segundosSinRuta;
        }

        _sinRutaDesde.Remove(enemigo);
        return false;
    }

    private bool HayRutaHastaAlguien(Vector3 desde)
    {
        if (!NavMesh.SamplePosition(desde, out NavMeshHit origen, 3f, NavMesh.AllAreas)) return false;

        var players = NetworkPlayer.AllPlayers;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] == null) continue;
            if (!NavMesh.SamplePosition(players[i].transform.position, out NavMeshHit destino, 4f, NavMesh.AllAreas))
                continue;
            if (NavMesh.CalculatePath(origen.position, destino.position, NavMesh.AllAreas, _ruta) &&
                _ruta.status == NavMeshPathStatus.PathComplete)
                return true;
        }
        return false;
    }

    private void TrySpawnBatch()
    {
        // El tope tambien crece con el equipo: con cuatro jugadores, 30 se queda corto
        int target = Mathf.Min(CurrentTarget, Mathf.RoundToInt(maxAlive * Mathf.Max(1f, FactorDificultad)));
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
        if (!usarVidaDelPrefab)
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
        // Un enemigo guardado sin vida no se recrea: volveria como inmortal
        if (health <= 0f) return null;

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
        if (usarPuntosDeSpawn && EnemySpawnPoint.All.Count > 0)
            return TryGetSpawnPoint(around, out result);

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

    private readonly List<EnemySpawnPoint> _candidatos = new List<EnemySpawnPoint>();
    // Puntos del cuerpo del enemigo que se comprueban al decidir si alguien lo veria
    private static readonly float[] AlturasCuerpo = { 0.3f, 1.0f, 1.8f };

    // Se crea en Awake: Unity no permite crear un NavMeshPath al inicializar campos
    private NavMeshPath _ruta;

    // Elige un EnemySpawnPoint valido para aparecer cerca de 'around'.
    // Primero el filtro barato (distancias y espera), luego el caro (vista y ruta)
    // sobre unos pocos candidatos en orden aleatorio.
    private bool TryGetSpawnPoint(Transform around, out Vector3 result)
    {
        result = Vector3.zero;

        // Radio amplio: en interiores y escaleras el jugador puede quedar algo separado
        // del NavMesh, y con 3 m justos no aparecia nadie mientras estuviera ahi.
        if (!NavMesh.SamplePosition(around.position, out NavMeshHit objetivo, 3f, NavMesh.AllAreas) &&
            !NavMesh.SamplePosition(around.position, out objetivo, 8f, NavMesh.AllAreas))
            return false;

        float minSqr = distanciaMinimaPunto * distanciaMinimaPunto;
        float maxSqr = distanciaMaximaPunto * distanciaMaximaPunto;
        var players = NetworkPlayer.AllPlayers;

        _candidatos.Clear();
        foreach (var punto in EnemySpawnPoint.All)
        {
            if (Time.time - punto.ultimoUso < esperaEntreUsos) continue;

            Vector3 p = punto.transform.position;
            if (DistanciaPlanaSqr(p, around.position) > maxSqr) continue;

            // Lejos de TODOS los jugadores, no solo del objetivo
            bool demasiadoCerca = false;
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && DistanciaPlanaSqr(p, players[i].transform.position) < minSqr)
                { demasiadoCerca = true; break; }
            if (demasiadoCerca) continue;

            _candidatos.Add(punto);
        }

        // Barajar para no favorecer siempre los mismos puntos
        for (int i = _candidatos.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_candidatos[i], _candidatos[j]) = (_candidatos[j], _candidatos[i]);
        }

        int comprobados = 0;
        foreach (var punto in _candidatos)
        {
            if (comprobados++ >= puntosPorIntento) break;

            // Repartir un poco alrededor del punto para que no salgan apilados
            Vector2 desvio = Random.insideUnitCircle * punto.radio;
            Vector3 candidato = punto.transform.position + new Vector3(desvio.x, 0f, desvio.y);
            if (!NavMesh.SamplePosition(candidato, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                continue;

            if (IsVisibleToAnyPlayer(hit.position)) continue;

            if (exigirRuta &&
                (!NavMesh.CalculatePath(hit.position, objetivo.position, NavMesh.AllAreas, _ruta) ||
                 _ruta.status != NavMeshPathStatus.PathComplete))
                continue;

            punto.ultimoUso = Time.time;
            result = hit.position;
            return true;
        }

        return false;
    }

    private static float DistanciaPlanaSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz;
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

            // Mas alla de la bruma no se le ve aparecer aunque este mirando hacia alli
            if (Bruma.Tapa(worldPos, p.transform.position)) continue;

            float angle = Vector3.Angle(p.transform.forward, toPoint.normalized);
            if (angle > viewConeAngle * 0.5f) continue;   // fuera de su cono de vision

            // Dentro del cono: le veria aparecer si ALGUNA parte del cuerpo del enemigo
            // (pies, pecho, cabeza) queda a la vista desde la altura de los ojos.
            // Mirar solo a 1 m de altura dejaba pasar enemigos detras de muretes
            // que tapan esa linea pero no la de los ojos.
            Vector3 ojos = p.transform.position + Vector3.up * alturaOjos;
            for (int k = 0; k < AlturasCuerpo.Length; k++)
                if (!Physics.Linecast(ojos, worldPos + Vector3.up * AlturasCuerpo[k],
                                      Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return true;
        }

        return false;
    }

    // Cuantos enemigos hay vivos ahora mismo (util para HUD o depuracion)
    public int AliveCount => _alive.Count;
}
