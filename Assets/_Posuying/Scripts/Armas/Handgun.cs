using UnityEngine;

public class Handgun : Weapon
{
    // ---------------------------------------------------------------
    // EFECTOS DEL ARMA
    //
    // El pack de Nokobot trae el fogonazo, el casquillo y la animacion
    // de corredera ya hechos. Aqui se enganchan todos desde Fire(), en
    // vez de usar los eventos de animacion que trae su demo: esos solo
    // llegan a componentes que esten en el MISMO GameObject que el
    // Animator, y este script vive en el arma, no en el modelo.
    // ---------------------------------------------------------------

    [Header("Referencias del arma")]
    [Tooltip("Animator del M1911 (el del prefab, con el parametro Fire).")]
    [SerializeField] private Animator gunAnimator;

    [Tooltip("Barrel_Location del prefab: de aqui sale el fogonazo.")]
    [SerializeField] private Transform barrelLocation;

    [Tooltip("CasingExit del prefab: por aqui sale el casquillo.")]
    [SerializeField] private Transform casingExitLocation;

    [Header("Prefabs de efectos")]
    [Tooltip("Effects/MuzzleFlash.prefab del pack de Nokobot.")]
    [SerializeField] private GameObject muzzleFlashPrefab;

    [Tooltip("_Prefabs/45ACP Bullet_Casing.prefab del pack de Nokobot.")]
    [SerializeField] private GameObject casingPrefab;

    [Tooltip("Point Light hija de Barrel_Location, APAGADA por defecto. " +
             "En un juego de terror esto vale mas que el propio fogonazo: " +
             "es lo que ilumina el pasillo durante dos fotogramas.")]
    [SerializeField] private Light muzzleLight;

    [Header("Ajustes de efectos")]
    [SerializeField] private float effectsLifetime = 2.0f;
    [SerializeField] private float ejectPower = 150.0f;
    [SerializeField] private float muzzleLightIntensity = 8.0f;
    [SerializeField] private float muzzleLightTime = 0.05f;

    // StringToHash una vez en vez de buscar por cadena en cada disparo.
    private static readonly int HashFire = Animator.StringToHash("Fire");


    protected override void Awake()
    {
        // La clase base captura el desfase del holder y el FOV de reposo.
        base.Awake();

        // Municion
        SetCapacity(15.0f);
        SetCurrentAmmo(15.0f);
        SetReserveAmmo(45.0f);

        // Estadisticas
        SetDamage(100.0f);
        SetCadency(0.3f);

        // Alcance maximo del rayo. El alcance EFECTIVO (donde el dano deja
        // de ser integro) se ajusta en el inspector: Effective Range.
        SetScope(50.0f);

        // Estados iniciales
        SetIsEmpty(false);
        SetIsFull(true);
        SetIsShooting(false);
        SetIsReloading(false);
        SetIsSwitchingWeapon(false);
        SetNoAmmo(true);

        // La luz del fogonazo empieza siempre apagada.
        if (muzzleLight != null)
        {
            muzzleLight.enabled = false;
        }
    }

    protected override void Update()
    {
        // La clase base lee el boton de apuntar y aplica el zoom.
        base.Update();

        // Con una ventana de interfaz abierta, los clics son para la UI.
        if (UIState.BlocksGameplay)
        {
            return;
        }

        if (Input.GetButtonDown("Fire"))
        {
            Fire();
        }

        if (Input.GetButtonDown("Reload-Interact"))
        {
            if (GetReserveAmmo() <= 0.0f)
            {
                Debug.Log("Sin municion de reserva");
            }
            else if (GetIsFull())
            {
                Debug.Log("El cargador ya esta lleno");
            }
            else
            {
                Reload();
                Debug.Log("RELOADING");
            }
        }
    }

