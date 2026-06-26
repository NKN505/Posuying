using UnityEngine.AI;

public class SlowEnemy : EnemyBehaviour
{
    protected override void Awake()
    {
        base.Awake();
        SetSpeed(2f);
        agent.speed = GetSpeed();
    }

    protected override void Chase()
    {
        agent.SetDestination(player.position);
    }
}
