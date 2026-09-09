using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public abstract class EnemyBehaviour : Character
{
    [Header("IA")]
    public float detectionRadius = 10f;
    public float patrolRadius = 20f;
    [Tooltip("Si esta activo, el enemigo va siempre a por el jugador (no patrulla). Lo usan los enemigos de horda.")]
    public bool alwaysAggro = false;

    [Header("Objetivo inalcanzable")]
    [Tooltip("Que hacer cuando el jugador se sube a un sitio al que no hay NavMesh " +
             "(una caja, una plataforma suelta). Sin esto el enemigo llega al borde " +
             "de lo que puede pisar y se queda petrificado, que parece un bug. " +
             "Con esto merodea por debajo esperando a que baje.")]
    public bool merodearSiNoLlega = true;
    [Tooltip("Radio del merodeo alrededor del punto mas cercano al jugador")]
    public float radioMerodeo = 3.5f;
    [Tooltip("Cada cuanto elige un punto nuevo al merodear")]
    public float cadenciaMerodeo = 1.6f;

    /// <summary>
    /// True cuando tiene al jugador fichado pero no existe camino hasta el.
    /// Lo puede leer el HUD, el audio o cualquier script que quiera reaccionar.
    /// </summary>
    public bool ObjetivoInalcanzable { get { return _tieneRecorte; } }

    // De que prefab del HordeDirector salio. Lo necesita el guardado del mundo
    // para poder recrearlo igual si cambia el host.
    [System.NonSerialized] public int prefabIndex = -1;

    [Header("Daño")]
    public float damageAmount = 10f;
    public float damageCooldown = 1f;
    private float _damageTimer = 0f;

    [Tooltip("Estado del Animator que se reproduce al golpear cuerpo a cuerpo. " +
             "Vacio = sin animacion (los enemigos de horda no tienen clip para esto). " +
             "Se llama asi y no 'attackState' porque el Acechador ya usa ese nombre " +
             "para su placaje, y dos campos serializados iguales rompen la herencia.")]
    public string estadoGolpe = "attack";
    [Tooltip("Cuanto dura la animacion del golpe")]
    public float duracionGolpe = 0.9f;

    // Lo busca solo: los enemigos que no lo lleven simplemente no se animan
    private EnemyLocomotionAnimator _locomocion;

    [Header("Muerte")]
    [Tooltip("Segundos que el cadaver se queda en el suelo antes de desaparecer. " +
             "Solo aplica si el enemigo tiene el componente RagdollDeath.")]
    public float segundosDeCadaver = 150f;

    protected NavMeshAgent agent;
    protected Transform player;
    protected enum State { Patrolling, Chasing }
    protected State state = State.Patrolling;

    // Recorte del destino cuando el objetivo esta en un sitio sin NavMesh conectado
    private bool _tieneRecorte;
    private bool _autoRepathOriginal = true;
    private float _tiempoHastaRecalcular;
    private float _tiempoDesdeElUltimoPunto;
    private Vector3 _anclaMerodeo;
    private NavMeshPath _rutaDePrueba;

    protected override void Awake()
    {
        base.Awake();

        SetIsLiving(true);
        SetIsPlayer(false);
        SetIsAvailable(true);
        SetIsJumping(false);

        agent = GetComponent<NavMeshAgent>();
        _locomocion = GetComponent<EnemyLocomotionAnimator>();
    }

    // Todos los enemigos vivos, en todas las maquinas. Lo usa el minimapa para
    // no tener que rastrear la escena cada frame (con hordas de 30 seria caro).
    public static readonly System.Collections.Generic.List<EnemyBehaviour> All =
        new System.Collections.Generic.List<EnemyBehaviour>();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!All.Contains(this)) All.Add(this);

        if (IsServer)
        {
            // Solo el servidor mueve a los enemigos
            SetNewPatrolTarget();
        }
        else if (agent != null)
        {
            // En los clientes el enemigo se mueve por NetworkTransform.
            // Si dejaramos el NavMeshAgent activo, pelearia contra la posicion recibida.
            agent.enabled = false;
        }
    }

    protected override void Update()
    {
        base.Update();

        // La IA es autoridad del servidor: los clientes solo ven el resultado
        if (!IsServer) return;

        if (_damageTimer > 0f) _damageTimer -= Time.deltaTime;

        UpdateNearestPlayer();
        if (player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        state = (alwaysAggro || distanceToPlayer < detectionRadius) ? State.Chasing : State.Patrolling;

        // Con el objetivo inalcanzable el agente entra en un ciclo destructivo:
        // autoRepath reintenta la ruta completa cada pocas decimas, cada reintento
        // TIRA el camino actual, y sin camino desiredVelocity cae a 0 y frena en
        // seco. Luego vuelve el trozo parcial y acelera. Eso es el diente de sierra.
        //
        // Medido: 2 reinicios por segundo, con has=0 rest=0 deseada=0 en cada fondo.
        //
        // La cura es no darle nunca un destino que no pueda alcanzar: se recorta al
        // punto alcanzable mas cercano, la ruta pasa a ser completa y autoRepath ya
        // no tiene nada que reintentar.
        ActualizarRecorte();

        // El switch se ejecuta SIEMPRE, tambien con el destino recortado. Chase()
        // no solo pide ruta: el Acechador comprueba ahi si lo estan mirando, el
        // Tanque mira sus zonas prohibidas, el Cobarde decide si huir. Saltarselo
        // les quita su mecanica. El recorte se aplica donde toca, dentro de
        // PedirDestino, no secuestrando el metodo entero.
        switch (state)
        {
            case State.Patrolling: Patrol(); break;
            case State.Chasing:   Chase();   break;
        }
    }

    // Con varios jugadores, el enemigo va a por el que tenga mas cerca
    protected void UpdateNearestPlayer()
    {
        Transform nearest = null;
        float nearestSqr = float.MaxValue;

        var players = NetworkPlayer.AllPlayers;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null) continue;

            // A los abatidos se les deja en paz: ya no son una amenaza y ademas
            // asi el companero puede acercarse a levantarlos
            var downed = p.GetComponent<PlayerDownedState>();
            if (downed != null && !downed.CanAct) continue;

            float sqr = (p.transform.position - transform.position).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = p.transform;
            }
        }

        // Los supervivientes NPC tambien valen como presa: si no, los enemigos
        // los atravesarian sin inmutarse y no servirian para probar nada.
        var npcs = NpcSurvivor.All;
        for (int i = 0; i < npcs.Count; i++)
        {
            var npc = npcs[i];
            if (npc == null) continue;

            float sqr = (npc.transform.position - transform.position).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = npc.transform;
            }
        }

        player = nearest;
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        base.OnNetworkDespawn();
    }

    // Red de seguridad: si un enemigo se destruye sin pasar por el despawn
    // (cambio de escena, reinicio de partida) no debe quedar en la lista.
    public override void OnDestroy()
    {
        All.Remove(this);
        base.OnDestroy();
    }

    // ---------------------------------------------------------------------
    // Recorte del destino cuando el objetivo es inalcanzable.
    //
    // El jugador puede acabar en sitios sin NavMesh conectado: encima de una caja,
    // en una plataforma suelta, o en otra de las islas en que esta partido el mapa.
    // Pedirle al agente que vaya ahi no solo no funciona, es que lo ROMPE (ver el
    // comentario de arriba sobre autoRepath).
    //
    // Aqui se calcula, como mucho dos veces por segundo, si hay camino de verdad.
    // Si no lo hay, el destino se recorta al ultimo punto alcanzable y el enemigo
    // merodea por ahi esperando. En cuanto vuelve a haber camino completo se suelta
    // el recorte y Chase() retoma el mando sin enterarse de nada.
    // ---------------------------------------------------------------------
    private void ActualizarRecorte()
    {
        if (!merodearSiNoLlega) { SoltarRecorte(); return; }
        if (player == null || agent == null || !agent.enabled || !agent.isOnNavMesh) { SoltarRecorte(); return; }

        _tiempoHastaRecalcular -= Time.deltaTime;
        if (_tiempoHastaRecalcular > 0f) return;
        _tiempoHastaRecalcular = 0.5f;

        if (_rutaDePrueba == null) _rutaDePrueba = new NavMeshPath();

        bool hayCamino =
            NavMesh.CalculatePath(transform.position, player.position, agent.areaMask, _rutaDePrueba) &&
            _rutaDePrueba.status == NavMeshPathStatus.PathComplete;

        if (hayCamino) { SoltarRecorte(); return; }

        int esquinas = _rutaDePrueba.corners.Length;
        if (esquinas == 0) { SoltarRecorte(); return; }

        // Ultimo punto al que si se puede llegar acercandose al objetivo
        _anclaMerodeo = _rutaDePrueba.corners[esquinas - 1];

        if (!_tieneRecorte)
        {
            _tieneRecorte = true;
            _tiempoDesdeElUltimoPunto = cadenciaMerodeo;   // elige punto ya

            // Mientras nosotros llevamos el destino, autoRepath solo estorba: es
            // justo quien provoca el frenazo periodico.
            _autoRepathOriginal = agent.autoRepath;
            agent.autoRepath = false;
        }
    }

    private void SoltarRecorte()
    {
        if (!_tieneRecorte) return;
        _tieneRecorte = false;
        if (agent != null && agent.enabled) agent.autoRepath = _autoRepathOriginal;
    }

    /// <summary>
    /// Unico sitio por el que se pide ruta. Las subclases deben usar esto en vez
    /// de agent.SetDestination: si el objetivo esta recortado por inalcanzable,
    /// aqui se sustituye por el punto alcanzable mas cercano. Llamando al agente
    /// directamente se saltarian el recorte y volveria el diente de sierra.
    /// </summary>
    protected void PedirDestino(Vector3 objetivo)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        if (_tieneRecorte) { IrAlPuntoRecortado(); return; }

        agent.SetDestination(objetivo);
    }

    private void IrAlPuntoRecortado()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        _tiempoDesdeElUltimoPunto += Time.deltaTime;
        if (_tiempoDesdeElUltimoPunto < cadenciaMerodeo && agent.hasPath) return;
        _tiempoDesdeElUltimoPunto = 0f;

        Vector2 c = Random.insideUnitCircle * radioMerodeo;
        Vector3 candidato = _anclaMerodeo + new Vector3(c.x, 0f, c.y);

        NavMeshHit hit;
        Vector3 destino = NavMesh.SamplePosition(candidato, out hit, radioMerodeo, agent.areaMask)
            ? hit.position : _anclaMerodeo;

        agent.SetDestination(destino);
    }

    protected virtual void Patrol()
    {
        if (!agent.hasPath || agent.remainingDistance < 1f)
            SetNewPatrolTarget();
    }

    protected abstract void Chase();

    // Los enemigos se destruyen al morir. Al hacerlo en el servidor sobre un objeto
    // de red, Netcode se encarga de eliminarlo tambien en todos los clientes.
    //
    // Si el enemigo tiene ragdoll montado, primero cae y el cuerpo se queda un
    // rato en el suelo. Destruirlo en el mismo frame haria que el ragdoll no
    // llegase a verse nunca.
    protected override void Die()
    {
        if (!IsServer) return;

        if (MorirConRagdoll())
        {
            Destroy(gameObject, segundosDeCadaver);
            return;
        }

        Destroy(gameObject);
    }

    protected void SetNewPatrolTarget()
    {
        Vector3 randomDirection = Random.insideUnitSphere * patrolRadius + transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, patrolRadius, 1))
            PedirDestino(hit.position);
    }

    void OnTriggerStay(Collider other)
    {
        // El dano a los jugadores solo lo aplica el servidor
        if (!IsServer) return;

        // Tampoco se remata a quien ya esta en el suelo
        var downedTarget = other.GetComponent<PlayerDownedState>();
        if (downedTarget != null && !downedTarget.CanAct) return;

        if (other.CompareTag("Player") && _damageTimer <= 0f)
        {
            Character playerChar = other.GetComponent<Character>();
            if (playerChar != null)
            {
                // El zarpazo entra por donde esta el enemigo y empuja hacia
                // donde esta la victima: asi el ragdoll cae en la direccion
                // del golpe en vez de desplomarse recto.
                Vector3 punto = other.ClosestPoint(transform.position + Vector3.up);
                Vector3 direccion = other.transform.position - transform.position;
                direccion.y = 0f;

                playerChar.TakeDamage(damageAmount, punto, direccion);
                _damageTimer = damageCooldown;

                // La animacion tiene que verse en todas las maquinas, no solo en
                // la del anfitrion: el dano es del servidor, el espectaculo no.
                AnimarAtaqueRpc();
            }
        }
    }

    /// <summary>
    /// Reproduce el zarpazo en todas las maquinas. Es un solo sitio para los trece
    /// enemigos: todos hacen dano por el mismo trigger, asi que todos se animan
    /// aqui. Si un enemigo no tiene EnemyLocomotionAnimator o su controlador no
    /// tiene el estado, no pasa nada: se queda como estaba.
    /// </summary>
    [Rpc(SendTo.Everyone)]
    private void AnimarAtaqueRpc()
    {
        if (_locomocion == null || string.IsNullOrEmpty(estadoGolpe)) return;
        _locomocion.PlayOneShot(estadoGolpe, duracionGolpe);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, patrolRadius);
    }
}
