using UnityEngine.AI;

public class NormalEnemy : EnemyBehaviour
{
    protected override void Awake()
    {
        base.Awake();
        SetSpeed(4f);
        agent.speed = GetSpeed();
    }

    protected override void Chase()
    {
        agent.SetDestination(player.position);
    }
}
