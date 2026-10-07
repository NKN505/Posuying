using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    [Header("Melee")]
    public float meleeDamage = 50f;
    public float meleeRange = 2f;
    public float meleeCooldown = 0.6f;
    [Tooltip("Radio del barrido del golpe: mas grande = mas facil acertar")]
    public float meleeRadius = 0.45f;
    [Tooltip("Estamina que gasta cada golpe")]
    public float meleeStamina = 12f;

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

        // Golpe cuerpo a cuerpo con su propia tecla: vale con el arma en la mano
        // (un culatazo) y es lo que queda cuando se acaban las balas.
        if (Controles.Pulsado(Accion.Golpe) && _meleeTimer <= 0f)
        {
            MeleeAttack();
            return;
        }

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

        // Cansa: sin aliento el golpe sale igual, pero mas flojo
        var yo = GetComponent<Character>();
        bool conFuerza = yo == null || yo.ConsumeStamina(meleeStamina);

        EmpujarArma();

        // Un barrido ancho y no un rayo fino: a un zombi encima se le da aunque
        // no este justo en el centro de la pantalla. Se golpea al mas cercano.
        Vector3 origen = _cam.transform.position, hacia = _cam.transform.forward;
        var golpes = Physics.SphereCastAll(origen - hacia * 0.3f, meleeRadius, hacia, meleeRange + 0.3f,
                                           Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        EnemyBehaviour objetivo = null;
        RaycastHit mejor = default;
        foreach (var g in golpes)
        {
            EnemyBehaviour e = g.collider.GetComponentInParent<EnemyBehaviour>();
            if (e == null || e.IsDead) continue;
            if (objetivo == null || g.distance < mejor.distance) { objetivo = e; mejor = g; }
        }
        // Suena siempre el silbido; el golpe seco, solo si da. Y lo oyen los demas.
        SonidoGolpe.Sonar(origen, objetivo != null, true);
        var red = GetComponent<NetworkPlayer>();
        if (red != null) red.AvisarGolpe(objetivo != null);

        if (objetivo == null) return;

        // SphereCast da punto (0,0,0) si ya estaba dentro al empezar
        Vector3 punto = mejor.point.sqrMagnitude > 0.001f ? mejor.point : objetivo.transform.position + Vector3.up * 0.3f;
        float dano = meleeDamage * (conFuerza ? 1f : 0.5f);

        FeedbackCombate.Impacto(punto, dano, objetivo.GetHealth() - dano <= 0f);

        // El dano lo aplica el servidor (RequestDamage se encarga de pedirlo).
        // Se le pasa por donde entro el golpe y hacia donde iba: es lo que
        // usa el ragdoll para torcer el cuerpo por el sitio correcto.
        objetivo.RequestDamage(dano, punto, hacia);
    }

    // No hay animacion de golpe para los brazos: se le da un empujon al soporte
    // del arma hacia delante y el propio Weapon lo devuelve suavemente a su sitio.
    private Transform _soporteArma;

    private void EmpujarArma()
    {
        if (_soporteArma == null && _cam != null) _soporteArma = _cam.transform.Find("Weapon Holder");
        if (_soporteArma == null) return;

        _soporteArma.localPosition += new Vector3(-0.05f, -0.02f, 0.24f);
        _soporteArma.localRotation *= Quaternion.Euler(-14f, 22f, 8f);
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
