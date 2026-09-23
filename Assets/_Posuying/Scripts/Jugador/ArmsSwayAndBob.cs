using UnityEngine;

public class ArmsSwayAndBob : MonoBehaviour
{
    [Header("Bob Settings")]
    public float bobFrequency = 2.5f;
    public float bobAmplitudeY = 0.02f;
    public float bobAmplitudeX = 0.01f;
    public float bobRotationAmount = 3f;

    [Header("Sway Settings")]
    public float swayAmount = 0.01f;
    public float swaySpeed = 8f;
    public float swayRotationAmount = 2f;

    [Header("References")]
    public CharacterController characterController;

    private Vector3 _initialPosition;
    private Quaternion _initialRotation;
    private float _bobTimer;

    void Start()
    {
        _initialPosition = transform.localPosition;
        _initialRotation = transform.localRotation;
    }

    void Update()
    {
        float speed = characterController != null ? characterController.velocity.magnitude : 0f;
        bool isMoving = speed > 0.1f;

        float targetFrequency = bobFrequency * (speed > 4f ? 1.5f : 1f); // más rápido al correr

        if (isMoving)
        {
            _bobTimer += Time.deltaTime * targetFrequency;

            float bobY = Mathf.Sin(_bobTimer) * bobAmplitudeY;
            float bobX = Mathf.Cos(_bobTimer * 0.5f) * bobAmplitudeX;

            // Rotación al caminar — simula flexión de brazos
            float rotX = Mathf.Sin(_bobTimer) * bobRotationAmount;
            float rotZ = Mathf.Cos(_bobTimer * 0.5f) * bobRotationAmount * 0.5f;

            Vector3 targetPos = _initialPosition + new Vector3(bobX, bobY, 0);
            Quaternion targetRot = _initialRotation * Quaternion.Euler(rotX, 0, rotZ);

            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, Time.deltaTime * 10f);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRot, Time.deltaTime * 10f);
        }
        else
        {
            _bobTimer = 0f;
            transform.localPosition = Vector3.Lerp(transform.localPosition, _initialPosition, Time.deltaTime * 5f);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, _initialRotation, Time.deltaTime * 5f);
        }

        // Sway con el ratón
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");
        Vector3 sway = new Vector3(-mouseX * swayAmount, -mouseY * swayAmount, 0);
        Quaternion swayRot = Quaternion.Euler(-mouseY * swayRotationAmount, mouseX * swayRotationAmount, 0);

        transform.localPosition = Vector3.Lerp(transform.localPosition, transform.localPosition + sway, Time.deltaTime * swaySpeed);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, transform.localRotation * swayRot, Time.deltaTime * swaySpeed);
    }
}
