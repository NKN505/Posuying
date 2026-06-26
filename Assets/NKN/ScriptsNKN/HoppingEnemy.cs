using UnityEngine;
using UnityEngine.AI;

public class HoppingEnemy : EnemyBehaviour
{
    [Header("Salto")]
    public float hopCooldown = 1.5f;
    public float hopForce = 6f;

    private float _hopTimer = 0f;
    private Vector3 _velocity;
    private float _gravity = -20f;

    protected override void Awake()
    {
        base.Awake();
        SetSpeed(5f);
        agent.speed = 0f; // el movimiento lo controlamos manualmente
        agent.updatePosition = false;
    }

    protected override void Chase()
    {
        _hopTimer -= Time.deltaTime;

        if (_hopTimer <= 0f)
        {
            Vector3 direction = (player.position - transform.position).normalized;
            _velocity = direction * GetSpeed();
            _velocity.y = hopForce;
            _hopTimer = hopCooldown;
        }

        _velocity.y += _gravity * Time.deltaTime;
        controller.Move(_velocity * Time.deltaTime);
        agent.nextPosition = transform.position;
    }

    protected override void Patrol()
    {
        _hopTimer -= Time.deltaTime;

        if (_hopTimer <= 0f)
        {
            SetNewPatrolTarget();
            Vector3 direction = (agent.destination - transform.position).normalized;
            _velocity = direction * (GetSpeed() * 0.5f);
            _velocity.y = hopForce * 0.7f;
            _hopTimer = hopCooldown * 1.5f;
        }

        _velocity.y += _gravity * Time.deltaTime;
        controller.Move(_velocity * Time.deltaTime);
        agent.nextPosition = transform.position;
    }
}
