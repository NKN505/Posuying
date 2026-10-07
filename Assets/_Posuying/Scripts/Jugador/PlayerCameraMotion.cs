using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Movimiento procedural de la camara del jugador.
/// Refactor de RealisticPOVCamera (Rui): mismas capas principales, en ~1/3 del codigo.
///
/// VA EN: Player > Main Camera. No hace falta CameraRoot ni tocar PlayerController.
///
/// Como convive con PlayerController y Weapon sin pisarse:
///   1) Update (orden -100, antes que ellos): quita el efecto del frame anterior.
///      PlayerController (pitch, agacharse) y Weapon (zoom al apuntar) trabajan
///      siempre sobre la camara "limpia", y los disparos salen hacia donde miras
///      de verdad, no hacia donde tiembla la camara.
///   2) LateUpdate: calcula el efecto y lo suma encima de lo que hayan dejado.
///
/// Se alimenta solo: lee el CharacterController, el PlayerController y el arma
/// activa. No hace falta llamarlo desde ningun otro script (aunque puedes:
/// OnShot, OnDamage, OnLand, OnJump, OnWeaponSwap y ResetMotion son publicos).
///
/// Capas:
///   Continuas (se aplican directas):  head bob, respiracion + micro-temblor.
///   Fisicas (pasan por un muelle):    inercia al acelerar, roll al strafear,
///                                     inercia al mirar, y todos los impulsos
///                                     (salto, aterrizaje, disparo, dano, cambio de arma).
///   FOV:                              +FOV al esprintar y pequenos golpes.
///
/// Recorte (clipping):
///   - Near clip corto: con el 0.3 de serie la camara cortaba todo lo que tenia a
///     menos de 30 cm, y por eso se veia el interior de los brazos y se atravesaban
///     las paredes (pegado a una, la tienes a ~13 cm).
///   - Guarda de paredes: si la camara (con todo el efecto sumado) quedaria dentro
///     de un muro, se retrasa hacia el eje del cuerpo hasta que no lo toque.
///   - Manos y arma siempre visibles: se dibujan con una camara overlay de URP
///     (creada aqui, hija de esta) que solo ve la capa FPArms y borra la
///     profundidad antes de pintar. Las paredes ya no las tapan. La camara
///     principal deja de dibujar FPArms. Todo lo que cuelga de esta camara con
///     Renderer (manos, pistola, fogonazo) se pasa a FPArms automaticamente.
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public sealed class PlayerCameraMotion : MonoBehaviour
{
    // =====================================================================
    // AJUSTES
    // =====================================================================

    [Header("Referencias (vacias = se buscan solas en el padre)")]
    [SerializeField] private PlayerController player;
    [SerializeField] private CharacterController body;

    [Header("General")]
    [Tooltip("0 = sin movimiento procedural (cinematicas), 1 = normal.")]
    [Range(0f, 1f)] public float weight = 1f;
    [Tooltip("Cuanto movimiento se conserva agachado.")]
    [SerializeField, Range(0f, 1f)] private float crouchMultiplier = 0.65f;
    [Tooltip("Velocidades del Character (speed y speed * sprintMultiplier).")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 9f;

    [Header("Head bob (metros, en espacio del cuerpo)")]
    [Tooltip("Ciclos por segundo. 1 ciclo = 2 pisadas.")]
    [SerializeField] private float walkStepRate = 1.75f;
    [SerializeField] private float sprintStepRate = 2.2f;
    [Tooltip("X = lado, Y = arriba/abajo, Z = adelante/atras.")]
    [SerializeField] private Vector3 walkBob = new Vector3(0.008f, 0.014f, 0.004f);
    [SerializeField] private Vector3 sprintBob = new Vector3(0.012f, 0.021f, 0.007f);
    [Tooltip("0 = pisada marcada (rebote), 1 = onda suave.")]
    [SerializeField, Range(0f, 1f)] private float bobSmoothness = 0.35f;

    [Header("Respiracion y micro-temblor (solo quieto)")]
    [SerializeField] private float breathRate = 0.18f;
    [SerializeField] private float breathHeight = 0.002f;
    [SerializeField] private float breathPitch = 0.08f;
    [SerializeField] private float microTremor = 0.055f;
    [SerializeField] private float microFrequency = 0.2f;

    [Header("Inercia del cuerpo")]
    [SerializeField] private float accelerationLag = 0.0018f;
    [SerializeField] private float accelerationPitch = 0.16f;
    [Tooltip("Grados de roll por cada m/s de strafe.")]
    [SerializeField] private float strafeRoll = 0.75f;
    [SerializeField] private float maxStrafeRoll = 2f;

    [Header("Inercia al mirar")]
    [Tooltip("Grados de roll por cada 100 grados/s de giro horizontal.")]
    [SerializeField] private float lookRoll = 0.8f;
    [SerializeField] private float lookLag = 0.3f;
    [SerializeField] private float maxLookSway = 2.5f;

    [Header("Impulsos (pico aproximado: metros / grados)")]
    [SerializeField] private float jumpHeight = 0.012f;
    [SerializeField] private float jumpPitch = 0.8f;
    [Tooltip("Velocidad de caida (m/s) a partir de la cual se nota y a la que es maximo.")]
    [SerializeField] private Vector2 landingSpeedRange = new Vector2(2f, 10f);
    [SerializeField] private float landingDrop = 0.06f;
    [SerializeField] private float landingPitch = 3f;
    [SerializeField] private float landingRoll = 1.2f;
    [Tooltip("Pitch hacia ARRIBA por disparo. El original lo aplicaba hacia abajo.")]
    [SerializeField] private float recoilPitch = 1.2f;
    [SerializeField] private float recoilYaw = 0.3f;
    [SerializeField] private float recoilRoll = 0.4f;
    [SerializeField] private float recoilKickBack = 0.01f;
    [SerializeField] private float swapShift = 0.01f;
    [SerializeField] private float swapRoll = 0.8f;
    [Tooltip("Dano (en fraccion de vida maxima) que produce el golpe maximo. 0.25 = un cuarto de vida.")]
    [SerializeField] private float damageForFullHit = 0.25f;
    [SerializeField] private float damageShift = 0.02f;
    [SerializeField] private float damagePitch = 2.5f;
    [SerializeField] private float damageRoll = 1.5f;

    [Header("Muelle (todo lo fisico pasa por aqui)")]
    [Tooltip("Mas alto = vuelve antes al centro.")]
    [SerializeField] private float springStiffness = 150f;
    [Tooltip("Mas bajo = mas rebote. Critico = 2 * raiz(stiffness).")]
    [SerializeField] private float springDamping = 18f;

    [Header("Recorte (brazos y paredes)")]
    [Tooltip("Distancia minima que dibuja la camara. 0.3 (el de serie) recorta brazos y paredes. 0.01-0.03 para primera persona.")]
    [SerializeField, Range(0.005f, 0.1f)] private float nearClip = 0.02f;
    [SerializeField] private bool wallGuard = true;
    [Tooltip("Capas contra las que choca la camara. Quita las del jugador, FPArms y Ragdoll.")]
    [SerializeField] private LayerMask wallLayers = ~0;
    [Tooltip("Margen extra entre la camara y la pared (m).")]
    [SerializeField] private float wallPadding = 0.02f;

    [Tooltip("Manos y arma en una camara overlay: nunca atraviesan paredes.")]
    [SerializeField] private bool armsOverlay = true;
    [SerializeField] private string armsLayerName = "FPArms";

    [Header("FOV (se suma al de Opciones y al zoom del arma)")]
    [SerializeField] private bool dynamicFov = true;
    [SerializeField] private float sprintFov = 3f;
    [SerializeField] private float landingFov = 1f;
    [SerializeField] private float shotFov = 0.25f;
    [SerializeField] private float fovRecovery = 8f;

    // =====================================================================
    // ESTADO
    // =====================================================================

    private Camera _cam;
    private Transform _t;

    // Muelles: posicion (m) y rotacion (grados). Los impulsos van a la velocidad.
    private Vector3 _springPos, _springPosVel;
    private Vector3 _springRot, _springRotVel;

    // Lo que habia antes de aplicar el efecto y lo que se aplico, para deshacerlo.
    private Vector3 _cleanPos, _appliedPos;
    private Quaternion _cleanRot, _appliedRot;
    private float _cleanFov, _appliedFov;
    private bool _applied;

    // Movimiento
    private Vector3 _lastWorldPos;
    private Vector3 _localVel, _lastLocalVel, _accel;
    private bool _hasLastPos;
    private float _bobPhase, _breathPhase, _noiseSeed;
    private float _airTime, _minAirVelY;
    private bool _wasGrounded = true, _wasJumping;
    private Vector2 _lookSway;
    private float _fovKick;

    // Arma y vida (deteccion automatica)
    private Weapon _weapon;
    private float _lastAmmo = -1f;
    private float _lastHealth = -1f;

    // Pico aproximado de un muelle por unidad de impulso.
    private float ImpulseScale => Mathf.Sqrt(springStiffness) * 2.718f;

    // =====================================================================
    // UNITY
    // =====================================================================

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        _t = transform;
        if (player == null) player = GetComponentInParent<PlayerController>();
        if (body == null) body = GetComponentInParent<CharacterController>();
        _noiseSeed = Random.Range(0f, 1000f);
        _cam.nearClipPlane = nearClip;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_cam != null) _cam.nearClipPlane = nearClip;   // ajustable en Play
    }
