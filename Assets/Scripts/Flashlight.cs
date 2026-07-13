using UnityEngine;

// Linterna del jugador. La luz debe ser un Spot Light hijo de la camara,
// asi apunta automaticamente hacia donde mira el jugador.
public class Flashlight : MonoBehaviour
{
    [Header("Referencias")]
    public Light spotLight;

    [Header("Input")]
    [Tooltip("Nombre de la accion en el Input Manager (F / R1 / RS)")]
    public string toggleButton = "Linterna";

    [Header("Bateria")]
    public float maxBattery = 100f;
    public float drainPerSecond = 5f;
    public float rechargePerSecond = 3f;
    public bool rechargeWhenOff = true;

    [Header("Parpadeo (bateria baja)")]
    public float lowBatteryThreshold = 20f;
    public float flickerInterval = 0.1f;

    private bool _isOn = false;
    private float _battery;
    private float _baseIntensity;
    private float _flickerTimer;

    void Start()
    {
        _battery = maxBattery;

        // Si no se asigno a mano, intentar encontrar el Spot Light en los hijos
        if (spotLight == null)
            spotLight = GetComponentInChildren<Light>();

        if (spotLight != null)
        {
            _baseIntensity = spotLight.intensity;
        }

        _isOn = false;
        ApplyLightState();
    }

    void Update()
    {
        if (Input.GetButtonDown(toggleButton))
            Toggle();

        if (_isOn)
        {
            _battery -= drainPerSecond * Time.deltaTime;
            if (_battery <= 0f)
            {
                _battery = 0f;
                _isOn = false;
            }
        }
        else if (rechargeWhenOff && _battery < maxBattery)
        {
            _battery = Mathf.Min(maxBattery, _battery + rechargePerSecond * Time.deltaTime);
        }

        ApplyLightState();
    }

    private void Toggle()
    {
        if (!_isOn && _battery <= 0f) return; // sin bateria no enciende
        _isOn = !_isOn;
        Debug.Log("Linterna: " + (_isOn ? "ENCENDIDA" : "APAGADA"));
    }

    // Aplica el estado de la luz cada frame segun _isOn (y el parpadeo si hay poca bateria)
    private void ApplyLightState()
    {
        if (spotLight == null) return;

        if (!_isOn)
        {
            spotLight.enabled = false;
            return;
        }

        // Encendida: con poca bateria parpadea y baja de intensidad
        if (_battery <= lowBatteryThreshold)
        {
            _flickerTimer -= Time.deltaTime;
            if (_flickerTimer <= 0f)
            {
                _flickerTimer = flickerInterval;
                spotLight.enabled = Random.value > 0.35f;
            }
            spotLight.intensity = _baseIntensity * Random.Range(0.4f, 0.8f);
        }
        else
        {
            spotLight.enabled = true;
            spotLight.intensity = _baseIntensity;
        }
    }

    // Utiles para un futuro indicador en el HUD
    public float GetBatteryRatio() => _battery / maxBattery;
    public bool IsOn() => _isOn;
}
