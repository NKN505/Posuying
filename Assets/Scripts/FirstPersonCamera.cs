using UnityEngine;

public class FirstPersonCamera : MonoBehaviour
{
    public float mouseSensitivity = 2f;
    public float topClamp = 80f;
    public float bottomClamp = -80f;
    public StarterAssets.StarterAssetsInputs inputs;

    private float _xRotation = 0f;
    private Transform _playerBody;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        _playerBody = transform.parent;
    }

    void Update()
    {
        float mouseX = inputs.look.x * mouseSensitivity * Time.deltaTime;
        float mouseY = inputs.look.y * mouseSensitivity * Time.deltaTime;

        _xRotation += mouseY;
        _xRotation = Mathf.Clamp(_xRotation, bottomClamp, topClamp);

        transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
        _playerBody.Rotate(Vector3.up * mouseX);
    }
}
