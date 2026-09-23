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

    [Header("Estela")]
    [SerializeField] private BulletTracer tracerPrefab;

    [Header("Depuracion")]
    // Dibuja el rayo en la vista Scene (y en Game si activas el boton
    // Gizmos). Rojo = impacto, amarillo = fallo.
    [SerializeField] private bool drawDebugRay = true;
    [SerializeField] private float debugRayDuration = 2.0f;

    // ---------------------------
    // ESTADO INTERNO DEL APUNTADO
    // ---------------------------

    private bool isAiming = false;

    // FOV de reposo. Se lee de la camara en Awake en vez de escribirlo a
    // mano, asi no hay que tocar el script si cambias el FOV en el editor.
    private float defaultFieldOfView;

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

    public LayerMask GetHittableLayers()
    {
        return hittableLayers;
    }

    // Dispersion activa segun el estado de apuntado.
    public float GetCurrentSpread()
    {
        return isAiming ? aimSpread : hipSpread;
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

    public void Reload()
    {
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

        if (isEmpty)
        {
            EmptyReload();
        }
        else
        {
            TacticalReload();
        }
    }

    private void TacticalReload()
    {
        isReloading = true;

        Debug.Log("Recarga táctica: aún queda munición en el cargador");

        //Animaciones. referencias al animator

        FillMagazine();

        isReloading = false;
    }

    private void EmptyReload()
    {
        isReloading = true;

        Debug.Log("Recarga desde vacío: cargador vacío");

        // Animator SetTrigger

        FillMagazine();

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
        hit = default;

        if (playerCamera == null)
        {
            Debug.LogWarning("Sin camara asignada: no se puede disparar");
            return false;
        }

        Transform cam = playerCamera.transform;

        Vector3 origin = cam.position;
        Vector3 direction = ApplySpread(cam.forward, cam.rotation, spread);

        bool impact = Physics.Raycast(
            origin,
            direction,
            out hit,
            scope,
            hittableLayers,
            QueryTriggerInteraction.Ignore
        );

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

        if (playerCamera != null)
        {
            defaultFieldOfView = playerCamera.fieldOfView;
        }
    }

    // Lectura del boton de apuntar. Virtual por si algun arma quiere otro
    // esquema (por ejemplo, apuntado con conmutador en vez de mantenido).
    protected virtual void ReadAimInput()
    {
        if (!canAim)
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

        float targetFov = isAiming ? aimFieldOfView : defaultFieldOfView;
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
        if (playerCamera != null && defaultFieldOfView > 0.0f)
        {
            playerCamera.fieldOfView = defaultFieldOfView;
        }
    }


    //----------------------------------------------
    // CICLO DE VIDA
    //----------------------------------------------

    protected virtual void Awake(){

        CacheAimOffsets();
        CacheCameraFieldOfView();
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
    }
}