using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Las cuatro llaves del juego.
public enum KeyType { Casco, Espada, Escudo, Armadura }

// Marca este objeto como una de las cuatro llaves. Necesita un NetworkObject.
// El trigger para cogerla se crea solo (una esfera de "radioRecogida" metros);
// los colliders sólidos que tenga el modelo se desactivan para que no estorben.
//
// Al tocarla, el jugador se la queda (la lista de llaves de cada jugador la lleva
// el servidor) y la llave desaparece para todos. Las llaves son personales.
//
// Aspecto, igual que ObjetoSoltado: el modelo gira y flota, y una luz puntual
// (del color de la llave) la señala desde lejos. Si el objeto no tiene luz, se
// crea sola al empezar.
public class Key : NetworkBehaviour
{
    public KeyType tipo = KeyType.Casco;

    [Header("Aspecto")]
    [Tooltip("Hijo con el modelo, que gira y flota. Vacío = el primer hijo con malla (o este mismo objeto).")]
    public Transform visual;
    public float velocidadGiro = 90f;
    public float alturaFlote = 0.08f;

    [Header("Luz")]
    [Tooltip("Crea la luz si el objeto no tiene ninguna.")]
    public bool crearLuz = true;
    public float intensidadLuz = 2.5f;
    public float alcanceLuz = 3.5f;
    public float alturaLuz = 0.7f;

    [Header("Recogida")]
    [Tooltip("Metros (en el mundo) a los que el jugador la coge al tocarla.")]
    public float radioRecogida = 0.6f;

    private Vector3 _visualBase;
    private SphereCollider _trigger;

    public static Color ColorDe(KeyType t) => t switch
    {
        KeyType.Casco => new Color(1f, 0.78f, 0.25f),
        KeyType.Espada => new Color(0.85f, 0.9f, 1f),
        KeyType.Escudo => new Color(0.35f, 0.6f, 1f),
        _ => new Color(1f, 0.35f, 0.25f),
    };

    void Awake()
    {
        // Que el trigger no pare disparos ni líneas de visión (como los objetos soltados)
        gameObject.layer = 2;   // Ignore Raycast

        if (visual == null)
        {
            var r = GetComponentInChildren<MeshRenderer>(true);
            visual = r != null ? r.transform : transform;
        }
        // Punto de partida = donde la has colocado tú. Se guarda YA, antes del primer Update.
        if (visual != null) _visualBase = visual.localPosition;

        // Trigger de recogida. Un MeshCollider normal no puede ser trigger, así que
        // los colliders sólidos se apagan y se usa una esfera.
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (!c.isTrigger) c.enabled = false;
        _trigger = gameObject.AddComponent<SphereCollider>();
        _trigger.isTrigger = true;
        _trigger.center = Vector3.zero;
        Vector3 s = transform.lossyScale;
        float esc = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z), 0.0001f);
        _trigger.radius = radioRecogida / esc;

        if (crearLuz && GetComponentInChildren<Light>(true) == null)
        {
            var go = new GameObject("Brillo");
            go.transform.SetParent(transform, false);
            go.transform.position = transform.position + Vector3.up * alturaLuz;
            var luz = go.AddComponent<Light>();
            luz.type = LightType.Point;
            luz.color = ColorDe(tipo);
            luz.intensity = intensidadLuz;
            luz.range = alcanceLuz;
            luz.shadows = LightShadows.None;
        }
    }

    void Update()
    {
        // Aspecto: en todas las máquinas, no viaja por red. Gira sobre sí misma y
        // flota alrededor de donde la colocaste; nunca se desplaza de ahí.
        if (visual == null || taken.Value) return;
        visual.Rotate(0f, velocidadGiro * Time.deltaTime, 0f, Space.World);
        visual.localPosition = _visualBase + Vector3.up * (Mathf.Sin(Time.time * 2f) * alturaFlote);
    }

    // ---- Llaves de cada jugador (solo en el servidor) ----
    private static readonly Dictionary<ulong, HashSet<KeyType>> _llaves = new Dictionary<ulong, HashSet<KeyType>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar() => _llaves.Clear();

    /// <summary>¿Tiene este jugador esta llave? Solo servidor.</summary>
    public static bool Tiene(ulong clientId, KeyType tipo) =>
        _llaves.TryGetValue(clientId, out var s) && s.Contains(tipo);

    static void Dar(ulong clientId, KeyType tipo)
    {
        if (!_llaves.TryGetValue(clientId, out var s)) _llaves[clientId] = s = new HashSet<KeyType>();
        s.Add(tipo);
    }

    public static string Nombre(KeyType t) => t switch
    {
        KeyType.Casco => "llave del casco",
        KeyType.Espada => "llave de la espada",
        KeyType.Escudo => "llave del escudo",
        _ => "llave de la armadura",
    };

    // Lo que se ve en la cerradura
    public static string Imagen(KeyType t) => t switch
    {
        KeyType.Casco => "un casco",
        KeyType.Espada => "una espada",
        KeyType.Escudo => "un escudo",
        _ => "una armadura",
    };

    // ---- La llave en el mundo ----
    private readonly NetworkVariable<bool> taken = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        taken.OnValueChanged += OnTakenChanged;
        ApplyTaken(taken.Value);
    }

    public override void OnNetworkDespawn() => taken.OnValueChanged -= OnTakenChanged;

    private void OnTakenChanged(bool previous, bool current) => ApplyTaken(current);

    private void ApplyTaken(bool isTaken)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = !isTaken;
        if (_trigger != null) _trigger.enabled = !isTaken;
        foreach (var l in GetComponentsInChildren<Light>(true)) l.enabled = !isTaken;
    }

    // Stay, como ObjetoSoltado: si llegas abatido, la coges al poder actuar sin salir y volver a entrar
    void OnTriggerStay(Collider other)
    {
        if (!IsServer || !IsSpawned || taken.Value) return;

        // Solo jugadores de verdad: los NPC también llevan el tag Player
        var jugador = other.GetComponentInParent<NetworkPlayer>();
        if (jugador == null) return;
        var estado = jugador.GetComponent<PlayerDownedState>();
        if (estado != null && !estado.CanAct) return;

        Dar(jugador.OwnerClientId, tipo);
        taken.Value = true;
        AvisarClientRpc("Has obtenido la " + Nombre(tipo) + ".", new ClientRpcParams
        { Send = new ClientRpcSendParams { TargetClientIds = new[] { jugador.OwnerClientId } } });
    }

    [ClientRpc]
    private void AvisarClientRpc(string mensaje, ClientRpcParams p = default) => MensajePantalla.Mostrar(mensaje);

    void OnDrawGizmos()
    {
        Gizmos.color = ColorDe(tipo);
        Gizmos.DrawWireSphere(transform.position, radioRecogida);
    }
}
