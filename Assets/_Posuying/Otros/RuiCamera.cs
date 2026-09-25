using UnityEngine;

/// <summary>
/// RealisticPOVCamera
/// -----------------
/// Sistema procedural de cámara en primera persona orientado a survival horror.
/// No sustituye el sistema de look del personaje: añade movimiento físico/procedural
/// sobre la rotación base de la cámara.
///
/// Jerarquía recomendada:
///
/// Player
/// └── CameraRoot       <- look/Yaw/Pitch del jugador
///     └── Main Camera  <- este componente
///
/// El resultado se construye por capas:
/// 1) Inercia de movimiento y aceleración
/// 2) Head bob dependiente de velocidad
/// 3) Respiración y micro-movimientos
/// 4) Inercia al mirar
/// 5) Pasos / saltos / aterrizajes
/// 6) Recoil de armas
/// 7) Cambio de arma
/// 8) Daño / stagger
/// 9) FOV dinámico
///
/// Todas las capas pasan por springs para evitar movimientos "robóticos".
/// </summary>
[DisallowMultipleComponent]
public sealed class RealisticPOVCamera : MonoBehaviour
{
    // =====================================================================
    // REFERENCIAS
    // =====================================================================

    [Header("REFERENCIAS")]
    [SerializeField] private Camera povCamera;

    [Tooltip("Transform opcional de referencia. Si se deja vacío se usa la cámara.")]
    [SerializeField] private Transform cameraTransform;

    // =====================================================================
    // MOVIMIENTO DEL CUERPO
    // =====================================================================

    [Header("MOVIMIENTO DEL CUERPO")]
    [SerializeField, Min(0.1f)] private float referenceWalkSpeed = 3.2f;
    [SerializeField, Min(0f)] private float accelerationPosAmount = 0.0018f;
    [SerializeField, Min(0f)] private float accelerationPitchAmount = 0.16f;
    [SerializeField, Min(0f)] private float lateralRollAmount = 0.75f;

    [Tooltip("Límite de inclinación lateral generado por el movimiento.")]
    [SerializeField, Min(0f)] private float maxMovementRoll = 2.0f;

    [Tooltip("Multiplicador de movimiento de cámara al agacharse.")]
    [SerializeField, Range(0f, 1f)] private float crouchMotionMultiplier = 0.65f;

    // =====================================================================
    // HEAD BOB / GAIT
    // =====================================================================

    [Header("HEAD BOB / GAIT")]
    [SerializeField, Min(0f)] private float walkStepRate = 1.75f;
    [SerializeField, Min(0f)] private float sprintStepRate = 2.20f;

    [SerializeField, Min(0f)] private float walkBobSide = 0.008f;
    [SerializeField, Min(0f)] private float walkBobVertical = 0.014f;
    [SerializeField, Min(0f)] private float walkBobForward = 0.004f;

    [SerializeField, Min(0f)] private float sprintBobSide = 0.012f;
    [SerializeField, Min(0f)] private float sprintBobVertical = 0.021f;
    [SerializeField, Min(0f)] private float sprintBobForward = 0.007f;

    [Tooltip("Forma de la onda vertical. 1 = más marcada, 0 = prácticamente lineal.")]
    [SerializeField, Range(0f, 1f)] private float secondaryBobBlend = 0.35f;

    [Tooltip("Cantidad de bob conservada en el aire.")]
    [SerializeField, Range(0f, 1f)] private float airborneBobMultiplier = 0.04f;

    [Tooltip("Evita head bob a velocidades insignificantes.")]
    [SerializeField, Min(0f)] private float minimumMovementSpeed = 0.15f;

    // =====================================================================
    // RESPIRACIÓN / MICRO MOVIMIENTO
    // =====================================================================

    [Header("RESPIRACIÓN / MICRO MOVIMIENTO")]
    [SerializeField, Min(0f)] private float breathingRate = 0.18f;
    [SerializeField, Min(0f)] private float breathingVertical = 0.0020f;
    [SerializeField, Min(0f)] private float breathingForward = 0.0012f;
    [SerializeField, Min(0f)] private float breathingPitch = 0.08f;
    [SerializeField, Min(0f)] private float idleMicroPosition = 0.0011f;
    [SerializeField, Min(0f)] private float idleMicroRotation = 0.055f;
    [SerializeField, Min(0f)] private float idleMicroFrequency = 0.20f;

    // =====================================================================
    // INERCIA AL MIRAR
    // =====================================================================

