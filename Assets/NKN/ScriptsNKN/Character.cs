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
    [SerializeField] private float jumpForce = 7f;
    [SerializeField] private float sprintMultiplier = 1.8f;
    [SerializeField] private float crouchMultiplier = 0.5f;

    [SerializeField] private bool isPlayer = false;

    private bool isLiving = false;
    private bool isJumping = false;
    private bool isAvailable = false;
    private bool isSprinting = false;
    private bool isCrouching = false;

    private Vector3 velocity;
    [SerializeField] private float gravity = -25f;

    private float maxHealth;
    private float regenTimer = 0f;

    [Header("Estamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float staminaRegenRate = 25f;
    [SerializeField] private float staminaRegenDelay = 1.5f;
    private float stamina;
    private float staminaRegenTimer = 0f;

    public void TakeDamage(float amount)
{
    health -= amount;
    if (health <= 0)
        Die();
}

public void Heal(float amount)
{
    health = Mathf.Min(health + amount, maxHealth);
}

// Restaura vida y estamina al maximo (usado al reaparecer)
public void FullRestore()
{
    health = maxHealth;
    stamina = maxStamina;
}

// --- ESTAMINA ---
public float GetStamina()
{
    return stamina;
}

public float GetMaxStamina()
{
    return maxStamina;
}

// Gasto puntual (salto). Devuelve true si habia suficiente y se consumio.
public bool ConsumeStamina(float amount)
{
    if (stamina < amount) return false;
    stamina -= amount;
    staminaRegenTimer = 0f;
    return true;
}

// Gasto continuo (correr, escalar). Devuelve false cuando ya no queda estamina.
public bool DrainStamina(float amountThisFrame)
{
    if (stamina <= 0f) return false;
    stamina = Mathf.Max(0f, stamina - amountThisFrame);
    staminaRegenTimer = 0f;
    return true;
}

protected virtual void Die()
{
    gameObject.SetActive(false);
}

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
    if (isCrouching) return speed * crouchMultiplier;
    if (isSprinting) return speed * sprintMultiplier;
    return speed;
}

public void SetSpeed(float speed)
{
    this.speed = speed;
}

public void SetIsSprinting(bool sprinting)
{
    this.isSprinting = sprinting;
}

public bool GetIsCrouching()
{
    return isCrouching;
}

public void SetIsCrouching(bool crouching)
{
    this.isCrouching = crouching;
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
        SetIsJumping(false);
    }

    velocity.y += gravity * Time.deltaTime;

    controller.Move(velocity * Time.deltaTime);

}

public void Jump()
{
    if (controller.isGrounded)
    {
        velocity.y = jumpForce;
        SetIsJumping(true);
        Debug.Log("Salto ejecutado");
    }
}

protected virtual void Awake(){

    controller = GetComponent<CharacterController>();
    maxHealth = health;
    stamina = maxStamina;

}

protected virtual void Update(){

    UpdatePassiveRegen();
    UpdateStaminaRegen();

}

private void UpdateStaminaRegen()
{
    if (stamina >= maxStamina) return;

    staminaRegenTimer += Time.deltaTime;
    if (staminaRegenTimer >= staminaRegenDelay)
        stamina = Mathf.Min(maxStamina, stamina + staminaRegenRate * Time.deltaTime);
}

private void UpdatePassiveRegen()
{
    if (this is IPassiveRegenerator regenerator)
    {
        if (regenerator.CanRegenerate())
        {
            regenTimer += Time.deltaTime;
            if (regenTimer >= regenerator.RegenDelay)
                Heal(regenerator.RegenAmountPerSecond * Time.deltaTime);
        }
        else
        {
            regenTimer = 0f;
        }
    }
}

}