    public override void Fire()
    {
        if (GetIsShooting() ||
            GetIsReloading() ||
            GetIsSwitchingWeapon())
        {
            return;
        }

        if (GetCurrentAmmo() <= 0.0f)
        {
            SetIsEmpty(true);
            SetIsFull(false);
            SetIsShooting(false);

            // Clic en seco: el arma no dispara pero SI suena y mueve el
            // martillo. Que el jugador oiga que esta vacio es informacion.
            if (gunAnimator != null)
            {
                gunAnimator.SetTrigger(HashFire);
            }

            Debug.Log("Cargador vacio");
            return;
        }

        SetIsShooting(true);

        SetCurrentAmmo(GetCurrentAmmo() - 1.0f);

        SetIsEmpty(GetCurrentAmmo() <= 0.0f);
        SetIsFull(GetCurrentAmmo() >= GetCapacity());
        SetNoAmmo(GetReserveAmmo() <= 0.0f);

        // ---- LOGICA: el rayo decide que pasa ----
        if (ShootRay(GetCurrentSpread(), out RaycastHit hit, out Vector3 direction))
        {
            float dano = ApplyDamage(hit, direction);

            if (dano > 0.0f)
            {
                Debug.Log(
                    $"Impacto en {hit.collider.name} a {hit.distance:0.0} m " +
                    $"-> {dano:0} de dano " +
                    $"(x{GetDamageMultiplier(hit.distance):0.00})"
                );
            }
        }

        // ---- PRESENTACION: lo que el jugador ve y oye ----
        PlayShotEffects();

        CancelInvoke(nameof(StopShooting));
        Invoke(nameof(StopShooting), GetCadency());
    }


    //----------------------------------------------
    // EFECTOS
    //----------------------------------------------

    private void PlayShotEffects()
    {
        // Brazos: Fp_Pistol_Shoot (trigger Shoot en el Animator de fpArms)
        PlayShootAnimation();

        // Arma: corredera, martillo y gatillo
        PlayGunAnimation();

        SpawnMuzzleFlash();
        SpawnCasing();
        FlashMuzzleLight();
    }

    // Mueve corredera, martillo y gatillo. El clip dura 0.23 s, asi que
    // encaja de sobra dentro de la cadencia de 0.3.
    private void PlayGunAnimation()
    {
        if (gunAnimator == null)
        {
            return;
        }

        gunAnimator.SetTrigger(HashFire);
    }

    /* El fogonazo se instancia COMO HIJO del canon.

    Nokobot lo suelta en el mundo, que vale para un arma de tercera
    persona. En primera persona el arma se mueve mucho (sway, retroceso,
    apuntado) y un fogonazo suelto se quedaria flotando detras. */

    private void SpawnMuzzleFlash()
    {
        if (muzzleFlashPrefab == null || barrelLocation == null)
        {
            return;
        }

        GameObject flash = Instantiate(
            muzzleFlashPrefab,
            barrelLocation.position,
            barrelLocation.rotation,
            barrelLocation
        );

        Destroy(flash, effectsLifetime);
    }

    /* El casquillo SI va suelto en el mundo: tiene que caer al suelo y
    quedarse ahi, no seguir al arma.

    La fuerza lleva algo de aleatoriedad para que dos disparos seguidos no
    expulsen el casquillo exactamente igual. */

    private void SpawnCasing()
    {
        if (casingPrefab == null || casingExitLocation == null)
        {
            return;
        }

        GameObject casing = Instantiate(
            casingPrefab,
            casingExitLocation.position,
            casingExitLocation.rotation
        );

        if (casing.TryGetComponent(out Rigidbody rb))
        {
            Vector3 pushOrigin = casingExitLocation.position
                                 - casingExitLocation.right * 0.3f
                                 - casingExitLocation.up * 0.6f;

            rb.AddExplosionForce(
                Random.Range(ejectPower * 0.7f, ejectPower),
                pushOrigin,
                1.0f
            );

            rb.AddTorque(
                new Vector3(0.0f,
                            Random.Range(100.0f, 500.0f),
                            Random.Range(100.0f, 1000.0f)),
                ForceMode.Impulse
            );
        }

        Destroy(casing, effectsLifetime);
    }

    // Destello corto. Se apaga con Invoke en vez de con una corrutina
    // para poder cancelarlo limpiamente si se dispara en rafaga.
    private void FlashMuzzleLight()
    {
        if (muzzleLight == null)
        {
            return;
        }

        muzzleLight.intensity = muzzleLightIntensity;
        muzzleLight.enabled = true;

        CancelInvoke(nameof(TurnOffMuzzleLight));
        Invoke(nameof(TurnOffMuzzleLight), muzzleLightTime);
    }

    private void TurnOffMuzzleLight()
    {
        if (muzzleLight != null)
        {
            muzzleLight.enabled = false;
        }
    }

    // Si el arma se guarda a mitad de disparo, que no se quede la luz
    // encendida ni el flag de disparo colgado.
    protected override void OnDisable()
    {
        base.OnDisable();

        CancelInvoke(nameof(TurnOffMuzzleLight));
        TurnOffMuzzleLight();
    }
}
