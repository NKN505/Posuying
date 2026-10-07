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

    [Header("Sonido (Wwise)")]
    [Tooltip("Play_Pistol_Shot")]
    [SerializeField] private AK.Wwise.Event shotEvent;
    [Tooltip("Play_Last_Bullet_01: sustituye al disparo normal en la PENULTIMA bala")]
    [SerializeField] private AK.Wwise.Event lastBullet01Event;
    [Tooltip("Play_Last_Bullet_02: sustituye al disparo normal en la ULTIMA bala")]
    [SerializeField] private AK.Wwise.Event lastBullet02Event;
    [Tooltip("Play_Pistol_Dryfire: clic con el cargador vacio")]
    [SerializeField] private AK.Wwise.Event dryFireEvent;
    [Tooltip("Play_Pistol_Reload_Animation")]
    [SerializeField] private AK.Wwise.Event reloadEvent;
    [Tooltip("Play_Pistol_HitMarker: confirma al jugador que ha acertado")]
    [SerializeField] private AK.Wwise.Event hitMarkerEvent;
    [Tooltip("Play_Body_Impact: suena en el enemigo alcanzado")]
    [SerializeField] private AK.Wwise.Event bodyImpactEvent;

    [Header("Interior / exterior (Switch de Wwise)")]
    [Tooltip("Nombre del Switch Group en Wwise")]
    [SerializeField] private string environmentSwitchGroup = "SG_Environment";
    [SerializeField] private string insideSwitch = "Inside";
    [SerializeField] private string outsideSwitch = "Outside";
    [Tooltip("Si hay techo a menos de estos metros por encima, se considera interior")]
    [SerializeField] private float roofCheckDistance = 12.0f;

    [Tooltip("Segundos que vive el emisor temporal del impacto en el cuerpo")]
    [SerializeField] private float bodyImpactEmitterLife = 3.0f;

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
        SetNoAmmo(GetReserveAmmo() <= 0.0f);

        // El "muzzle" del prefab apuntaba a un asset (el prefab de la estela) y
        // no al canon de ESTA pistola, asi que la estela salia de un punto fijo
        // del mundo. Si no es un objeto de la escena, se usa Barrel_Location.
        Transform muzzle = GetMuzzle();
        if (barrelLocation != null && (muzzle == null || !muzzle.gameObject.scene.IsValid()))
        {
            SetMuzzle(barrelLocation);
        }

        // La luz del fogonazo empieza siempre apagada.
        if (muzzleLight != null)
        {
            muzzleLight.enabled = false;
        }

        // Wwise solo reproduce sobre objetos registrados: el arma es el
        // emisor de sus propios sonidos (disparo, recarga, clic en seco).
        if (GetComponent<AkGameObj>() == null)
        {
            gameObject.AddComponent<AkGameObj>();
        }
    }

    protected override void Update()
    {
        // La clase base lee el boton de apuntar y aplica el zoom.
        base.Update();

        // Con una ventana de interfaz abierta, los clics son para la UI.
        // Abatido no se dispara ni se recarga.
        if (UIState.BlocksGameplay || !OwnerCanAct)
        {
            return;
        }

        if (Controles.Pulsado(Accion.Disparar))
        {
            Fire();
        }

        if (Controles.Pulsado(Accion.Recargar))
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
                bool wasReloading = GetIsReloading();
                Reload();

                // Reload() puede negarse (abatido, cambiando de arma...):
                // solo suena si la recarga ha empezado de verdad.
                if (!wasReloading && GetIsReloading())
                {
                    PostWithEnvironment(reloadEvent, gameObject, GetRoom(transform.position));
                }

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

            PostWithEnvironment(dryFireEvent, gameObject, GetRoom(transform.position));

            Debug.Log("Cargador vacio");
            return;
        }

        SetIsShooting(true);

        // Se mira ANTES de restar la bala: las dos ultimas suenan distinto
        float ammoBeforeShot = GetCurrentAmmo();

        SetCurrentAmmo(GetCurrentAmmo() - 1.0f);

        SetIsEmpty(GetCurrentAmmo() <= 0.0f);
        SetIsFull(GetCurrentAmmo() >= GetCapacity());
        SetNoAmmo(GetReserveAmmo() <= 0.0f);

        // Los enemigos con oido tienen que enterarse del disparo (antes solo lo
        // hacia el disparo de PlayerCombat, que ahora no actua con arma en mano)
        MakeShotNoise();

        // ---- LOGICA: el rayo decide que pasa ----
        if (ShootRay(GetCurrentSpread(), out RaycastHit hit, out Vector3 direction))
        {
            // Se mira ANTES del dano: si este tiro lo mata, sigue contando
            // como acierto. Un cadaver (ragdoll) no da hitmarker.
            EnemyBehaviour enemy = hit.collider.GetComponentInParent<EnemyBehaviour>();
            bool enemyWasAlive = enemy != null && enemy.GetHealth() > 0.0f;

            float dano = ApplyDamage(hit, direction);

            if (dano > 0.0f)
            {
                PlayHitSounds(hit, enemy, enemyWasAlive);

                Debug.Log(
                    $"Impacto en {hit.collider.name} a {hit.distance:0.0} m " +
                    $"-> {dano:0} de dano " +
                    $"(x{GetDamageMultiplier(hit.distance):0.00})"
                );
            }
        }

        // ---- PRESENTACION: lo que el jugador ve y oye ----
        PlayShotEffects();
        PlayShotSound(ammoBeforeShot);

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

    //----------------------------------------------
    // SONIDO (WWISE)
    //
    // Aqui solo se lanzan Events: que suena y como lo decide Wwise. Casi
    // todos los sonidos del arma cambian segun se este dentro o fuera
    // (Switch SG_Environment), asi que se fija el Switch justo antes.
    //----------------------------------------------

    private void PlayShotSound(float ammoBeforeShot)
    {
        AK.Wwise.Event evt = shotEvent;

        // Penultima bala -> Last_Bullet_01, ultima -> Last_Bullet_02
        AK.Wwise.Event special = null;
        if (ammoBeforeShot <= 1.0f)
        {
            special = lastBullet02Event;
        }
        else if (ammoBeforeShot <= 2.0f)
        {
            special = lastBullet01Event;
        }

        if (IsAssigned(special))
        {
            evt = special;
        }

        PostWithEnvironment(evt, gameObject, GetRoom(transform.position));

        // Que lo oigan tambien los demas jugadores, desde donde estoy
        var red = GetComponentInParent<NetworkPlayer>();
        if (red != null) red.AvisarDisparo();
    }

    /// <summary>
    /// El disparo de OTRO jugador, tal como se oye en esta maquina. En las copias
    /// remotas esta arma esta apagada (va con las manos de primera persona), asi
    /// que suena sobre el cuerpo del jugador, que es lo que si existe aqui.
    /// </summary>
    public void PlayRemoteShot(GameObject emitter)
    {
        if (emitter == null || !IsAssigned(shotEvent)) return;
        PostWithEnvironment(shotEvent, emitter, GetRoom(emitter.transform.position));
    }

    private void PlayHitSounds(RaycastHit hit, EnemyBehaviour enemy, bool enemyWasAlive)
    {
        if (enemy == null)
        {
            return;
        }

        // Hitmarker: es informacion para quien dispara, suena en el arma.
        // Solo al acertar a un enemigo VIVO.
        if (enemyWasAlive && IsAssigned(hitMarkerEvent))
        {
            hitMarkerEvent.Post(gameObject);
        }

        // Impacto: NO se lanza sobre el enemigo. Si el tiro lo mata, el
        // enemigo se destruye en ese mismo frame y Wwise corta todo lo que
        // sonaba en el. Se usa un emisor temporal en el punto del impacto.
        if (IsAssigned(bodyImpactEvent))
        {
            GameObject emitter = new GameObject("BodyImpact (Wwise)");
            emitter.transform.position = hit.point;
            emitter.AddComponent<AkGameObj>();

            PostWithEnvironment(bodyImpactEvent, emitter, GetRoom(hit.point, enemy.transform));

            Destroy(emitter, bodyImpactEmitterLife);
        }
    }

    private void PostWithEnvironment(AK.Wwise.Event evt, GameObject emitter, WwiseRoomSize room)
    {
        if (!IsAssigned(evt) || emitter == null)
        {
            return;
        }

        // Switch: elige la version seca (Inside) o la de calle (Outside)
        if (!string.IsNullOrEmpty(environmentSwitchGroup))
        {
            AkUnitySoundEngine.SetSwitch(
                environmentSwitchGroup,
                room == WwiseRoomSize.Outside ? outsideSwitch : insideSwitch,
                emitter);
        }

        // Aux Send: la reverb de la sala (fuera, ninguna)
        WwiseRoomAcoustics.ApplyReverb(emitter, room);

        evt.Post(emitter);
    }

    private static bool IsAssigned(AK.Wwise.Event evt)
    {
        return evt != null && evt.IsValid();
    }

    /* Dentro (y que sala) o fuera. Lo mide WwiseRoomAcoustics: techo
    encima = dentro, y el ancho de la sala elige el Aux Bus. Se ignoran los
    colliders del propio jugador y los del enemigo alcanzado. */

    private WwiseRoomSize GetRoom(Vector3 position, Transform alsoIgnore = null)
    {
        return WwiseRoomAcoustics.GetRoom(position, transform, alsoIgnore, roofCheckDistance);
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
