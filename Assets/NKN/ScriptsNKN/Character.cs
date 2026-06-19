using UnityEngine;

public class Character : MonoBehaviour{

/*
    Este script define la logica comun de movimientos,
    salto, recibir daño infligir daño etc
    Las clases hijas deciden como usarlo.
    
*/
    protected CharacterController controller; 

    [SerializeField] private float health = 1000f;
    [SerializeField] private float speed = 5f;
    [SerializeField] private float jumpForce =10f;

    [SerializeField] private bool isPlayer = false;

    private bool isLiving = false;
    private bool isJumping = false;
    private bool isAvailable = false;

    private Vector3 velocity;
    private float gravity = -9.81f;

    public float GetHealth()
{
    return health;
}

public void SetHealth(float health)
{
    this.health = health;
}

public float GetSpeed()
{
    return speed;
}

public void SetSpeed(float speed)
{
    this.speed = speed;
}

public float GetJumpForce()
{
    return jumpForce;
}

public void SetJumpForce(float jumpForce)
{
    this.jumpForce = jumpForce;
}

public bool GetIsPlayer()
{
    return isPlayer;
}

public void SetIsPlayer(bool isPlayer)
{
    this.isPlayer = isPlayer;
}

public bool GetIsLiving()
{
    return isLiving;
}

public void SetIsLiving(bool isLiving)
{
    this.isLiving = isLiving;
}

public bool GetIsJumping()
{
    return isJumping;
}

public void SetIsJumping(bool isJumping)
{
    this.isJumping = isJumping;
}

public bool GetIsAvailable()
{
    return isAvailable;
}

public void SetIsAvailable(bool isAvailable)
{
    this.isAvailable = isAvailable;
}



public void Move(Vector3 direction){

    controller.Move(direction * GetSpeed() * Time.deltaTime);

}

public void ApplyGravity(){

    if (controller.isGrounded && velocity.y < 0)
    {
        velocity.y = -2f;
    }

    velocity.y += gravity * Time.deltaTime;

    controller.Move(velocity * Time.deltaTime);

}

protected virtual void Awake(){

    controller = GetComponent<CharacterController>();

}

protected virtual void Update(){

}

}