    [Header("INERCIA AL MIRAR")]
    [Tooltip("Sway generado por el movimiento del ratón / stick.")]
    [SerializeField, Min(0f)] private float lookPitchSway = 0.035f;
    [SerializeField, Min(0f)] private float lookYawSway = 0.030f;
    [SerializeField, Min(0f)] private float lookRollSway = 0.045f;

    [Tooltip("Cuánto se conserva el sway al dejar de mover la cámara.")]
    [SerializeField, Min(0f)] private float lookMomentum = 0.12f;

    [Tooltip("Límite de delta de look procesado por frame.")]
    [SerializeField, Min(0f)] private float maxLookDelta = 30f;

    // =====================================================================
    // PASOS
    // =====================================================================

    [Header("PASOS")]
    [SerializeField, Min(0f)] private float stepVerticalImpulse = 0.006f;
    [SerializeField, Min(0f)] private float stepLateralImpulse = 0.0015f;
    [SerializeField, Min(0f)] private float stepRollImpulse = 0.22f;
    [SerializeField, Min(0f)] private float stepPitchImpulse = 0.08f;

    // =====================================================================
    // SALTO
    // =====================================================================

    [Header("SALTO")]
    [SerializeField, Min(0f)] private float jumpUpImpulse = 0.010f;
    [SerializeField, Min(0f)] private float jumpPitchImpulse = 0.14f;

    // =====================================================================
    // CAÍDA / ATERRIZAJE
    // =====================================================================

    [Header("CAÍDA / ATERRIZAJE")]
    [SerializeField, Min(0f)] private float landingMinSpeed = 2.0f;
    [SerializeField, Min(0f)] private float landingMaxSpeed = 10.0f;

    [SerializeField, Min(0f)] private float landingVerticalImpulse = 0.055f;
    [SerializeField, Min(0f)] private float landingPitchImpulse = 1.10f;
    [SerializeField, Min(0f)] private float landingRollImpulse = 0.35f;
    [SerializeField, Min(0f)] private float fallPitchAmount = 0.10f;
    [SerializeField, Min(0f)] private float fallPositionAmount = 0.002f;

    // =====================================================================
    // RECOIL
    // =====================================================================

    [Header("RECOIL")]
    [Tooltip("Multiplicador general de recoil.")]
    [SerializeField, Min(0f)] private float recoilMultiplier = 1.0f;

    [SerializeField, Min(0f)] private float recoilPitchSpeed = 7.5f;
    [SerializeField, Min(0f)] private float recoilYawSpeed = 2.0f;
    [SerializeField, Min(0f)] private float recoilRollSpeed = 1.3f;
    [SerializeField, Min(0f)] private float recoilPositionImpulse = 0.0055f;

    [Tooltip("Máximo recoil vertical acumulado antes de saturar.")]
    [SerializeField, Min(0f)] private float maxAccumulatedRecoil = 12f;

    // =====================================================================
    // CAMBIO DE ARMA
    // =====================================================================

    [Header("CAMBIO DE ARMA")]
    [SerializeField, Min(0f)] private float weaponSwapPositionImpulse = 0.018f;
    [SerializeField, Min(0f)] private float weaponSwapYawImpulse = 1.0f;
    [SerializeField, Min(0f)] private float weaponSwapRollImpulse = 0.55f;

    // =====================================================================
    // DAÑO / STAGGER
    // =====================================================================

    [Header("DAÑO / STAGGER")]
    [SerializeField, Min(0f)] private float damagePositionImpulse = 0.018f;
    [SerializeField, Min(0f)] private float damagePitchImpulse = 1.8f;
    [SerializeField, Min(0f)] private float damageYawImpulse = 1.1f;
    [SerializeField, Min(0f)] private float damageRollImpulse = 0.7f;

    // =====================================================================
    // SPRINGS
    // =====================================================================

    [Header("SPRINGS DE POSICIÓN")]
    [SerializeField, Min(0f)] private float positionSmoothTime = 0.085f;
    [SerializeField, Min(0f)] private float positionMaxSpeed = 8f;

    [Header("SPRINGS DE ROTACIÓN")]
    [SerializeField, Min(0f)] private float rotationSmoothTime = 0.075f;
    [SerializeField, Min(0f)] private float rotationMaxSpeed = 180f;

    // =====================================================================
    // FOV
    // =====================================================================

