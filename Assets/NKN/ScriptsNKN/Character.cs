using Unity.Netcode;
using UnityEngine;

public class Character : NetworkBehaviour{

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

    // La vida vive "en la red": solo el servidor puede escribirla y todos la leen,
    // asi los dos jugadores ven exactamente la misma vida de cada personaje.
    private readonly NetworkVariable<float> netHealth = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netMaxHealth = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float regenTimer = 0f;

    // Datos del golpe que te mato, guardados por el servidor para pasarselos al
    // ragdoll. No se replican solos: viajan dentro del ClientRpc de la muerte.
    private Vector3 _puntoUltimoGolpe;
    private Vector3 _direccionUltimoGolpe;
    private float _sobredanoUltimoGolpe;

    private RagdollDeath _ragdoll;
    private bool _ragdollBuscado;

    [Header("Estamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float staminaRegenRate = 25f;
    [SerializeField] private float staminaRegenDelay = 1.5f;
    private float stamina;
    private float staminaRegenTimer = 0f;

    // El servidor es el unico que decide la vida de todos.
public override void OnNetworkSpawn()
{
    if (IsServer && netMaxHealth.Value <= 0f)
    {
        netMaxHealth.Value = health;   // 'health' es el valor puesto en el Inspector
        netHealth.Value = health;
    }
}

public void TakeDamage(float amount)
{
    TakeDamage(amount, Vector3.zero, Vector3.zero);
}

// Igual que la anterior pero sabiendo POR DONDE te entro el golpe. El punto y la
// direccion no cambian el dano: solo alimentan al ragdoll. Un cero en cualquiera
// de los dos significa "sin datos", y entonces el ragdoll se apana con el pecho
// y con la espalda.
public void TakeDamage(float amount, Vector3 puntoImpacto, Vector3 direccionImpacto)
{
    if (!IsServer) return;   // solo el servidor aplica dano

    float vidaAntes = netHealth.Value;
    netHealth.Value -= amount;

    // El 'vidaAntes > 0' evita que los golpes que sigan cayendo sobre un cuerpo
    // ya muerto vuelvan a llamar a Die() una y otra vez.
    if (netHealth.Value <= 0f && vidaAntes > 0f)
    {
        // Sobredano: lo que sobro del golpe despues de dejarte a cero. Un tiro
        // justo da 0 y el cuerpo se desploma donde esta; una escopeta a
        // bocajarro da mucho y te manda por los aires.
        _sobredanoUltimoGolpe   = Mathf.Max(0f, amount - Mathf.Max(0f, vidaAntes));
        _puntoUltimoGolpe       = puntoImpacto;
        _direccionUltimoGolpe   = direccionImpacto;

        Die();
    }
}

// Un cliente no puede tocar la vida directamente: se la pide al servidor.
// RequireOwnership = false porque disparamos a enemigos que no son nuestros.
[ServerRpc(RequireOwnership = false)]
public void TakeDamageServerRpc(float amount)
{
    TakeDamage(amount);
}

// Nombre distinto en vez de una sobrecarga: Netcode genera el codigo de los RPC
// a partir del nombre del metodo, y dos RPC que se llamen igual no compilan.
[ServerRpc(RequireOwnership = false)]
public void TakeDamageWithHitServerRpc(float amount, Vector3 puntoImpacto, Vector3 direccionImpacto)
{
    TakeDamage(amount, puntoImpacto, direccionImpacto);
}

public void Heal(float amount)
{
    if (!IsServer) return;

    netHealth.Value = Mathf.Min(netHealth.Value + amount, GetMaxHealth());
}

[ServerRpc(RequireOwnership = false)]
private void HealServerRpc(float amount)
{
    Heal(amount);
}

// Punto de entrada publico para curar (botiquines, objetos del inventario)
public void RequestHeal(float amount)
{
    if (IsServer) Heal(amount);
    else HealServerRpc(amount);
}

// Punto de entrada para hacer dano a ESTE personaje desde cualquier sitio.
// Si somos el servidor se aplica directo; si somos un cliente, se lo pedimos.
public void RequestDamage(float amount)
{
    if (IsServer) TakeDamage(amount);
    else TakeDamageServerRpc(amount);
}

// Igual, pero pasando de donde vino el golpe para que el ragdoll lo use.
public void RequestDamage(float amount, Vector3 puntoImpacto, Vector3 direccionImpacto)
{
    if (IsServer) TakeDamage(amount, puntoImpacto, direccionImpacto);
    else TakeDamageWithHitServerRpc(amount, puntoImpacto, direccionImpacto);
}

// Cura funcionando tanto si somos el servidor como si somos un cliente
protected void ApplyHeal(float amount)
{
    if (IsServer) Heal(amount);
    else if (IsOwner) HealServerRpc(amount);
}

// Restaura vida y estamina al maximo (usado al reaparecer)
public void FullRestore()
{
    if (IsServer)
        netHealth.Value = GetMaxHealth();

    stamina = maxStamina;   // la estamina es local de cada jugador
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

// --------------------------------------------------------------------------
// MUERTE CON RAGDOLL
// --------------------------------------------------------------------------
//
// Quien decide es el SERVIDOR: elige el tipo de muerte, y manda a todos ese
// numero junto con el punto y la fuerza del golpe. Cada cliente simula su
// propio ragdoll con esos datos.
//
// NO se sincroniza hueso por hueso. Serian 11 transforms por cadaver y por
// frame; y no hace falta, porque partiendo del mismo clip, del mismo punto de
// impacto y del mismo impulso, todas las maquinas ven una caida practicamente
// igual. Que un brazo acabe unos centimetros distinto no lo nota nadie.

private RagdollDeath Ragdoll
{
    get
    {
        if (!_ragdollBuscado)
        {
            _ragdoll = GetComponent<RagdollDeath>();
            _ragdollBuscado = true;
        }
        return _ragdoll;
    }
}

/// <summary>
/// Lanza la muerte con ragdoll en todas las maquinas. Solo el servidor.
/// Devuelve false si este personaje no tiene ragdoll montado, para que quien
/// llame pueda seguir con el comportamiento de siempre.
/// </summary>
public bool MorirConRagdoll()
{
    if (!IsServer || Ragdoll == null) return false;

    int tipo = Ragdoll.ElegirTipoMuerte();

    // Sin direccion conocida, el cuerpo cae hacia atras: es lo que hace alguien
    // al que le disparan de frente, que es el caso normal.
    Vector3 direccion = _direccionUltimoGolpe.sqrMagnitude > 0.0001f
                      ? _direccionUltimoGolpe
                      : -transform.forward;

    MorirConRagdollClientRpc(tipo, _puntoUltimoGolpe, direccion, _sobredanoUltimoGolpe);
    Ragdoll.Matar(tipo, _puntoUltimoGolpe, direccion, _sobredanoUltimoGolpe);
    return true;
}

[ClientRpc]
private void MorirConRagdollClientRpc(int tipo, Vector3 punto, Vector3 direccion, float sobredano)
{
    // El host es servidor Y cliente: ya lo ha hecho arriba, no repetir.
    if (IsServer) return;

    if (Ragdoll != null) Ragdoll.Matar(tipo, punto, direccion, sobredano);
}

/// <summary>Deshace el ragdoll en todas las maquinas (reanimacion, reinicio).</summary>
public void LevantarDeRagdoll()
{
    if (!IsServer || Ragdoll == null) return;

    LevantarDeRagdollClientRpc();
    Ragdoll.Levantar();
}

[ClientRpc]
private void LevantarDeRagdollClientRpc()
{
    if (IsServer) return;
    if (Ragdoll != null) Ragdoll.Levantar();
}

public float GetHealth()
{
    // Antes de existir en la red usamos el valor del Inspector
    return IsSpawned ? netHealth.Value : health;
}

public float GetMaxHealth()
{
    return (IsSpawned && netMaxHealth.Value > 0f) ? netMaxHealth.Value : health;
}

public void SetHealth(float health)
{
    if (!IsServer) return;
    netHealth.Value = health;
}

// Fija vida maxima y actual a la vez (lo usa el director de hordas al crear enemigos)
public void SetMaxHealth(float value)
{
    if (!IsServer) return;
    netMaxHealth.Value = value;
    netHealth.Value = value;
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
                ApplyHeal(regenerator.RegenAmountPerSecond * Time.deltaTime);
        }
        else
        {
            regenTimer = 0f;
        }
    }
}

}

