using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// Superviviente controlado por la maquina: hace de "companero de mentira".
//
// Para que sirve: sin tener que esperar a que se conecten companeros de verdad,
// da gente a la que ver por el mapa con su nombre encima, a la que perseguir
// con la camara, y contra la que probar la vida, las etiquetas y las hordas.
//
// No es un jugador de red: no tiene camara, ni inventario, ni dueno. Lo mueve
// entero el servidor con un NavMeshAgent, y los demas lo ven por NetworkTransform
// (igual que los enemigos).
[RequireComponent(typeof(NavMeshAgent))]
public class NpcSurvivor : Character
{
    // Todos los NPCs vivos. Lo usan las etiquetas de nombre.
    public static readonly List<NpcSurvivor> All = new List<NpcSurvivor>();

    [Header("Deambular")]
    [Tooltip("Cuanto se aleja al elegir un destino nuevo")]
    public float wanderRadius = 25f;
    public float walkSpeed = 3.5f;
    [Tooltip("Margen para dar por llegado un destino")]
    public float arriveDistance = 1.5f;

    [Header("Huida")]
    [Tooltip("Si un enemigo entra en este radio, sale corriendo")]
    public float fleeRadius = 12f;
    [Tooltip("A que distancia intenta escapar")]
    public float fleeDistance = 18f;
    public float runSpeed = 6.5f;

    [Tooltip("Cada cuanto recalcula la ruta mientras huye (segundos)")]
    public float fleeRepathInterval = 0.4f;

    // El nombre lo decide el servidor y lo ven todos
    private readonly NetworkVariable<FixedString32Bytes> netName =
        new NetworkVariable<FixedString32Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Para pintar en rojo al que va huyendo, igual que a un companero abatido
    private readonly NetworkVariable<bool> netFleeing = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public string Name => netName.Value.IsEmpty ? "NPC" : netName.Value.ToString();
    public bool IsFleeing => netFleeing.Value;

    private NavMeshAgent agent;
    private float _repathTimer;

    // Buffer reutilizado: buscar enemigos cada pocos frames no debe generar basura
    private static readonly Collider[] _nearby = new Collider[32];

    protected override void Awake()
    {
        base.Awake();

        agent = GetComponent<NavMeshAgent>();

        SetIsLiving(true);
        SetIsAvailable(true);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!All.Contains(this)) All.Add(this);

        // El NPC usa el modelo del jugador, que se reparte por capas segun sea
        // tuyo o de otro. Como el NPC no es de nadie, lo forzamos a "cuerpo de
        // otro": si se quedara en la capa del cuerpo local, tu camara lo
        // excluiria y no lo verias. En todas las maquinas, no solo el servidor.
        PlayerVisual.ApplyRemoteBodyLayer(gameObject);

        if (IsServer)
        {
            if (netName.Value.IsEmpty)
                netName.Value = new FixedString32Bytes(NpcNames.Random());

            agent.speed = walkSpeed;
            PickWanderTarget();
        }
        else if (agent != null)
        {
            // En los clientes lo mueve NetworkTransform. Si dejaramos el agente
            // vivo, pelearia contra la posicion que llega del servidor.
            agent.enabled = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        base.OnNetworkDespawn();
    }

    protected override void Update()
    {
        base.Update();

        // Toda la IA es del servidor; los clientes solo ven el resultado
        if (!IsServer) return;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Transform threat = FindNearbyEnemy();

        if (threat != null)
        {
            Flee(threat);
            return;
        }

        if (netFleeing.Value)
        {
            netFleeing.Value = false;
            agent.speed = walkSpeed;
        }

        Wander();
    }

    // ---------- Comportamientos ----------

    private void Wander()
    {
        if (agent.pathPending) return;

        if (!agent.hasPath || agent.remainingDistance < arriveDistance)
            PickWanderTarget();
    }

    private void Flee(Transform threat)
    {
        if (!netFleeing.Value)
        {
            netFleeing.Value = true;
            agent.speed = runSpeed;
        }

        // Huir es cosa de ir recalculando: el enemigo se mueve, y si fijamos el
        // destino una sola vez el NPC corre hacia el peligro en cuanto rodea.
        _repathTimer -= Time.deltaTime;
        if (_repathTimer > 0f) return;

        _repathTimer = fleeRepathInterval;

        Vector3 away = (transform.position - threat.position).normalized;
        Vector3 goal = transform.position + away * fleeDistance;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(goal, out hit, fleeDistance, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
            return;
        }

        // Contra una pared no hay salida en linea recta: se prueba de lado
        Vector3 side = Vector3.Cross(away, Vector3.up) * fleeDistance;
        if (NavMesh.SamplePosition(transform.position + side, out hit, fleeDistance, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    private void PickWanderTarget()
    {
        Vector3 target = transform.position + Random.insideUnitSphere * wanderRadius;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(target, out hit, wanderRadius, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    // El enemigo mas cercano dentro del radio de panico, o null si no hay
    private Transform FindNearbyEnemy()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, fleeRadius, _nearby);

        Transform nearest = null;
        float nearestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            if (_nearby[i] == null) continue;

            var enemy = _nearby[i].GetComponentInParent<EnemyBehaviour>();
            if (enemy == null) continue;

            float sqr = (enemy.transform.position - transform.position).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = enemy.transform;
            }
        }

        return nearest;
    }

    protected override void Die()
    {
        if (!IsServer) return;

        // Por RPC, o el aviso solo saldria en la pantalla del anfitrion
        AnnounceDeathClientRpc(Name);

        // Como los enemigos: al destruirlo en el servidor, Netcode lo quita
        // tambien en todos los clientes.
        Destroy(gameObject);
    }

    [ClientRpc]
    private void AnnounceDeathClientRpc(string npcName)
    {
        Notifications.Show(npcName + " ha caido");
    }
}