#endif

    private void OnDisable()
    {
        RemoveEffect();
        ResetMotion();
        DestroyArmsOverlay();
    }

    // Antes que PlayerController y Weapon: les devuelve la camara limpia.
    private void Update()
    {
        RemoveEffect();

        // Solo el dueno: en los remotos la camara esta apagada y esto sobra.
        if (player != null && player.IsSpawned && !player.IsOwner)
            enabled = false;
    }

    private void LateUpdate()
    {
        ApplyMotion();
        UpdateArmsOverlay();   // despues: copia el FOV ya con el efecto sumado
    }

    private void ApplyMotion()
    {
        if (body == null || player == null) return;

        float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.05f);

        _cleanPos = _t.localPosition;
        _cleanRot = _t.localRotation;
        _cleanFov = _cam.fieldOfView;

        if (!ReadMovement(dt)) return;   // teletransporte: este frame no cuenta
        DetectEvents();

        bool crouching = player.GetIsCrouching();
        float stance = crouching ? crouchMultiplier : 1f;
        float speed = new Vector2(_localVel.x, _localVel.z).magnitude;
        float move01 = Mathf.Clamp01(speed / walkSpeed);
        float sprint01 = crouching ? 0f : Mathf.InverseLerp(walkSpeed, sprintSpeed, speed);
        float still01 = 1f - Mathf.Clamp01(speed / (walkSpeed * 0.35f));
        bool airborne = _airTime > 0.1f;   // margen: en escaleras isGrounded parpadea

        // --- Capas continuas (directas, ya son suaves) ---------------------
        Vector3 pos = Bob(dt, speed, move01, sprint01, airborne) * stance;
        Vector3 rot = Vector3.zero;
        Breathing(dt, still01, ref pos, ref rot);

        // --- Capas fisicas (objetivo del muelle) ---------------------------
        Vector3 posTarget = new Vector3(-_accel.x, -_accel.y * 0.2f, -_accel.z) * accelerationLag * stance;
        Vector3 rotTarget = new Vector3(
            -_accel.z * accelerationPitch * stance,
            0f,
            Mathf.Clamp(-_localVel.x * strafeRoll, -maxStrafeRoll, maxStrafeRoll) * stance);
        rotTarget += LookSway(dt);

        Spring(ref _springPos, ref _springPosVel, posTarget, dt);
        Spring(ref _springRot, ref _springRotVel, rotTarget, dt);

        pos += _springPos;
        rot += _springRot;

        // --- Aplicar -------------------------------------------------------
        _appliedRot = _cleanRot * Quaternion.Euler(rot * weight);
        _appliedPos = GuardWalls(_cleanPos + pos * weight);
        _t.localPosition = _appliedPos;
        _t.localRotation = _appliedRot;

        _fovKick = Mathf.MoveTowards(_fovKick, 0f, fovRecovery * dt);
        float fovOffset = dynamicFov ? (sprint01 * sprintFov + _fovKick) * weight : 0f;
        _appliedFov = _cleanFov + fovOffset;
        _cam.fieldOfView = _appliedFov;

        _applied = true;
    }

    // =====================================================================
    // API PUBLICA (opcional: todo se detecta solo)
    // =====================================================================

    public void OnJump()
    {
        Kick(new Vector3(0f, jumpHeight, 0f), new Vector3(-jumpPitch, 0f, 0f));
    }

    /// <param name="fallSpeed">Velocidad vertical al tocar el suelo (m/s, positiva).</param>
    public void OnLand(float fallSpeed)
    {
        float k = Mathf.InverseLerp(landingSpeedRange.x, landingSpeedRange.y, Mathf.Abs(fallSpeed));
        if (k <= 0f) return;
        Kick(new Vector3(0f, -landingDrop, 0f) * k,
             new Vector3(landingPitch, 0f, RandomSign() * landingRoll) * k);
        _fovKick += landingFov * k;
    }

    /// <param name="strength">1 = recoil normal. Cada arma puede pasar el suyo.</param>
    public void OnShot(float strength = 1f)
    {
        Kick(new Vector3(0f, 0f, -recoilKickBack) * strength,
             new Vector3(-recoilPitch,
                         Random.Range(-recoilYaw, recoilYaw),
                         Random.Range(-recoilRoll, recoilRoll)) * strength);
        _fovKick += shotFov * strength;
    }

    public void OnWeaponSwap()
    {
        float s = RandomSign();
        Kick(new Vector3(s * swapShift, -swapShift, -swapShift * 0.5f),
             new Vector3(0f, s * swapRoll, -s * swapRoll));
    }

    /// <param name="intensity">0..1</param>
    public void OnDamage(float intensity)
    {
        intensity = Mathf.Clamp01(intensity);
        if (intensity <= 0f) return;
        float s = RandomSign();
        Kick(new Vector3(s * damageShift, 0f, -damageShift) * intensity,
             new Vector3(damagePitch, s * damageRoll * 0.7f, -s * damageRoll) * intensity);
    }

    /// <summary>Borra todos los impulsos. Se llama solo al reaparecer.</summary>
    public void ResetMotion()
    {
        _springPos = _springPosVel = Vector3.zero;
        _springRot = _springRotVel = Vector3.zero;
        _accel = _localVel = _lastLocalVel = Vector3.zero;
        _lookSway = Vector2.zero;
        _fovKick = 0f;
        _bobPhase = 0f;
        _hasLastPos = false;
    }

    // =====================================================================
    // INTERNO
    // =====================================================================

    // Deshace lo aplicado en el frame anterior. Si otro script ya ha escrito
    // encima (valor distinto al que dejamos), se respeta el suyo.
    private void RemoveEffect()
    {
        if (!_applied) return;
        if (_t.localPosition == _appliedPos) _t.localPosition = _cleanPos;
        if (_t.localRotation == _appliedRot) _t.localRotation = _cleanRot;
        if (Mathf.Approximately(_cam.fieldOfView, _appliedFov)) _cam.fieldOfView = _cleanFov;
        _applied = false;
    }

    // Velocidad y aceleracion en espacio del jugador, medidas por desplazamiento.
    private bool ReadMovement(float dt)
    {
        Transform root = player.transform;
        Vector3 worldPos = root.position;

        if (!_hasLastPos || (worldPos - _lastWorldPos).sqrMagnitude > 9f)
        {
            // Primer frame o salto de mas de 3 m (spawn / respawn / escalada rara)
            if (_hasLastPos) ResetMotion();
            _lastWorldPos = worldPos;
            _hasLastPos = true;
            return false;
        }

        _localVel = root.InverseTransformDirection((worldPos - _lastWorldPos) / dt);
        _lastWorldPos = worldPos;

        Vector3 rawAccel = Vector3.ClampMagnitude((_localVel - _lastLocalVel) / dt, 30f);
        _accel = Vector3.Lerp(_accel, rawAccel, 1f - Mathf.Exp(-15f * dt));   // quita el ruido
        _lastLocalVel = _localVel;
        return true;
    }

    // Salto, aterrizaje, disparo, cambio de arma y dano, sin tocar otros scripts.
    private void DetectEvents()
    {
        // Salto
        bool jumping = player.GetIsJumping();
        if (jumping && !_wasJumping) OnJump();
        _wasJumping = jumping;

        // Aterrizaje: la velocidad de caida mas alta mientras estaba en el aire
        bool grounded = body.isGrounded;
        if (!grounded)
        {
            _airTime += Time.deltaTime;
            _minAirVelY = Mathf.Min(_minAirVelY, _localVel.y);
        }
        else
        {
            if (!_wasGrounded) OnLand(-_minAirVelY);
            _airTime = 0f;
            _minAirVelY = 0f;
        }
        _wasGrounded = grounded;

        // Arma activa: cambio y disparo (bajada de municion sin estar recargando)
        if (_weapon == null || !_weapon.isActiveAndEnabled)
        {
            Weapon found = player.GetComponentInChildren<Weapon>(false);
            if (found != _weapon)
            {
                if (_weapon != null || found != null) OnWeaponSwap();
                _weapon = found;
                _lastAmmo = found != null ? found.GetCurrentAmmo() : -1f;
            }
        }
        if (_weapon != null)
        {
            float ammo = _weapon.GetCurrentAmmo();
            if (_lastAmmo >= 0f && ammo < _lastAmmo && !_weapon.GetIsReloading())
                OnShot();
            _lastAmmo = ammo;
        }

        // Dano: la vida viene del servidor por NetworkVariable
        float health = player.GetHealth();
        if (_lastHealth > 0f && health < _lastHealth)
        {
            float frac = (_lastHealth - health) / Mathf.Max(1f, player.GetMaxHealth());
            OnDamage(frac / Mathf.Max(0.01f, damageForFullHit));
        }
        _lastHealth = health;
    }

    private Vector3 Bob(float dt, float speed, float move01, float sprint01, bool airborne)
    {
        if (speed < 0.15f && !airborne) return Vector3.zero;

        if (!airborne)
            _bobPhase += dt * Mathf.Lerp(walkStepRate, sprintStepRate, sprint01)
                            * Mathf.Lerp(0.45f, 1f, move01) * Mathf.PI * 2f;

        Vector3 amp = Vector3.Lerp(walkBob, sprintBob, sprint01) * move01 * (airborne ? 0.04f : 1f);
        float vertical = Mathf.Lerp(Mathf.Abs(Mathf.Sin(_bobPhase)), Mathf.Sin(_bobPhase * 2f), bobSmoothness);

        return new Vector3(
            Mathf.Sin(_bobPhase) * amp.x,
            vertical * amp.y,
            Mathf.Sin(_bobPhase + 0.6f) * amp.z);
    }

    private void Breathing(float dt, float still01, ref Vector3 pos, ref Vector3 rot)
    {
        _breathPhase = Mathf.Repeat(_breathPhase + dt * breathRate * Mathf.PI * 2f, Mathf.PI * 2f);
        float breath = Mathf.Sin(_breathPhase) * still01;
        pos.y += breath * breathHeight;
        pos.z += breath * breathHeight * 0.6f;
        rot.x += breath * breathPitch;

        // Micro-temblor: siempre un poco, mas cuando estas quieto
        float n = Time.time * microFrequency;
        Vector3 noise = new Vector3(
            Mathf.PerlinNoise(n, _noiseSeed) - 0.5f,
            Mathf.PerlinNoise(_noiseSeed, n) - 0.5f,
            Mathf.PerlinNoise(n, _noiseSeed + 50f) - 0.5f) * 2f;
        rot += noise * microTremor * Mathf.Lerp(0.15f, 1f, still01);
    }

    // Mismo input que PlayerController, convertido a grados/s para que no dependa de los FPS.
    private Vector3 LookSway(float dt)
    {
        Vector2 look = Vector2.zero;
        if (!UIState.BlocksGameplay)
        {
            float sens = GameSettings.MouseSensitivity;
            float invert = GameSettings.InvertY ? -1f : 1f;
            Vector2 mirar = Controles.Mirar();   // raton + stick derecho
            look = new Vector2(mirar.x, mirar.y * invert) / dt;
        }
        _lookSway = Vector2.Lerp(_lookSway, look, 1f - Mathf.Exp(-12f * dt));

        Vector3 sway = new Vector3(
            _lookSway.y * lookLag * 0.01f,
            -_lookSway.x * lookLag * 0.01f,
            -_lookSway.x * lookRoll * 0.01f);
        return Vector3.ClampMagnitude(sway, maxLookSway);
    }

    // ---------------------------------------------------------------------
    // Camara overlay de manos
    // ---------------------------------------------------------------------

    private Camera _armsCam;
    private int _armsLayer = -2;          // -2 = sin resolver, -1 = no existe
    private float _nextLayerSweep;
    private Weapon _layeredWeapon;

    private void UpdateArmsOverlay()
    {
        if (!armsOverlay || _cam == null)
        {
            DestroyArmsOverlay();
            return;
        }

        // En la copia remota no hay nada que dibujar (y el script se apaga solo).
        if (player != null && player.IsSpawned && !player.IsOwner) return;

        if (_armsLayer == -2) _armsLayer = FindLayer(armsLayerName);
        if (_armsLayer < 0) return;
        int armsBit = 1 << _armsLayer;

        if (_armsCam == null) CreateArmsOverlay(armsBit);

        // PlayerVisual vuelve a meter FPArms en la principal al spawnear: se quita cada frame.
        _cam.cullingMask &= ~armsBit;

        _armsCam.cullingMask = armsBit;
        _armsCam.fieldOfView = _cam.fieldOfView;   // sigue el zoom del arma y el FOV dinamico
        _armsCam.nearClipPlane = 0.01f;
        _armsCam.farClipPlane = 10f;

        // Las armas se instancian en Default: al cambiar de arma (y cada segundo por
        // si aparece algo nuevo) todo lo que cuelga de la camara pasa a FPArms.
        if (_weapon != _layeredWeapon || Time.time >= _nextLayerSweep)
        {
            _layeredWeapon = _weapon;
            _nextLayerSweep = Time.time + 1f;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].gameObject.layer = _armsLayer;
        }
    }

    private void CreateArmsOverlay(int armsBit)
    {
        var go = new GameObject("FP Arms Camera");
        go.transform.SetParent(_t, false);
        _armsCam = go.AddComponent<Camera>();
        _armsCam.clearFlags = CameraClearFlags.Nothing;
        _armsCam.cullingMask = armsBit;
        _armsCam.depth = _cam.depth + 1;

        var baseData = _cam.GetUniversalAdditionalCameraData();
        var armsData = _armsCam.GetUniversalAdditionalCameraData();
        armsData.renderType = CameraRenderType.Overlay;
        armsData.renderPostProcessing = baseData.renderPostProcessing;
        armsData.renderShadows = baseData.renderShadows;

        if (!baseData.cameraStack.Contains(_armsCam))
            baseData.cameraStack.Add(_armsCam);
    }

    private void DestroyArmsOverlay()
    {
        if (_armsCam == null) return;

        if (_cam != null)
        {
            var baseData = _cam.GetUniversalAdditionalCameraData();
            baseData.cameraStack.Remove(_armsCam);
            if (_armsLayer >= 0) _cam.cullingMask |= 1 << _armsLayer;   // que la principal vuelva a verlas
        }

        Destroy(_armsCam.gameObject);
        _armsCam = null;
        _layeredWeapon = null;
    }

    // Igual que PlayerVisual: algunas capas se guardaron con espacio al final.
    private static int FindLayer(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0) return layer;
        for (int i = 0; i < 32; i++)
        {
            string n = LayerMask.LayerToName(i);
            if (!string.IsNullOrEmpty(n) &&
                string.Equals(n.Trim(), layerName.Trim(), System.StringComparison.OrdinalIgnoreCase))
                return i;
        }
        Debug.LogWarning("[PlayerCameraMotion] No existe la capa '" + layerName + "': manos sin overlay.");
        return -1;
    }

    // Lanza una esfera desde el eje del cuerpo (a la altura de la camara) hasta
    // donde iria la camara. Si choca, la camara se queda antes del choque.
    // El radio cubre las esquinas del plano cercano, que estan mas lejos del
    // centro cuanto mas abierto el FOV.
    private readonly RaycastHit[] _hits = new RaycastHit[8];

    private Vector3 GuardWalls(Vector3 localPos)
    {
        Transform parent = _t.parent;
        if (!wallGuard || parent == null) return localPos;

        float halfV = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfH = halfV * _cam.aspect;
        float radius = _cam.nearClipPlane * Mathf.Sqrt(1f + halfV * halfV + halfH * halfH) + wallPadding;

        Vector3 origin = parent.TransformPoint(new Vector3(0f, localPos.y, 0f));
        Vector3 target = parent.TransformPoint(localPos);
        Vector3 dir = target - origin;
        float dist = dir.magnitude;
        if (dist < 0.0001f) return localPos;
        dir /= dist;

        int count = Physics.SphereCastNonAlloc(origin, radius, dir, _hits, dist,
                                               wallLayers, QueryTriggerInteraction.Ignore);
        float nearest = dist;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = _hits[i];
            if (h.collider.transform.IsChildOf(player.transform)) continue;   // el propio jugador
            if (h.distance <= 0f) continue;   // empezaba solapado: no hay direccion util
            if (h.distance < nearest) nearest = h.distance;
        }

        if (nearest >= dist) return localPos;
        return parent.InverseTransformPoint(origin + dir * nearest);
    }

    // Muelle amortiguado (Euler semi-implicito). Los impulsos entran por la velocidad.
    private void Spring(ref Vector3 value, ref Vector3 velocity, Vector3 target, float dt)
    {
        velocity += (target - value) * springStiffness * dt;
        velocity *= Mathf.Exp(-springDamping * dt);
        value += velocity * dt;
    }

    private void Kick(Vector3 position, Vector3 rotation)
    {
        float k = ImpulseScale;
        _springPosVel += position * k;
        _springRotVel += rotation * k;
    }

    private static float RandomSign() => Random.value < 0.5f ? -1f : 1f;
}
