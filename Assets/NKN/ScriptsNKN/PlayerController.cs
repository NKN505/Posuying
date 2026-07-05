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

        bool wantsToCrouch = Input.GetKey(KeyCode.LeftControl);
        SetIsCrouching(wantsToCrouch);
        SetIsSprinting(!wantsToCrouch && Input.GetKey(KeyCode.LeftShift));
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
        controller.enabled = false;

        float chestOffset = standingHeight * 0.5f;
        float climbed = 0f;
        float elapsed = 0f;
        bool foundLedge = false;
        Vector3 ledgePosition = Vector3.zero;

        while (elapsed < climbMaxTime && climbed < climbMaxHeight)
        {
            elapsed += Time.deltaTime;

            float progress = climbed / climbMaxHeight;
            float speed = Mathf.Lerp(climbBaseSpeed, climbMinSpeed, progress);
            float step = speed * Time.deltaTime;

            // Si hay techo/saliente justo encima, no seguir subiendo (evita atravesar geometria)
            if (Physics.Raycast(transform.position, Vector3.up, step + 0.05f, climbLayerMask))
                break;

            climbed += step;
            transform.position += Vector3.up * step;

            // Solo buscamos aterrizaje cuando ya hemos superado la altura de la pared
            // (el rayo hacia delante deja de chocar con ella)
            bool wallStillBlocking = Physics.Raycast(
                transform.position + Vector3.up * chestOffset,
                forward, climbCheckDistance + 0.3f, climbLayerMask);

            if (!wallStillBlocking)
            {
                Vector3 aheadPoint = transform.position + forward * (climbCheckDistance + 0.5f) + Vector3.up * 0.5f;
                if (Physics.Raycast(aheadPoint, Vector3.down, out RaycastHit landHit, 2f, climbLayerMask))
                {
                    foundLedge = true;
                    ledgePosition = landHit.point;
                    ledgePosition.y += 0.05f;
                    break;
                }
            }

            yield return null;
        }

        if (foundLedge)
        {
            Vector3 start = transform.position;
            float t = 0f;
            while (t < 0.15f)
            {
                t += Time.deltaTime;
                transform.position = Vector3.Lerp(start, ledgePosition, t / 0.15f);
                yield return null;
            }
            transform.position = ledgePosition;
        }
        // Si no se encontro el borde a tiempo, el jugador se queda donde llego y cae con la gravedad normal

        controller.enabled = true;
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