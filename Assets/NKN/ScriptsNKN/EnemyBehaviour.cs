using UnityEngine;
using UnityEngine.AI;

public abstract class EnemyBehaviour : Character
{
    [Header("IA")]
    public float detectionRadius = 10f;
    public float patrolRadius = 20f;

    [Header("Daño")]
    public float damageAmount = 10f;
    public float damageCooldown = 1f;
    private float _damageTimer = 0f;

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
        player = GameObject.FindWithTag("Player")?.transform;

        SetNewPatrolTarget();
    }

    protected override void Update()
    {
        base.Update();

        if (_damageTimer > 0f) _damageTimer -= Time.deltaTime;

        if (player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        state = distanceToPlayer < detectionRadius ? State.Chasing : State.Patrolling;

        switch (state)
        {
            case State.Patrolling: Patrol(); break;
            case State.Chasing:   Chase();   break;
        }
    }

    protected virtual void Patrol()
    {
        if (!agent.hasPath || agent.remainingDistance < 1f)
            SetNewPatrolTarget();
    }

    protected abstract void Chase();

    protected void SetNewPatrolTarget()
    {
        Vector3 randomDirection = Random.insideUnitSphere * patrolRadius + transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, patrolRadius, 1))
            agent.SetDestination(hit.position);
    }

    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Player") && _damageTimer <= 0f)
        {
            Character playerChar = other.GetComponent<Character>();
            if (playerChar != null)
            {
                playerChar.TakeDamage(damageAmount);
                _damageTimer = damageCooldown;
                Debug.Log("Daño infligido: " + damageAmount);
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