    [Header("FOV")]
    [SerializeField] private bool useDynamicFOV = true;
    [SerializeField, Min(1f)] private float baseFOV = 75f;
    [SerializeField, Min(0f)] private float sprintFOV = 2.0f;
    [SerializeField, Min(0f)] private float landingFOV = 1.0f;
    [SerializeField, Min(0f)] private float shotFOV = 0.25f;
    [SerializeField, Min(0f)] private float FOVSmoothTime = 0.10f;

    // =====================================================================
    // RUNTIME
    // =====================================================================

    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;

    private Vector3 positionCurrent;
    private Vector3 positionVelocity;

    private Vector3 rotationCurrent;
    private Vector3 rotationVelocity;

    private Vector3 movementLocalVelocity;
    private Vector3 previousMovementVelocity;
    private Vector3 currentAcceleration;

    private Vector2 lookDelta;
    private Vector2 previousLookDelta;

    private bool grounded;
    private bool sprinting;
    private bool crouching;

    private float bobPhase;
    private float breathingPhase;
    private float microPhase;

    private float movementSpeed01;
    private float horizontalSpeed;

    private float dynamicFOVKick;
    private float fovVelocity;

    private float accumulatedRecoilPitch;

    private float randomSeedX;
    private float randomSeedY;
    private float randomSeedZ;

    // =====================================================================
    // UNITY
    // =====================================================================

    private void Reset()
    {
        povCamera = GetComponent<Camera>();
        cameraTransform = transform;
    }

    private void Awake()
    {
        if (cameraTransform == null)
            cameraTransform = transform;

        if (povCamera == null)
            povCamera = cameraTransform.GetComponent<Camera>();

        baseLocalPosition = cameraTransform.localPosition;
        baseLocalRotation = cameraTransform.localRotation;

        if (povCamera != null)
            baseFOV = povCamera.fieldOfView;

        randomSeedX = Random.Range(0f, 1000f);
        randomSeedY = Random.Range(0f, 1000f);
        randomSeedZ = Random.Range(0f, 1000f);
    }

    private void LateUpdate()
    {
        float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.05f);

        UpdateMovement(dt);
        UpdateBreathing(dt);
        UpdateLookMomentum(dt);

        Vector3 targetPosition = BuildPositionTarget();
        Vector3 targetRotation = BuildRotationTarget();

        positionCurrent = Vector3.SmoothDamp(
            positionCurrent,
            targetPosition,
            ref positionVelocity,
            positionSmoothTime,
            positionMaxSpeed,
            dt
        );

        rotationCurrent = Vector3.SmoothDamp(
            rotationCurrent,
            targetRotation,
            ref rotationVelocity,
            rotationSmoothTime,
            rotationMaxSpeed,
            dt
        );

        cameraTransform.localPosition =
            baseLocalPosition + positionCurrent;

        cameraTransform.localRotation =
            baseLocalRotation *
            Quaternion.Euler(rotationCurrent);

        UpdateDynamicFOV(dt);

