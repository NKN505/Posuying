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

    [Header("Manos")]
    [Tooltip("La linterna propia no ilumina tus brazos ni tu arma (quedaban quemados de luz " +
             "al estar pegados al foco). El resto de luces si los sigue iluminando.")]
    public bool noIluminarManos = true;

    // Capa de luz reservada a las manos ("Light Layer 2" en URP). Se hace con capas
    // de luz y no con el Culling Mask de la luz porque el proyecto usa Forward+,
    // que ignora ese mask.
    private const int CapaLuzManos = 1 << 2;
    private float _siguienteRepaso;

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
        {
            UpdateOwner();
            ApartarManosDeLaLinterna();
        }

        // ...pero todas las maquinas dibujan la luz segun el estado de red
        ApplyLightState();
    }

    private void UpdateOwner()
    {
        // Con una ventana abierta no interpretamos teclas de juego
        if (!UIState.BlocksGameplay && Controles.Pulsado(Accion.Linterna))
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

    // Las manos pasan a una capa de luz propia que la linterna no tiene, y a todas
    // las demas luces se les anade esa capa para que las sigan iluminando.
    // Se repasa cada segundo: aparecen armas, luces de objetos soltados, fogonazos
    // y linternas de companeros durante la partida.
    private void ApartarManosDeLaLinterna()
    {
        if (!noIluminarManos || spotLight == null || Time.time < _siguienteRepaso) return;
        _siguienteRepaso = Time.time + 1f;

        spotLight.renderingLayerMask &= ~CapaLuzManos;

        // Todo lo que cuelga de la camara son los brazos y el arma en primera persona
        Transform camara = spotLight.transform.parent;
        if (camara != null)
            foreach (var r in camara.GetComponentsInChildren<Renderer>(true))
                r.renderingLayerMask = (uint)CapaLuzManos;

        foreach (var luz in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (luz != spotLight)
                luz.renderingLayerMask |= CapaLuzManos;
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
