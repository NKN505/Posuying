using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    [Header("Melee")]
    public float meleeDamage = 50f;
    public float meleeRange = 2f;
    public float meleeCooldown = 0.5f;

    [Header("Disparo")]
    public float shootDamage = 30f;
    public float shootRange = 50f;
    public float shootCooldown = 0.3f;

    [Tooltip("Camara de ESTE jugador. Si se deja vacia se busca entre sus hijos.")]
    public Camera playerCamera;

    private float _meleeTimer = 0f;
    private float _shootTimer = 0f;
    private Camera _cam;
    private PlayerNoise _ruido;
    private PlayerDownedState _abatido;
    private Weapon _arma;

    void Start()
    {
        // Su propia camara, no Camera.main (que en red puede ser la de otro jugador)
        _cam = playerCamera != null ? playerCamera : GetComponentInChildren<Camera>(true);
        _ruido = GetComponent<PlayerNoise>();
        _abatido = GetComponent<PlayerDownedState>();
    }

    void Update()
    {
        // Con una ventana abierta, los clics son para la interfaz (no disparar)
        if (UIState.BlocksGameplay) return;

        if (_meleeTimer > 0f) _meleeTimer -= Time.deltaTime;
        if (_shootTimer > 0f) _shootTimer -= Time.deltaTime;

        // Abatido o fuera de combate no se pelea
        if (_abatido != null && !_abatido.CanAct) return;

        // Con un arma en la mano, el raton es del arma (Fire = clic izquierdo,
        // Aim = clic derecho). Antes este script actuaba a la vez: cada disparo
        // daba tambien un golpe de mele y apuntar lanzaba un disparo invisible.
        if (TieneArmaActiva()) return;

        if (Input.GetMouseButtonDown(0) && _meleeTimer <= 0f)
            MeleeAttack();

        if (Input.GetMouseButtonDown(1) && _shootTimer <= 0f)
            Shoot();
    }

    // Mismo criterio que PlayerController: el arma activa es la que esta
    // encendida en la jerarquia, y solo se busca cuando la anterior se apaga.
    private bool TieneArmaActiva()
    {
        if (_arma == null || !_arma.isActiveAndEnabled)
            _arma = GetComponentInChildren<Weapon>(false);

        return _arma != null && _arma.isActiveAndEnabled;
    }

    void MeleeAttack()
    {
        _meleeTimer = meleeCooldown;

        // Un golpe suena poco, pero suena
        if (_ruido != null) _ruido.MakeMelee();

        RaycastHit hit;
        if (Physics.Raycast(_cam.transform.position, _cam.transform.forward, out hit, meleeRange))
        {
            EnemyBehaviour enemy = hit.collider.GetComponentInParent<EnemyBehaviour>();
            if (enemy != null)
            {
                // El dano lo aplica el servidor (RequestDamage se encarga de pedirlo).
                // Se le pasa por donde entro el golpe y hacia donde iba: es lo que
                // usa el ragdoll para torcer el cuerpo por el sitio correcto.
                enemy.RequestDamage(meleeDamage, hit.point, _cam.transform.forward);
                Debug.Log("Golpe melee a " + hit.collider.name);
            }
        }
    }

    void Shoot()
    {
        _shootTimer = shootCooldown;

        // Lo más ruidoso que puede hacer el jugador, con diferencia
        if (_ruido != null) _ruido.MakeShot();

        RaycastHit hit;
        if (Physics.Raycast(_cam.transform.position, _cam.transform.forward, out hit, shootRange))
        {
            EnemyBehaviour enemy = hit.collider.GetComponentInParent<EnemyBehaviour>();
            if (enemy != null)
            {
                enemy.RequestDamage(shootDamage, hit.point, _cam.transform.forward);
                Debug.Log("Disparo a " + hit.collider.name);
            }
        }
    }
}
