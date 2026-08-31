using UnityEngine;
using UnityEngine.AI;

public abstract class EnemyBehaviour : Character
{
    [Header("IA")]
    public float detectionRadius = 10f;
    public float patrolRadius = 20f;
    [Tooltip("Si esta activo, el enemigo va siempre a por el jugador (no patrulla). Lo usan los enemigos de horda.")]
    public bool alwaysAggro = false;

    // De que prefab del HordeDirector salio. Lo necesita el guardado del mundo
    // para poder recrearlo igual si cambia el host.
    [System.NonSerialized] public int prefabIndex = -1;

    [Header("Daño")]
    public float damageAmount = 10f;
    public float damageCooldown = 1f;
    private float _damageTimer = 0f;

    [Header("Muerte")]
    [Tooltip("Segundos que el cadaver se queda en el suelo antes de desaparecer. " +
             "Solo aplica si el enemigo tiene el componente RagdollDeath.")]
    public float segundosDeCadaver = 150f;

    protected NavMeshAgent agent;
    protected Transform player;
    protected enum State { Patrolling, Chasing }
    protected State state = State.Patrolling;

    protected override void Awake()
    {
        base.Awake();

        SetIsLiving(true);
        SetIsPlayer(false);
        SetIsAvailable(true);
        SetIsJumping(false);

        agent = GetComponent<NavMeshAgent>();
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
            agent.SetDestination(hit.position);
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
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, patrolRadius);
    }
}
