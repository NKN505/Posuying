using System.Collections;
using UnityEngine;

/// <summary>
/// Como se sostiene el arma. Decide QUE SET de animaciones usa el cuerpo.
/// Es un enum y no booleanos a proposito: con booleanos serian posibles
/// estados invalidos (pistola y fusil a la vez) y cada arma nueva obligaria
/// a tocar todas las demas.
/// </summary>
public enum WeaponGrip
{
    Unarmed   = 0,   // manos vacias
    OneHanded = 1,   // pistola 
    TwoHanded = 2,   // fusil, escopeta, rifle
    Melee     = 3,   // cuchillo
}

public abstract class Weapon : MonoBehaviour{

    // ---------------------------
    // ANIMACION
    // ---------------------------

    [Header("Animacion")]
    [Tooltip("Set de animaciones de cuerpo que usa esta arma. Por defecto Unarmed " +
             "a proposito: si se olvida ponerlo, el personaje anima con las manos " +
             "vacias, que es un fallo evidente, en vez de con un arma que no lleva.")]
    [SerializeField] private WeaponGrip grip = WeaponGrip.Unarmed;

    // ---------------------------
    // APUNTADO Y ZOOM (comun a todas las armas)
    // ---------------------------

    [Header("Posiciones del arma")]
    [SerializeField] private Transform weaponHolder;
    [SerializeField] private Transform hipPosition;
    [SerializeField] private Transform aimPosition;

    [Header("Apuntado")]
    // Desactivar en armas que no apuntan (cuchillo, linterna, objetos
    // arrojadizos). Si esta en false, Aim() y Zoom() no hacen nada.
    [SerializeField] private bool canAim = true;
    [SerializeField] private float aimSpeed = 10.0f;

    [Header("Zoom al apuntar")]
    // Si se deja vacio se usa Camera.main automaticamente.
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float aimFieldOfView = 48.0f;
    [SerializeField] private float zoomSpeed = 10.0f;

    // ---------------------------
    // DISPARO
    // ---------------------------

    [Header("Disparo")]
    // Capas que el rayo PUEDE tocar. Hay que desmarcar la capa del jugador
    // y la del arma, o el rayo impactara en las propias manos a 40 cm.
    [SerializeField] private LayerMask hittableLayers;

    // Punta del canon. NO se usa para la logica (el rayo sale de la camara),
    // solo como origen visual de la estela.
    [SerializeField] private Transform muzzle;

    [Header("Dispersion")]
    // Desviacion maxima en GRADOS. Apuntando debe ser mucho menor que en
    // cadera: es lo que convierte el zoom en una mecanica y no en un adorno.
    [SerializeField] private float hipSpread = 2.0f;
    [SerializeField] private float aimSpread = 0.2f;

    [Tooltip("Agachado la dispersion se multiplica por esto (menos de 1 = mas preciso)")]
    [SerializeField] private float crouchSpreadMultiplier = 0.5f;

    [Header("Dispersion segun el movimiento (solo sin apuntar)")]
    [Tooltip("De pie y quieto: algo mas preciso que andando")]
    [SerializeField] private float stillSpreadMultiplier = 0.7f;
    [Tooltip("Andando")]
    [SerializeField] private float walkSpreadMultiplier = 1.25f;
    [Tooltip("Corriendo")]
    [SerializeField] private float sprintSpreadMultiplier = 2.0f;
    [Tooltip("Velocidad (m/s) a partir de la cual cuenta como andar del todo")]
    [SerializeField] private float walkSpeedForSpread = 2.5f;

    [Header("Dispersion acumulada al disparar seguido sin apuntar")]
    [Tooltip("Grados que se suman al cono por cada disparo sin apuntar")]
    [SerializeField] private float spreadPerShot = 0.6f;
    [Tooltip("Tope de grados acumulados")]
    [SerializeField] private float maxExtraSpread = 4.0f;
    [Tooltip("Segundos sin disparar antes de que el cono empiece a cerrarse")]
    [SerializeField] private float spreadRecoveryDelay = 0.25f;
    [Tooltip("Grados por segundo que se recuperan")]
    [SerializeField] private float spreadRecoveryPerSecond = 5.0f;

