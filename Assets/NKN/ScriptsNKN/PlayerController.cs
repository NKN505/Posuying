using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerController : Character, IPassiveRegenerator
{
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

    [Header("Regeneracion pasiva")]
    public float regenDelay = 3f;
    public float regenAmountPerSecond = 2f;

    private bool _isStillCrouching = false;

    float IPassiveRegenerator.RegenDelay => regenDelay;
    float IPassiveRegenerator.RegenAmountPerSecond => regenAmountPerSecond;
    bool IPassiveRegenerator.CanRegenerate() => _isStillCrouching;
    
    protected override void Awake(){

        base.Awake();

        SetIsLiving(true);
        SetIsPlayer(true);
        SetIsAvailable(true);
        SetIsJumping(false);

        cameraTransform = Camera.main.transform;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        standingHeight = controller.height;
        standingCenter = controller.center;
        standingCameraPos = cameraTransform.localPosition;
    }

    protected override void Update(){

        base.Update();

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
            Jump();

        if (isClimbing) return;

        bool wantsToCrouch = Input.GetButton("Crouch");

        SetIsCrouching(wantsToCrouch);
        SetIsSprinting(!wantsToCrouch && Input.GetButton("Sprint"));
        UpdateCrouch(wantsToCrouch);

        // DESPLAZAMIENTO
        float movex = Input.GetAxis("Horizontal");
        float movez = Input.GetAxis("Vertical");

        Vector3 move = transform.right * movex + transform.forward * movez;

        controller.Move(move * GetSpeed() * Time.deltaTime);

        // Condicion de regeneracion pasiva (leida por Character via IPassiveRegenerator)
        bool isStill = Mathf.Approximately(movex, 0f) && Mathf.Approximately(movez, 0f);
        _isStillCrouching = wantsToCrouch && isStill;

    }

    private bool TryClimb()
    {
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

    protected override void Die()
    {
        Debug.Log("Jugador muerto - reiniciando escena");
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}