        previousLookDelta = lookDelta;
        lookDelta = Vector2.zero;
    }

    // =====================================================================
    // API PÚBLICA
    // =====================================================================

    /// <summary>
    /// Llamar una vez por frame desde el Character Controller.
    /// La velocidad debe estar expresada en espacio LOCAL del jugador.
    /// </summary>
    public void SetMovementState(
        Vector3 localVelocity,
        bool isGrounded,
        bool isSprinting,
        bool isCrouching)
    {
        movementLocalVelocity = localVelocity;
        grounded = isGrounded;
        sprinting = isSprinting;
        crouching = isCrouching;
    }

    /// <summary>
    /// Llamar desde el sistema de mouse / stick.
    /// Este método NO gira al jugador: solamente alimenta la inercia de cámara.
    /// </summary>
    public void SetLookDelta(Vector2 delta)
    {
        lookDelta = Vector2.ClampMagnitude(delta, maxLookDelta);
    }

    /// <summary>
    /// Impulso de salto.
    /// </summary>
    public void OnJump()
    {
        positionVelocity.y += jumpUpImpulse;
        rotationVelocity.x -= jumpPitchImpulse;
    }

    /// <summary>
    /// Aterrizaje.
    /// impactSpeed debe ser la velocidad vertical descendente absoluta.
    /// </summary>
    public void OnLand(float impactSpeed)
    {
        float intensity = Mathf.InverseLerp(
            landingMinSpeed,
            landingMaxSpeed,
            Mathf.Abs(impactSpeed)
        );

        intensity = Mathf.Clamp01(intensity);

        if (intensity <= 0f)
            return;

        positionVelocity.y -= landingVerticalImpulse * intensity;
        rotationVelocity.x += landingPitchImpulse * intensity;

        float side = Random.value > 0.5f ? 1f : -1f;
        rotationVelocity.z +=
            side * landingRollImpulse * intensity;

        dynamicFOVKick += landingFOV * intensity;
    }

    /// <summary>
    /// Disparo.
    ///
    /// recoilPitch/yaw/roll permiten que cada arma tenga su propia respuesta.
    /// Ejemplo de fusil: pitch 1.2, yaw 0.15, roll 0.05.
    /// </summary>
    public void OnShot(
        float recoilPitch = 1.0f,
        float recoilYaw = 0.15f,
        float recoilRoll = 0.05f)
    {
        recoilPitch = Mathf.Max(0f, recoilPitch);
        recoilYaw = Mathf.Abs(recoilYaw);
        recoilRoll = Mathf.Abs(recoilRoll);

        float randomYaw =
            Random.Range(-recoilYaw, recoilYaw);

        float randomRoll =
            Random.Range(-recoilRoll, recoilRoll);

        float multiplier = recoilMultiplier;

        accumulatedRecoilPitch = Mathf.Clamp(
            accumulatedRecoilPitch +
            recoilPitch * multiplier,
            0f,
            maxAccumulatedRecoil
        );

        rotationVelocity.x +=
            accumulatedRecoilPitch * recoilPitchSpeed * 0.10f;

        rotationVelocity.y +=
            randomYaw * recoilYawSpeed * multiplier;

        rotationVelocity.z +=
            randomRoll * recoilRollSpeed * multiplier;

        positionVelocity.z -=
            recoilPositionImpulse * multiplier;

        dynamicFOVKick += shotFOV * multiplier;
    }

    /// <summary>
    /// Llamar cuando el arma empieza a cambiar.
    /// </summary>
    public void OnWeaponSwap()
    {
        float side = Random.value > 0.5f ? 1f : -1f;

        positionVelocity.x +=
            side * weaponSwapPositionImpulse;

        positionVelocity.z -=
            weaponSwapPositionImpulse * 0.35f;

        rotationVelocity.y +=
            side * weaponSwapYawImpulse;

        rotationVelocity.z +=
            -side * weaponSwapRollImpulse;
    }

    /// <summary>
    /// Llamar desde un Animation Event del pie.
    /// leftFoot = true para izquierdo, false para derecho.
    /// </summary>
    public void OnStep(bool leftFoot)
    {
        float side = leftFoot ? -1f : 1f;

        positionVelocity.y -=
            stepVerticalImpulse;

        positionVelocity.x +=
            side * stepLateralImpulse;

        rotationVelocity.z +=
            side * stepRollImpulse;

        rotationVelocity.x +=
            stepPitchImpulse;
    }

    /// <summary>
    /// Impacto recibido.
    /// intensity = 0..1
    /// </summary>
    public void OnDamage(float intensity = 1f)
    {
        intensity = Mathf.Clamp01(intensity);

        if (intensity <= 0f)
            return;

        float side =
            Random.value > 0.5f
                ? 1f
                : -1f;

        positionVelocity.x +=
            side * damagePositionImpulse * intensity;

        positionVelocity.z -=
            damagePositionImpulse * intensity;

        rotationVelocity.x +=
            damagePitchImpulse * intensity;

        rotationVelocity.y +=
            side * damageYawImpulse * intensity;

        rotationVelocity.z +=
            -side * damageRollImpulse * intensity;
    }

    /// <summary>
    /// Permite reducir la sensación de cámara para cinemáticas.
    /// 1 = normal, 0 = sin movimiento procedural.
    /// </summary>
    public void SetMotionWeight(float weight)
    {
        float clamped =
            Mathf.Clamp01(weight);

        positionCurrent *= clamped;
        positionVelocity *= clamped;

        rotationCurrent *= clamped;
        rotationVelocity *= clamped;
    }

    /// <summary>
    /// Limpia todos los impulsos actuales.
    /// </summary>
    public void ResetMotion()
    {
        positionCurrent = Vector3.zero;
        positionVelocity = Vector3.zero;
        rotationCurrent = Vector3.zero;
        rotationVelocity = Vector3.zero;

        accumulatedRecoilPitch = 0f;
        dynamicFOVKick = 0f;

        bobPhase = 0f;
        previousMovementVelocity = movementLocalVelocity;
    }

    // =====================================================================
    // ACTUALIZACIÓN INTERNA
    // =====================================================================

    private void UpdateMovement(float dt)
    {
        currentAcceleration =
            (movementLocalVelocity -
             previousMovementVelocity) /
            dt;

        previousMovementVelocity =
            movementLocalVelocity;

        Vector3 horizontalVelocity =
            new Vector3(
                movementLocalVelocity.x,
                0f,
                movementLocalVelocity.z
            );

        horizontalSpeed =
            horizontalVelocity.magnitude;

        movementSpeed01 =
            Mathf.Clamp01(
                horizontalSpeed /
                Mathf.Max(referenceWalkSpeed, 0.01f)
            );

        if (crouching)
            movementSpeed01 *= crouchMotionMultiplier;

        // Fase del paso basada en movimiento real.
        // Deja de "latir" cuando el personaje está prácticamente parado.
        float stepRate =
            sprinting
                ? sprintStepRate
                : walkStepRate;

        if (grounded &&
            horizontalSpeed > minimumMovementSpeed)
        {
            float speedFactor =
                Mathf.Clamp01(
                    horizontalSpeed /
                    Mathf.Max(referenceWalkSpeed, 0.01f)
                );

            bobPhase +=
                dt *
                stepRate *
                Mathf.Lerp(0.45f, 1f, speedFactor) *
                Mathf.PI * 2f;
        }
    }

    private void UpdateBreathing(float dt)
    {
        breathingPhase +=
            dt *
            breathingRate *
            Mathf.PI * 2f;

        microPhase +=
            dt *
            idleMicroFrequency *
            Mathf.PI * 2f;

        if (breathingPhase > Mathf.PI * 2f)
            breathingPhase -= Mathf.PI * 2f;

        if (microPhase > Mathf.PI * 2f)
            microPhase -= Mathf.PI * 2f;
    }

    private void UpdateLookMomentum(float dt)
    {
        Vector2 current =
            Vector2.ClampMagnitude(
                lookDelta,
                maxLookDelta
            );

        // Movimiento real del input: evita que el sway sea un
        // simple "shake" aleatorio.
        Vector2 inputChange =
            (current - previousLookDelta) /
            Mathf.Max(dt, 0.0001f);

        float momentum =
            Mathf.Clamp(
                lookMomentum,
                0f,
                1f
            );

        rotationVelocity.x +=
            -inputChange.y *
            lookPitchSway *
            momentum;

        rotationVelocity.y +=
            -inputChange.x *
            lookYawSway *
            momentum;

        rotationVelocity.z +=
            -inputChange.x *
            lookRollSway *
            momentum;
    }

    private Vector3 BuildPositionTarget()
    {
        Vector3 target =
            Vector3.zero;

        float stanceMultiplier =
            crouching
                ? crouchMotionMultiplier
                : 1f;

        // -------------------------------------------------------------
        // Aceleración / frenada
        // -------------------------------------------------------------

        Vector3 accelerationLag =
            new Vector3(
                -currentAcceleration.x *
                accelerationPosAmount,

                -currentAcceleration.y *
                accelerationPosAmount *
                0.20f,

                -currentAcceleration.z *
                accelerationPosAmount
            );

        target +=
            accelerationLag *
            stanceMultiplier;

        // -------------------------------------------------------------
        // HEAD BOB
        // -------------------------------------------------------------

        float bobWeight =
            movementSpeed01;

        if (!grounded)
            bobWeight *= airborneBobMultiplier;

        if (horizontalSpeed > minimumMovementSpeed ||
            !grounded)
        {
            bool isSprint =
                sprinting &&
                !crouching;

            float sideAmplitude =
                isSprint
                    ? sprintBobSide
                    : walkBobSide;

            float verticalAmplitude =
                isSprint
                    ? sprintBobVertical
                    : walkBobVertical;

            float forwardAmplitude =
                isSprint
                    ? sprintBobForward
                    : walkBobForward;

            float side =
                Mathf.Sin(bobPhase);

            float verticalPrimary =
                Mathf.Abs(Mathf.Sin(bobPhase));

            float verticalSecondary =
                Mathf.Sin(
                    bobPhase * 2f
                );

            float vertical =
                Mathf.Lerp(
                    verticalPrimary,
                    verticalSecondary,
                    secondaryBobBlend
                );

            float forward =
                Mathf.Sin(
                    bobPhase +
                    0.6f
                );

            target.x +=
                side *
                sideAmplitude *
                bobWeight *
                stanceMultiplier;

            target.y +=
                vertical *
                verticalAmplitude *
                bobWeight *
                stanceMultiplier;

            target.z +=
                forward *
                forwardAmplitude *
                bobWeight *
                stanceMultiplier;
        }

        // -------------------------------------------------------------
        // RESPIRACIÓN
        // -------------------------------------------------------------

        float stillness =
            1f -
            Mathf.Clamp01(
                horizontalSpeed /
                Mathf.Max(referenceWalkSpeed * 0.35f, 0.1f)
            );

        if (stillness > 0f)
        {
            float breath =
                Mathf.Sin(
                    breathingPhase
                );

            float breathSub =
                Mathf.Sin(
                    breathingPhase * 0.5f +
                    1.1f
                );

            target.y +=
                breath *
                breathingVertical *
                stillness;

            target.z +=
                breath *
                breathingForward *
                stillness;

            target.x +=
                breathSub *
                idleMicroPosition *
                stillness;
        }

        // -------------------------------------------------------------
        // CAÍDA
        // -------------------------------------------------------------

        if (!grounded &&
            movementLocalVelocity.y < 0f)
        {
            float falling01 =
                Mathf.Clamp01(
                    -movementLocalVelocity.y /
                    10f
                );

            target.y +=
                falling01 *
                fallPositionAmount;
        }

        return target;
    }

    private Vector3 BuildRotationTarget()
    {
        Vector3 target =
            Vector3.zero;

        float stanceMultiplier =
            crouching
                ? crouchMotionMultiplier
                : 1f;

        // -------------------------------------------------------------
        // INCLINACIÓN POR ACELERACIÓN
        // -------------------------------------------------------------

        float pitch =
            -currentAcceleration.z *
            accelerationPitchAmount;

        target.x +=
            pitch *
            stanceMultiplier;

        // -------------------------------------------------------------
        // STRAFE / ROLL
        // -------------------------------------------------------------

        float roll =
            -movementLocalVelocity.x *
            lateralRollAmount;

        target.z +=
            Mathf.Clamp(
                roll,
                -maxMovementRoll,
                maxMovementRoll
            ) *
            stanceMultiplier;

        // -------------------------------------------------------------
        // RESPIRACIÓN
        // -------------------------------------------------------------

        float stillness =
            1f -
            Mathf.Clamp01(
                horizontalSpeed /
                Mathf.Max(referenceWalkSpeed * 0.35f, 0.1f)
            );

        if (stillness > 0f)
        {
            target.x +=
                Mathf.Sin(
                    breathingPhase
                ) *
                breathingPitch *
                stillness;
        }

        // -------------------------------------------------------------
        // MICRO MOVIMIENTO ORGÁNICO
        // -------------------------------------------------------------

        float t =
            Time.time *
            idleMicroFrequency;

        float nX =
            (Mathf.PerlinNoise(
                t,
                randomSeedX
            ) - 0.5f) * 2f;

        float nY =
            (Mathf.PerlinNoise(
                randomSeedY,
                t
            ) - 0.5f) * 2f;

        float nZ =
            (Mathf.PerlinNoise(
                t,
                randomSeedZ
            ) - 0.5f) * 2f;

        float noiseWeight =
            Mathf.Lerp(
                0.15f,
                1f,
                stillness
            );

        target +=
            new Vector3(
                nX,
                nY,
                nZ
            ) *
            idleMicroRotation *
            noiseWeight;

        // -------------------------------------------------------------
        // CAÍDA
        // -------------------------------------------------------------

        if (!grounded &&
            movementLocalVelocity.y < 0f)
        {
            float falling01 =
                Mathf.Clamp01(
                    -movementLocalVelocity.y /
                    10f
                );

            target.x +=
                falling01 *
                fallPitchAmount;
        }

        return target;
    }

    private void UpdateDynamicFOV(float dt)
    {
        if (!useDynamicFOV ||
            povCamera == null)
            return;

        float sprintAmount =
            sprinting &&
            grounded &&
            !crouching
                ? movementSpeed01
                : 0f;

        float targetFOV =
            baseFOV +
            sprintAmount * sprintFOV +
            dynamicFOVKick;

        povCamera.fieldOfView =
            Mathf.SmoothDamp(
                povCamera.fieldOfView,
                targetFOV,
                ref fovVelocity,
                FOVSmoothTime,
                Mathf.Infinity,
                dt
            );

        dynamicFOVKick =
            Mathf.MoveTowards(
                dynamicFOVKick,
                0f,
                dt * 8f
            );

        // El recoil acumulado cae progresivamente.
        accumulatedRecoilPitch =
            Mathf.MoveTowards(
                accumulatedRecoilPitch,
                0f,
                dt * 8f
            );
    }
}
