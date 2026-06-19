using UnityEngine;

public class EnemyBehaviour : Character
{

    protected override void Awake()
    {
    base.Awake();

    SetIsLiving(true);
    SetIsPlayer(false);
    SetIsAvailable(true);
    SetIsJumping(false);

    }   

    protected override void Update()
    {
        base.Update();

        // IA del enemigo

    }
}