using UnityEngine.AI;

public class FastEnemy : EnemyBehaviour
{
    protected override void Awake()
    {
        base.Awake();
        SetSpeed(8f);
        agent.speed = GetSpeed();
    }

    protected override void Chase()
    {
        agent.SetDestination(player.position);
    }
}