    [Header("Caida de dano por distancia")]
    [Tooltip("Hasta esta distancia el arma hace el 100% del dano. Mas alla " +
             "empieza a caer hasta el alcance maximo (scope).")]
    [SerializeField] private float effectiveRange = 10.0f;

    [Tooltip("Multiplicador de dano en el alcance maximo. NO conviene ponerlo " +
             "a 0: un impacto que suena, se ve y no hace nada se lee como un " +
             "bug. Un 10-15% comunica 'le has dado, pero desde aqui no sirve'.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float minDamageMultiplier = 0.15f;

    [Tooltip("Forma de la caida entre effectiveRange y scope. X = 0 es el " +
             "alcance efectivo, X = 1 el alcance maximo. Y = 0 es dano pleno, " +
             "Y = 1 es dano minimo. Recta = caida progresiva; una curva de " +
             "acantilado comunica mejor un limite claro al jugador.")]
    [SerializeField]
    private AnimationCurve falloffShape = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Estela")]
    [SerializeField] private BulletTracer tracerPrefab;

    // ---------------------------
    // ANIMACION DE LOS BRAZOS (fpArms)
    // ---------------------------

    [Header("Brazos de primera persona")]
    [Tooltip("Animator del objeto fpArms. Parametros esperados: " +
             "Shoot (Trigger), Reload (Trigger), Sprinting (Bool).")]
    [SerializeField] private Animator armsAnimator;

    [Tooltip("Duracion del clip Fp_Pistol_Reloading en SEGUNDOS. " +
             "48 fotogramas a 24 fps = 2.0 s. Si cambias el clip en " +
             "Blender, actualiza este numero o la municion entrara " +
             "antes o despues de lo que se ve.")]
    [SerializeField] private float reloadDuration = 2.0f;

    [Tooltip("En que punto de la recarga (0-1) entran las balas al " +
             "cargador. 0.75 = cuando el cargador nuevo ya esta dentro, " +
             "antes de montar la corredera.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float reloadAmmoPoint = 0.75f;

    // Hashes cacheados: StringToHash una vez, no en cada llamada.
    private static readonly int HashShoot = Animator.StringToHash("Shoot");
    private static readonly int HashReload = Animator.StringToHash("Reload");
    private static readonly int HashSprinting = Animator.StringToHash("Sprinting");

    private Coroutine reloadRoutine;

    [Header("Depuracion")]
    // Dibuja el rayo en la vista Scene (y en Game si activas el boton
    // Gizmos). Rojo = impacto, amarillo = fallo.
    [SerializeField] private bool drawDebugRay = true;
    [SerializeField] private float debugRayDuration = 2.0f;

    // ---------------------------
    // ESTADO INTERNO DEL APUNTADO
    // ---------------------------

    private bool isAiming = false;

    // FOV de reposo. Se lee SIEMPRE de Opciones (GameSettings.FieldOfView).
    // Antes se copiaba de la camara una sola vez en Awake, y si cambiabas el FOV
    // en Opciones con el arma en la mano, el zoom lo devolvia al valor viejo.
    private static float DefaultFieldOfView => GameSettings.FieldOfView;

    // Jugador que lleva el arma. Sirve para que el rayo no se pare en su propio
    // cuerpo, para no poder disparar estando abatido y para avisar del ruido.
    private Transform ownerRoot;
    private PlayerDownedState ownerDowned;
    private PlayerController ownerController;
    private CharacterController ownerBody;
    private bool ownerSprinting;

    // Dispersion acumulada: se guarda el valor en el ultimo disparo y el momento,
    // y la recuperacion se calcula al leerla (no hace falta un Update para esto).
    private float extraSpread;
    private float lastShotTime = -999f;
    private int lastShotFrame = -1;
    private PlayerNoise ownerNoise;

    // El jugador puede usar el arma (no esta abatido ni fuera de combate)
    protected bool OwnerCanAct => ownerDowned == null || ownerDowned.CanAct;

    /* Desfase entre el PIVOTE del WeaponHolder y el marcador de cadera.
    Existe porque los modelos de manos y arma estan desplazados dentro del
    holder, asi que el pivote no coincide con lo que se ve en pantalla.
    Lo capturamos una vez en Awake y lo aplicamos a CUALQUIER marcador,
    de modo que apuntar mueve el arma la misma distancia relativa que
    habiamos ajustado a ojo en cadera. */

    private Vector3 holderPositionOffset;
    private Quaternion holderRotationOffset;

    // ---------------------------
    // ESTADISTICAS Y ESTADO
    // ---------------------------

    private float capacity;
    private float currentAmmo; //Balas en el cargador
    private float reserveAmmo;

    private float damage;
    private float cadency;
    private float scope;

    private bool isEmpty = false;
    private bool isFull = false;
    private bool isShooting = false;
    private bool isReloading = false;
    private bool isSwitchingWeapon = false;
    private bool noAmmo = false;

    private bool usingSoftAmmo = false;
    private bool usingMediumAmmo = false;
    private bool usingHardAmmo = false;
    private bool usingSpecialAmmo = false;

    // ---------------------------
    // GETTERS
    // ---------------------------

    public float GetCapacity()
    {
        return capacity;
    }

    public float GetDamage()
    {
        return damage;
    }

    public float GetCadency()
    {
        return cadency;
    }

    public float GetScope()
    {
        return scope;
    }

    public bool GetIsEmpty()
    {
        return isEmpty;
    }

    public bool GetIsFull()
    {
        return isFull;
    }

    public bool GetIsShooting()
    {
        return isShooting;
    }

    public bool GetIsReloading()
    {
        return isReloading;
    }

    public bool GetIsSwitchingWeapon()
    {
        return isSwitchingWeapon;
    }

    public bool GetNoAmmo()
    {
        return noAmmo;
    }

    public bool GetUsingSoftAmmo()
    {
        return usingSoftAmmo;
    }

    public bool GetUsingMediumAmmo()
    {
        return usingMediumAmmo;
    }

    public bool GetUsingHardAmmo()
    {
        return usingHardAmmo;
    }

    public bool GetUsingSpecialAmmo()
    {
        return usingSpecialAmmo;
    }

    public bool GetIsAiming()
    {
        return isAiming;
    }

    public bool GetCanAim()
    {
        return canAim;
    }

    public WeaponGrip GetGrip()
    {
        return grip;
    }

    public Camera GetPlayerCamera()
    {
        return playerCamera;
    }

    public Transform GetMuzzle()
    {
        return muzzle;
    }

    protected void SetMuzzle(Transform muzzle)
    {
        this.muzzle = muzzle;
    }

    public LayerMask GetHittableLayers()
    {
        return hittableLayers;
    }

    // Dispersion activa segun el estado de apuntado.
    // Es el cono REAL del siguiente disparo; la reticula del HUD dibuja este valor.
    public float GetCurrentSpread()
    {
        float spread = (isAiming ? aimSpread : hipSpread) + GetExtraSpread();

        // Moverse abre el cono: quieto < andando < corriendo. Apuntando no
        // cuenta: quien apunta ya anda despacio y la mira manda.
        if (!isAiming)
        {
            spread *= GetMovementSpreadMultiplier();
        }

        if (ownerController != null && ownerController.GetIsCrouching())
        {
            spread *= crouchSpreadMultiplier;
        }

        return spread;
    }

    // Se usa la velocidad real del cuerpo y no las teclas: asi empujarse contra
    // una pared (teclas pulsadas, cuerpo parado) no abre el cono.
    private float GetMovementSpreadMultiplier()
    {
        if (ownerSprinting)
        {
            return sprintSpreadMultiplier;
        }

        if (ownerBody == null)
        {
            return 1.0f;
        }

        Vector3 velocity = ownerBody.velocity;
        velocity.y = 0.0f;

        float moving = Mathf.Clamp01(velocity.magnitude / Mathf.Max(0.1f, walkSpeedForSpread));

        return Mathf.Lerp(stillSpreadMultiplier, walkSpreadMultiplier, moving);
    }

    // Lo acumulado por disparar seguido, ya descontado lo recuperado desde el ultimo tiro.
    private float GetExtraSpread()
    {
        float resting = Time.time - lastShotTime - spreadRecoveryDelay;

        return resting <= 0.0f
            ? extraSpread
            : Mathf.Max(0.0f, extraSpread - resting * spreadRecoveryPerSecond);
    }

    // Cada disparo sin apuntar abre un poco mas el cono. Una vez por frame como
    // mucho: la escopeta lanza varios rayos en el mismo disparo y cuenta como uno.
    private void AccumulateSpread()
    {
        if (isAiming || lastShotFrame == Time.frameCount)
        {
            return;
        }

        extraSpread = Mathf.Min(maxExtraSpread, GetExtraSpread() + spreadPerShot);
        lastShotTime = Time.time;
        lastShotFrame = Time.frameCount;
    }

    // ---------------------------
    // SETTERS
    // ---------------------------

    public void SetCapacity(float capacity)
    {
        this.capacity = capacity;
    }

    public void SetDamage(float damage)
    {
        this.damage = damage;
    }

    public void SetCadency(float cadency)
    {
        this.cadency = cadency;
    }

    public void SetScope(float scope)
    {
        this.scope = scope;
    }

    public void SetIsEmpty(bool isEmpty)
    {
        this.isEmpty = isEmpty;
    }

    public void SetIsFull(bool isFull)
    {
        this.isFull = isFull;
    }

    public void SetIsShooting(bool isShooting)
    {
        this.isShooting = isShooting;
    }

    public void SetIsReloading(bool isReloading)
    {
        this.isReloading = isReloading;
    }

    public void SetIsSwitchingWeapon(bool isSwitchingWeapon)
    {
        this.isSwitchingWeapon = isSwitchingWeapon;
    }

    public void SetNoAmmo(bool noAmmo)
    {
        this.noAmmo = noAmmo;
    }

    public void SetUsingSoftAmmo(bool usingSoftAmmo)
    {
        this.usingSoftAmmo = usingSoftAmmo;
    }

    public void SetUsingMediumAmmo(bool usingMediumAmmo)
    {
        this.usingMediumAmmo = usingMediumAmmo;
    }

    public void SetUsingHardAmmo(bool usingHardAmmo)
    {
        this.usingHardAmmo = usingHardAmmo;
    }

    public void SetUsingSpecialAmmo(bool usingSpecialAmmo)
    {
        this.usingSpecialAmmo = usingSpecialAmmo;
    }

    public void SetCanAim(bool canAim)
    {
        this.canAim = canAim;
    }

    public void SetHipSpread(float hipSpread)
    {
        this.hipSpread = hipSpread;
    }

    public void SetAimSpread(float aimSpread)
    {
        this.aimSpread = aimSpread;
    }

    protected void SetIsAiming(bool isAiming)
    {
        this.isAiming = isAiming;
    }


    public abstract void Fire();

    protected void StopShooting()
    {
        isShooting = false;
    }

    /* La recarga ya NO es instantanea: dura lo que dura la animacion.

    Antes este metodo ponia isReloading a true, rellenaba el cargador y lo
    volvia a poner a false en el mismo frame. Con un clip de 2 segundos eso
    significaria recargar y poder disparar al instante mientras las manos
    siguen su teatro. */

    public void Reload()
    {
        // Abatido no se recarga (y la R es la tecla de gastar una vida)
        if (!OwnerCanAct)
        {
            return;
        }

        if (isReloading || isSwitchingWeapon)
        {
            return;
        }

        if (isFull)
        {
            return;
        }

        if (noAmmo)
        {
            return;
        }

        if (!isActiveAndEnabled)
        {
            // Sin objeto activo no se puede lanzar corrutina: recarga seca.
            FillMagazine();
            return;
        }

        reloadRoutine = StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        isReloading = true;

        Debug.Log(isEmpty
            ? "Recarga desde vacío: cargador vacío"
            : "Recarga táctica: aún queda munición en el cargador");

        PlayReloadAnimation();

        // Las balas entran a mitad de animacion, no al principio: cuando el
        // cargador nuevo ya esta dentro segun lo que ve el jugador.
        float ammoDelay = reloadDuration * reloadAmmoPoint;

        yield return new WaitForSeconds(ammoDelay);

        FillMagazine();

        // El resto del clip: montar la corredera y volver a la pose.
        yield return new WaitForSeconds(reloadDuration - ammoDelay);

        isReloading = false;
        reloadRoutine = null;
    }

    // Corta la recarga a medias (cambio de arma, muerte, guardar el arma).
    // Sin esto el arma se quedaria con isReloading pegado a true.
    protected void CancelReload()
    {
        if (reloadRoutine != null)
        {
            StopCoroutine(reloadRoutine);
            reloadRoutine = null;
        }

        isReloading = false;
    }

    private void FillMagazine() //Mueve las balas reserva al cargador
    {
        float ammoNeeded = capacity - currentAmmo;
        float ammoToReload = Mathf.Min(ammoNeeded, reserveAmmo);

        currentAmmo += ammoToReload;
        reserveAmmo -= ammoToReload;

        isEmpty = currentAmmo <= 0;
        isFull = currentAmmo >= capacity;
        noAmmo = reserveAmmo <= 0;
    }

    public void SwitchWeapon(){

        isSwitchingWeapon = true;

    }

    public float GetCurrentAmmo()
    {
        return currentAmmo;
    }

    public void SetCurrentAmmo(float currentAmmo)
    {
        this.currentAmmo = currentAmmo;
    }

    public float GetReserveAmmo()
    {
        return reserveAmmo;
    }

    public void SetReserveAmmo(float reserveAmmo)
    {
        this.reserveAmmo = reserveAmmo;
    }


    //----------------------------------------------
    // DISPARO (servicio compartido por todas las armas)
    //----------------------------------------------

    /* Lanza UN rayo desde la camara con la dispersion indicada.

    IMPORTANTE: el rayo sale de la CAMARA, no del canon. El jugador apunta
    con el centro de la pantalla, asi que disparar desde el canon desviaria
    el tiro respecto a la mira y permitiria disparar a traves de esquinas.
    La estela si sale del canon: la logica y la presentacion van separadas.

    La dispersion llega COMO PARAMETRO para que la escopeta pueda llamar a
    este mismo metodo N veces con un cono ancho, sin duplicar nada. */

    protected bool ShootRay(float spread, out RaycastHit hit)
    {
        return ShootRay(spread, out hit, out _);
    }

    // Misma llamada, pero devolviendo tambien la direccion del disparo: la
    // necesita ApplyDamage para que el ragdoll tuerza el cuerpo hacia donde
    // iba la bala.
    protected bool ShootRay(float spread, out RaycastHit hit, out Vector3 direction)
    {
        hit = default;
        direction = Vector3.forward;

        if (playerCamera == null)
        {
            Debug.LogWarning("Sin camara asignada: no se puede disparar");
            return false;
        }

        Transform cam = playerCamera.transform;

        Vector3 origin = cam.position;
        direction = ApplySpread(cam.forward, cam.rotation, spread);

        // Despues de calcular la direccion: este tiro sale con el cono que
        // marcaba la reticula, y es el siguiente el que sale mas abierto.
        AccumulateSpread();

        bool impact = RaycastIgnoringOwner(origin, direction, out hit);

        // Punto final real del disparo: el impacto, o el alcance maximo.
        Vector3 endPoint = impact
            ? hit.point
            : origin + direction * scope;

        if (drawDebugRay)
        {
            Debug.DrawLine(
                origin,
                endPoint,
                impact ? Color.red : Color.yellow,
                debugRayDuration
            );
        }

        SpawnTracer(endPoint);

        return impact;
    }

    /* Igual que Physics.Raycast, pero saltandose el cuerpo de quien dispara.

    El rayo nace en la camara, que va DENTRO de la capsula del jugador. Unity no
    detecta el collider en el que empieza el rayo, pero al agacharse la camara
    queda en el borde de la capsula y el tiro podia morir en tu propio cuerpo.
    Se piden todos los impactos y se queda el mas cercano que no sea tuyo. */

    private bool RaycastIgnoringOwner(Vector3 origin, Vector3 direction, out RaycastHit hit)
    {
        hit = default;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            scope,
            hittableLayers,
            QueryTriggerInteraction.Ignore
        );

        bool found = false;
        float nearest = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            if (ownerRoot != null && hits[i].collider.transform.IsChildOf(ownerRoot))
            {
                continue;
            }

            if (hits[i].distance < nearest)
            {
                nearest = hits[i].distance;
                hit = hits[i];
                found = true;
            }
        }

        return found;
    }

    /* Desvia una direccion dentro de un cono.

    Se construye la desviacion en el espacio LOCAL de la camara y despues se
    multiplica por su rotacion. Si se rotara el vector con angulos de mundo,
    la dispersion se deformaria segun hacia donde mires.

    insideUnitCircle reparte los puntos en un circulo, no en un cuadrado:
    da un cono redondo y sin acumulacion en las esquinas. */

    private Vector3 ApplySpread(
        Vector3 forward,
        Quaternion cameraRotation,
        float spread)
    {
        // Cono cero = disparo exacto al centro, sin ruido de coma flotante.
        if (spread <= 0.0f)
        {
            return forward;
        }

        Vector2 offset = Random.insideUnitCircle * spread;

        Quaternion deviation = Quaternion.Euler(offset.y, offset.x, 0.0f);

        return cameraRotation * deviation * Vector3.forward;
    }

    /* Multiplicador de dano segun la distancia del impacto.

    Hasta effectiveRange el dano es integro. A partir de ahi cae segun la
    curva hasta minDamageMultiplier en el alcance maximo. Un arma sin caida
    solo tiene que dejar effectiveRange >= scope. */

    protected float GetDamageMultiplier(float distance)
    {
        if (distance <= effectiveRange || scope <= effectiveRange)
        {
            return 1.0f;
        }

        // 0 en el alcance efectivo, 1 en el alcance maximo.
        float t = Mathf.InverseLerp(effectiveRange, scope, distance);

        float shaped = Mathf.Clamp01(falloffShape.Evaluate(t));

        return Mathf.Lerp(1.0f, minDamageMultiplier, shaped);
    }

    /* Aplica el dano a lo que haya impactado.

    Se usa GetComponentInParent porque el collider tocado suele ser una
    hitbox de un hueso, no la raiz del enemigo.

    RequestDamage viene de Character: resuelve el dano en el SERVIDOR y lleva
    el punto y la direccion del impacto, que es lo que usa el ragdoll para
    torcer el cuerpo por donde entro la bala. Devuelve el dano aplicado por
    si quien llama quiere acumularlo (escopeta) o mostrarlo. */

    protected float ApplyDamage(RaycastHit hit, Vector3 direction)
    {
        EnemyBehaviour enemy = hit.collider.GetComponentInParent<EnemyBehaviour>();

        if (enemy == null)
        {
            return 0.0f;
        }

        float finalDamage = damage * GetDamageMultiplier(hit.distance);

        // Aviso visual para quien dispara (marca en la reticula y numero de dano).
        // A un cadaver no: sus huesos siguen ahi unos segundos y confundiria.
        if (!enemy.IsDead)
        {
            FeedbackCombate.Impacto(hit.point, finalDamage, enemy.GetHealth() - finalDamage <= 0.0f);
        }

        enemy.RequestDamage(finalDamage, hit.point, direction);

        return finalDamage;
    }

    //----------------------------------------------
    // ANIMACION DE LOS BRAZOS
    //
    // Tres llamadas y nada mas. Las armas hijas no tocan el Animator
    // directamente: si manana el controller cambia de parametros, se
    // arregla aqui y no en cuatro sitios.
    //----------------------------------------------

    protected void PlayShootAnimation()
    {
        if (armsAnimator == null || !armsAnimator.isActiveAndEnabled)
        {
            return;
        }

        armsAnimator.SetTrigger(HashShoot);
    }

    protected void PlayReloadAnimation()
    {
        if (armsAnimator == null || !armsAnimator.isActiveAndEnabled)
        {
            return;
        }

        armsAnimator.SetTrigger(HashReload);
    }

    /* Lo llama PlayerController cuando el jugador empieza o deja de correr.

    Es un Bool y no un Trigger a proposito: el sprint es un ESTADO que dura,
    no un evento puntual. Con un trigger habria que acordarse de apagarlo. */

    public void SetSprinting(bool sprinting)
    {
        ownerSprinting = sprinting;   // tambien abre el cono de dispersion

        // Correr no debe interrumpir una recarga ya empezada.
        if (armsAnimator == null || !armsAnimator.isActiveAndEnabled)
        {
            return;
        }

        armsAnimator.SetBool(HashSprinting, sprinting && !isReloading);
    }

    public Animator GetArmsAnimator()
    {
        return armsAnimator;
    }

    public float GetReloadDuration()
    {
        return reloadDuration;
    }


    // Estela visual: del CANON al punto de impacto.
    private void SpawnTracer(Vector3 endPoint)
    {
        if (tracerPrefab == null || muzzle == null)
        {
            return;
        }

        BulletTracer tracer = Instantiate(tracerPrefab);
        tracer.Show(muzzle.position, endPoint);
    }


    //----------------------------------------------
    // APUNTADO
    //----------------------------------------------

    // Mide el desfase pivote <-> marcador. Se llama una sola vez.
    private void CacheAimOffsets()
    {
        if (weaponHolder != null && hipPosition != null)
        {
            holderPositionOffset =
                weaponHolder.localPosition - hipPosition.localPosition;

            holderRotationOffset =
                Quaternion.Inverse(hipPosition.localRotation) *
                weaponHolder.localRotation;
        }
        else
        {
            holderPositionOffset = Vector3.zero;
            holderRotationOffset = Quaternion.identity;
        }
    }

    private void CacheCameraFieldOfView()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
    }

    private void ResolveOwner()
    {
        PlayerController owner = GetComponentInParent<PlayerController>();
        ownerRoot = owner != null ? owner.transform : transform.root;
        ownerController = owner;
        ownerBody = owner != null ? owner.GetComponent<CharacterController>() : null;

        ownerDowned = GetComponentInParent<PlayerDownedState>();
        ownerNoise = GetComponentInParent<PlayerNoise>();
    }

    /* Si en el Inspector se dejan las capas del rayo en "Nothing", el disparo
    no podia tocar NADA (es lo que pasaba en el prefab del jugador). En ese caso
    se usa una mascara por defecto: todo menos Ignore Raycast, las manos FP, el
    cuerpo local y los ragdolls. Si se asigna una mascara a mano, se respeta. */

    private void ResolveHittableLayers()
    {
        if (hittableLayers.value != 0)
        {
            return;
        }

        int mask = Physics.DefaultRaycastLayers;
        mask &= ~LayerBit(PlayerVisual.FPArmsLayerName);
        mask &= ~LayerBit(PlayerVisual.LocalBodyLayerName);
        mask &= ~LayerBit("Ragdoll");

        hittableLayers = mask;
    }

    private static int LayerBit(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? 1 << layer : 0;
    }

    // Avisa a los enemigos con oido de que ha sonado un disparo
    protected void MakeShotNoise()
    {
        if (ownerNoise != null)
        {
            ownerNoise.MakeShot();
        }
    }

    // Lectura del boton de apuntar. Virtual por si algun arma quiere otro
    // esquema (por ejemplo, apuntado con conmutador en vez de mantenido).
    protected virtual void ReadAimInput()
    {
        // Con una ventana de interfaz abierta los clics son para la UI.
        // Abatido tampoco se apunta.
        if (!canAim || UIState.BlocksGameplay || !OwnerCanAct)
        {
            isAiming = false;
            return;
        }

        isAiming = Input.GetButton("Aim");
    }

    // Mueve el WeaponHolder entre cadera y apuntado.
    // Virtual: un arma con mira telescopica puede extenderlo para ocultar
    // el modelo y superponer la textura del visor.
    protected virtual void Aim()
    {
        if (!canAim ||
            weaponHolder == null ||
            hipPosition == null ||
            aimPosition == null)
        {
            return;
        }

        Transform target = isAiming ? aimPosition : hipPosition;

        // Marcador + desfase del pivote = destino real del WeaponHolder.
        Vector3 targetPosition =
            target.localPosition + holderPositionOffset;

        Quaternion targetRotation =
            target.localRotation * holderRotationOffset;

        // Interpolacion independiente del framerate.
        float t = 1.0f - Mathf.Exp(-aimSpeed * Time.deltaTime);

        weaponHolder.localPosition = Vector3.Lerp(
            weaponHolder.localPosition,
            targetPosition,
            t
        );

        weaponHolder.localRotation = Quaternion.Slerp(
            weaponHolder.localRotation,
            targetRotation,
            t
        );
    }

    // Cierra el FOV al apuntar. Virtual por si un arma quiere zoom
    // escalonado en varios niveles.
    protected virtual void Zoom()
    {
        if (!canAim || playerCamera == null)
        {
            return;
        }

        float targetFov = isAiming ? aimFieldOfView : DefaultFieldOfView;
        float z = 1.0f - Mathf.Exp(-zoomSpeed * Time.deltaTime);

        playerCamera.fieldOfView = Mathf.Lerp(
            playerCamera.fieldOfView,
            targetFov,
            z
        );
    }

    // Devuelve la camara a su FOV de reposo. Necesario al guardar el arma:
    // si cambias de arma mientras apuntas, el zoom se quedaria pegado.
    protected void ResetZoom()
    {
        if (playerCamera != null && DefaultFieldOfView > 0.0f)
        {
            playerCamera.fieldOfView = DefaultFieldOfView;
        }
    }


    //----------------------------------------------
    // CICLO DE VIDA
    //----------------------------------------------

    protected virtual void Awake(){

        CacheAimOffsets();
        CacheCameraFieldOfView();
        ResolveOwner();
        ResolveHittableLayers();
    }

    protected virtual void Update(){

        ReadAimInput();
        Zoom();
    }

    // El movimiento del arma va en LateUpdate: asi se aplica DESPUES de que
    // el mouse look haya rotado la camara este frame y no aparece jitter.
    protected virtual void LateUpdate(){

        Aim();
    }

    protected virtual void OnDisable(){

        isAiming = false;
        ResetZoom();

        // Si el arma se guarda a mitad de disparo o de recarga, que no se
        // queden los flags colgados: al volver a sacarla no dispararia.
        CancelInvoke(nameof(StopShooting));
        isShooting = false;

        CancelReload();

        if (armsAnimator != null && armsAnimator.isActiveAndEnabled)
        {
            armsAnimator.SetBool(HashSprinting, false);
        }
    }
}