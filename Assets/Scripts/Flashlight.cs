using Unity.Netcode;
using UnityEngine;

// Linterna del jugador. La luz debe ser un Spot Light hijo de la camara,
// asi apunta automaticamente hacia donde mira el jugador.
//
// EN RED: el estado (encendida / bateria baja) viaja como variable de red escrita
// por el dueno, de modo que tu companero ve tu linterna igual que tu.
// Por eso este script NO se desactiva en los jugadores remotos: necesita seguir
// ejecutandose para aplicar el estado que llega por la red.
public class Flashlight : NetworkBehaviour
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

    // Solo el dueno de la linterna las escribe; todos las leen.
    private readonly NetworkVariable<bool> netIsOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> netLowBattery = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

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
            _baseIntensity = spotLight.intensity;

        ApplyLightState();
    }

    void Update()
    {
        // Solo el dueno maneja el interruptor y gasta bateria...
        if (IsOwner)
            UpdateOwner();

        // ...pero todas las maquinas dibujan la luz segun el estado de red
        ApplyLightState();
    }

    private void UpdateOwner()
    {
        if (Input.GetButtonDown(toggleButton))
            Toggle();

        if (netIsOn.Value)
        {
            _battery -= drainPerSecond * Time.deltaTime;
            if (_battery <= 0f)
            {
                _battery = 0f;
                netIsOn.Value = false;
            }
        }
        else if (rechargeWhenOff && _battery < maxBattery)
        {
            _battery = Mathf.Min(maxBattery, _battery + rechargePerSecond * Time.deltaTime);
        }

        bool low = _battery <= lowBatteryThreshold;
        if (netLowBattery.Value != low)
            netLowBattery.Value = low;
    }

    private void Toggle()
    {
        if (!netIsOn.Value && _battery <= 0f) return; // sin bateria no enciende
        netIsOn.Value = !netIsOn.Value;
    }

    // Aplica el estado de la luz cada frame (y el parpadeo si hay poca bateria)
    private void ApplyLightState()
    {
        if (spotLight == null) return;

        if (!netIsOn.Value)
        {
            spotLight.enabled = false;
            return;
        }

        if (netLowBattery.Value)
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
    public bool IsOn() => netIsOn.Value;
}
