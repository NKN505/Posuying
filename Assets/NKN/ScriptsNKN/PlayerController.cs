using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerController : Character
{
    private Transform cameraTransform;
    private float pitch = 0f;

    protected override void Awake(){

        base.Awake();

        SetIsLiving(true);
        SetIsPlayer(true);
        SetIsAvailable(true);
        SetIsJumping(false);

        cameraTransform = Camera.main.transform;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    protected override void Update(){

        base.Update();

        ApplyGravity();

        // DESPLAZAMIENTO
        float movex = Input.GetAxis("Horizontal");
        float movez = Input.GetAxis("Vertical");

        Vector3 move = transform.right * movex + transform.forward * movez;

        controller.Move(move * GetSpeed() * Time.deltaTime);

        // MOVIMIENTO DE CAMARA
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        transform.Rotate(0, mouseX, 0);

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -90f, 90f);

        cameraTransform.localRotation = Quaternion.Euler(pitch, 0, 0);

    }

    protected override void Die()
    {
        Debug.Log("Jugador muerto - reiniciando escena");
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}