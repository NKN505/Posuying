using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Tarjeta de acceso. Funciona como las llaves: se coloca a mano en el mapa,
// gira, flota y tiene luz; al tocarla el jugador se la queda (el servidor lo
// apunta) y desaparece para todos. Abre las puertas con PuertaTarjeta.
// Necesita un NetworkObject (se añade solo). El trigger y la luz se crean solos.
public enum ColorTarjeta { Azul, Roja }

[RequireComponent(typeof(NetworkObject))]
public class Tarjeta : NetworkBehaviour
{
    [Tooltip("Cada tarjeta abre solo los portones de su mismo color.")]
    public ColorTarjeta color = ColorTarjeta.Azul;

    [Header("Aspecto")]
    [Tooltip("Lo que gira y flota. Vacío = el primer hijo con malla (o este mismo objeto).")]
    public Transform visual;
    public float velocidadGiro = 90f;
    public float alturaFlote = 0.08f;

    [Header("Luz")]
    public bool crearLuz = true;
    public float intensidadLuz = 2.5f;
    public float alcanceLuz = 3.5f;
    public float alturaLuz = 0.7f;

    [Header("Recogida")]
    [Tooltip("Metros (en el mundo) a los que el jugador la coge al tocarla.")]
    public float radioRecogida = 0.6f;

    public static string Nombre(ColorTarjeta c) => c == ColorTarjeta.Roja ? "tarjeta de acceso roja" : "tarjeta de acceso azul";
    public static Color ColorDe(ColorTarjeta c) => c == ColorTarjeta.Roja ? new Color(1f, 0.25f, 0.2f) : new Color(0.3f, 0.85f, 1f);

    private Vector3 _visualBase;
    private SphereCollider _trigger;

    // ---- Quién tiene tarjeta (solo servidor) ----
    private static readonly HashSet<(ulong, ColorTarjeta)> _conTarjeta = new HashSet<(ulong, ColorTarjeta)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar() => _conTarjeta.Clear();

    /// <summary>¿Tiene este jugador la tarjeta de ese color? Solo servidor.</summary>
    public static bool Tiene(ulong clientId, ColorTarjeta c) => _conTarjeta.Contains((clientId, c));

    private readonly NetworkVariable<bool> taken = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    void Awake()
    {
        gameObject.layer = 2;   // Ignore Raycast

        if (visual == null)
        {
            var r = GetComponentInChildren<MeshRenderer>(true);
            visual = r != null ? r.transform : transform;
        }
        _visualBase = visual.localPosition;

        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (!c.isTrigger) c.enabled = false;
        _trigger = gameObject.AddComponent<SphereCollider>();
        _trigger.isTrigger = true;
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
            luz.color = ColorDe(color);
            luz.intensity = intensidadLuz;
            luz.range = alcanceLuz;
            luz.shadows = LightShadows.None;
        }
    }

    void Update()
    {
        if (visual == null || taken.Value) return;
        visual.Rotate(0f, velocidadGiro * Time.deltaTime, 0f, Space.World);
        visual.localPosition = _visualBase + Vector3.up * (Mathf.Sin(Time.time * 2f) * alturaFlote);
    }

    public override void OnNetworkSpawn()
    {
        taken.OnValueChanged += Cambio;
        Aplicar(taken.Value);
    }

    public override void OnNetworkDespawn() => taken.OnValueChanged -= Cambio;

    void Cambio(bool antes, bool ahora) => Aplicar(ahora);

    void Aplicar(bool cogida)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = !cogida;
        foreach (var l in GetComponentsInChildren<Light>(true)) l.enabled = !cogida;
        if (_trigger != null) _trigger.enabled = !cogida;
    }

    void OnTriggerStay(Collider other)
    {
        if (!IsServer || !IsSpawned || taken.Value) return;

        var jugador = other.GetComponentInParent<NetworkPlayer>();
        if (jugador == null) return;
        var estado = jugador.GetComponent<PlayerDownedState>();
        if (estado != null && !estado.CanAct) return;

        _conTarjeta.Add((jugador.OwnerClientId, color));
        taken.Value = true;
        AvisarClientRpc("Has obtenido la " + Nombre(color) + ".", new ClientRpcParams
        { Send = new ClientRpcSendParams { TargetClientIds = new[] { jugador.OwnerClientId } } });
    }

    [ClientRpc]
    void AvisarClientRpc(string mensaje, ClientRpcParams p = default) => MensajePantalla.Mostrar(mensaje);

    void OnDrawGizmos()
    {
        Gizmos.color = ColorDe(color);
        Gizmos.DrawWireSphere(transform.position, radioRecogida);
    }
}
