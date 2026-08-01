using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerController : Character, IPassiveRegenerator
{
    [Tooltip("Camara de ESTE jugador. Si se deja vacia se busca entre sus hijos.")]
    public Camera playerCamera;

    private Transform cameraTransform;
    private float pitch = 0f;

    [Header("Agacharse")]
    public float crouchHeight = 1f;
    public float crouchCameraY = 1f;

    private float standingHeight;
    private Vector3 standingCenter;
    private Vector3 standingCameraPos;

    [Header("Escalar obstaculos")]
    public float climbCheckDistance = 0.7f;
    public float climbMaxHeight = 3f;
    public float climbBaseSpeed = 3f;
    public float climbMinSpeed = 0.3f;
    public float climbMaxTime = 1.5f;
    public LayerMask climbLayerMask = ~0;

    private bool isClimbing = false;

    [Header("Coste de estamina")]
    public float sprintStaminaPerSecond = 20f;
    public float jumpStaminaCost = 15f;
    public float climbStaminaPerSecond = 15f;

    [Header("Regeneracion pasiva")]
    public float regenDelay = 3f;
    public float regenAmountPerSecond = 2f;

    [Header("Dano por caida")]
    [Tooltip("Metros de caida sin dano")]
    public float fallSafeHeight = 4f;
    [Tooltip("Metros de caida a partir de los cuales la caida es mortal")]
    public float fallLethalHeight = 15f;
    [Tooltip("Dano por cada metro caido por encima del umbral seguro")]
    public float fallDamagePerMeter = 40f;

    private bool _isStillCrouching = false;

    private bool _wasGrounded = true;
    private float _fallPeakY;

    float IPassiveRegenerator.RegenDelay => regenDelay;
    float IPassiveRegenerator.RegenAmountPerSecond => regenAmountPerSecond;
    bool IPassiveRegenerator.CanRegenerate() => _isStillCrouching;
    
    protected override void Awake(){

        base.Awake();

        SetIsLiving(true);
        SetIsPlayer(true);
        SetIsAvailable(true);
        SetIsJumping(false);

        // Cada jugador usa SU propia camara: con varios jugadores en red,
        // Camera.main podria devolver la camara de otro jugador.
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);
        cameraTransform = playerCamera.transform;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        standingHeight = controller.height;
        standingCenter = controller.center;
        standingCameraPos = cameraTransform.localPosition;

        _fallPeakY = transform.position.y;
    }

    protected override void Update(){

        base.Update();

        // DEBUG: matar al jugador con K para probar el spawn
        if (Input.GetKeyDown(KeyCode.K))
            RequestDamage(GetHealth());

        // MOVIMIENTO DE CAMARA (siempre activo, incluso escalando)
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        transform.Rotate(0, mouseX, 0);

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -90f, 90f);

        cameraTransform.localRotation = Quaternion.Euler(pitch, 0, 0);

        if (isClimbing) return;

        ApplyGravity();

        if (Input.GetButtonDown("Jump") && !TryClimb())
        {
            // El salto cuesta estamina; solo si estamos en el suelo y hay suficiente
            if (controller.isGrounded && ConsumeStamina(jumpStaminaCost))
                Jump();
        }

        if (isClimbing) return;

        bool wantsToCrouch = Input.GetButton("Crouch");

        // DESPLAZAMIENTO
        float movex = Input.GetAxis("Horizontal");
        float movez = Input.GetAxis("Vertical");

        bool isMoving = !(Mathf.Approximately(movex, 0f) && Mathf.Approximately(movez, 0f));

        // SPRINT: solo si nos movemos, no agachados, con boton pulsado y queda estamina
        bool wantsSprint = !wantsToCrouch && isMoving && Input.GetButton("Sprint");
        bool sprinting = wantsSprint && GetStamina() > 0f;
        if (sprinting)
            DrainStamina(sprintStaminaPerSecond * Time.deltaTime);

        SetIsCrouching(wantsToCrouch);
        SetIsSprinting(sprinting);
        UpdateCrouch(wantsToCrouch);

        Vector3 move = transform.right * movex + transform.forward * movez;

        controller.Move(move * GetSpeed() * Time.deltaTime);

        // Condicion de regeneracion pasiva (leida por Character via IPassiveRegenerator)
        _isStillCrouching = wantsToCrouch && !isMoving;

        TrackFall();

    }

    private void TrackFall()
    {
        bool grounded = controller.isGrounded;

        if (grounded)
        {
            // Acabamos de aterrizar: calcular la distancia caida desde el punto mas alto
            if (!_wasGrounded)
            {
                float fallDistance = _fallPeakY - transform.position.y;
                HandleFallDamage(fallDistance);
            }
            _fallPeakY = transform.position.y;
        }
        else
        {
            // En el aire: guardar la altura maxima alcanzada
            if (transform.position.y > _fallPeakY)
                _fallPeakY = transform.position.y;
        }

        _wasGrounded = grounded;
    }

    private void HandleFallDamage(float fallDistance)
    {
        if (fallDistance <= fallSafeHeight)
            return;

        if (fallDistance >= fallLethalHeight)
        {
            Debug.Log("Caida mortal: " + fallDistance.ToString("F1") + "m");
            RequestDamage(GetHealth());
            return;
        }

        float damage = (fallDistance - fallSafeHeight) * fallDamagePerMeter;
        Debug.Log("Dano por caida: " + damage.ToString("F0") + " (" + fallDistance.ToString("F1") + "m)");
        RequestDamage(damage);
    }

    // Reinicia el seguimiento de caida (tras escalar o reaparecer) para evitar dano falso
    private void ResetFallTracking()
    {
        _fallPeakY = transform.position.y;
        _wasGrounded = true;
    }

    private bool TryClimb()
    {
        // Sin estamina no se puede escalar
        if (GetStamina() <= 0f)
            return false;

        Vector3 origin = transform.position + Vector3.up * (controller.height * 0.5f);

        if (!Physics.Raycast(origin, transform.forward, climbCheckDistance, climbLayerMask))
            return false;

        StartCoroutine(ClimbRoutine(transform.forward));
        return true;
    }

    private System.Collections.IEnumerator ClimbRoutine(Vector3 forward)
    {
        isClimbing = true;
        // IMPORTANTE: el CharacterController se mantiene ACTIVADO durante toda la
        // escalada. Movemos con controller.Move() para que las colisiones sigan
        // respetandose y sea imposible atravesar cualquier objeto.

        float climbed = 0f;
        float elapsed = 0f;
        bool cleared = false;

        // FASE 1: subir mientras el obstaculo siga delante
        while (elapsed < climbMaxTime && climbed < climbMaxHeight)
        {
            elapsed += Time.deltaTime;

            // ¿Hemos superado ya el borde superior del obstaculo?
            // Rayo a la altura de los pies: cuando deja de chocar, lo hemos coronado.
            Vector3 feetRay = transform.position + Vector3.up * 0.1f;
            if (!Physics.Raycast(feetRay, forward, climbCheckDistance + 0.2f, climbLayerMask))
            {
                cleared = true;
                break;
            }

            // Escalar consume estamina; si se agota, dejamos de subir y caemos
            if (!DrainStamina(climbStaminaPerSecond * Time.deltaTime))
                break;

            float progress = climbed / climbMaxHeight;
            float speed = Mathf.Lerp(climbBaseSpeed, climbMinSpeed, progress);
            float step = speed * Time.deltaTime;

            float prevY = transform.position.y;
            controller.Move(Vector3.up * step);
            float actualRise = transform.position.y - prevY;
            climbed += actualRise;

            // Si apenas subimos pese a intentarlo, hay un techo encima: no se puede escalar mas
            if (actualRise < step * 0.5f)
                break;

            yield return null;
        }

        // FASE 2: solo si hemos coronado el obstaculo, avanzamos para subirnos encima.
        // Si no (timeout, altura maxima o techo), no avanzamos y el jugador cae por gravedad.
        if (cleared)
        {
            // Pequeño margen extra de subida para no engancharnos en el borde
            float margin = 0f;
            while (margin < 0.25f)
            {
                float step = climbMinSpeed * Time.deltaTime + 0.02f;
                controller.Move(Vector3.up * step);
                margin += step;
                yield return null;
            }

            // Avanzar sobre la superficie. controller.Move respeta colisiones,
            // asi que si quedara pared delante el jugador simplemente no avanzaria.
            float forwardDist = 0f;
            float targetForward = climbCheckDistance + 0.3f;
            while (forwardDist < targetForward)
            {
                float step = climbBaseSpeed * Time.deltaTime;
                controller.Move(forward * step);
                forwardDist += step;
                yield return null;
            }
        }

        isClimbing = false;
        ResetFallTracking(); // no contar la subida como una caida
    }

    private void UpdateCrouch(bool crouching)
    {
        float targetHeight = crouching ? crouchHeight : standingHeight;
        float targetCameraY = crouching ? crouchCameraY : standingCameraPos.y;

        controller.height = Mathf.Lerp(controller.height, targetHeight, Time.deltaTime * 10f);
        controller.center = new Vector3(standingCenter.x, controller.height / 2f, standingCenter.z);

        Vector3 camPos = cameraTransform.localPosition;
        camPos.y = Mathf.Lerp(camPos.y, targetCameraY, Time.deltaTime * 10f);
        cameraTransform.localPosition = camPos;
    }

    // La muerte la decide el SERVIDOR (es quien lleva la vida).
    protected override void Die()
    {
        if (!IsServer) return;

        Debug.Log("Jugador muerto - reapareciendo");

        FullRestore();        // el servidor devuelve la vida al maximo
        RespawnClientRpc();   // y avisa al dueno para que se mueva al punto de spawn
    }

    // La posicion del jugador la manda su dueno (NetworkTransform en modo Owner),
    // por eso el teletransporte lo tiene que hacer el, no el servidor.
    [ClientRpc]
    private void RespawnClientRpc()
    {
        if (!IsOwner) return;

        var networkPlayer = GetComponent<NetworkPlayer>();
        if (networkPlayer != null)
            networkPlayer.MoveToSpawnPoint();

        FullRestore();        // restaura la estamina local
        pitch = 0f;
        ResetFallTracking();  // no contar el teletransporte como una caida
    }
}