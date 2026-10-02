using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Puerta simple o doble. Va en la RAÍZ de la puerta, junto a un NetworkObject.
//
// - Sin llave echada: se abre sola cuando un jugador llega a ella (hacia el lado
//   contrario a él) y se vuelve a cerrar sola cuando ya no queda nadie cerca.
// - Cerrada con llave: al llegar, el jugador ve en pantalla "Está cerrada, hay una
//   imagen de ... en la cerradura." Si lleva esa llave, la abre y desde entonces la puerta queda sin
//   llave para siempre: se abre y se cierra al pasar como cualquier otra.
//
// Lo decide el SERVIDOR y se ve igual en todas las máquinas. Las hojas giran
// sobre su propio origen (en nuestros modelos, el eje de las bisagras).
public class Door : NetworkBehaviour
{
    [Tooltip("Marcado: puerta doble. Desmarcado: simple.")]
    public bool esDoble = false;

    [Tooltip("Qué llave la abre.")]
    public KeyType llave = KeyType.Casco;

    [Tooltip("Marcado: empieza cerrada y hay que abrirla la primera vez (con llave, o desde el otro lado). Desmarcado: se abre al pasar.")]
    public bool cerrada = false;

    [Tooltip("Solo si está Cerrada. Marcado: no hace falta llave; desde un lado dice \"Está cerrada por el otro lado\" y desde el otro se abre. " +
             "Con la puerta seleccionada, la flecha VERDE señala el lado desde el que se abre.")]
    public bool seAbreDesdeElOtroLado = false;
    [Tooltip("Cambia de lado la flecha verde (el lado desde el que se abre).")]
    public bool invertirLado = false;

    [Header("Avanzado (se rellena solo)")]
    [Tooltip("Hoja de la simple, o la izquierda de la doble. Vacío = busca un hijo con 'Hoja' en el nombre.")]
    public Transform hoja;
    [Tooltip("Solo dobles: la segunda hoja.")]
    public Transform hojaDerecha;
    [Range(30f, 180f)] public float anguloApertura = 95f;
    [Tooltip("Grados por segundo.")]
    public float velocidad = 200f;
    [Tooltip("Distancia a la que el jugador 'llega' a la puerta (en horizontal).")]
    public float radio = 1.4f;
    [Tooltip("Segundos sin nadie cerca antes de que se cierre sola.")]
    public float esperaCierre = 1.5f;

    // ---- Estado de red (lo escribe el servidor) ----
    private readonly NetworkVariable<bool> netAbierta = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> netCerrada = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<sbyte> netSentido = new NetworkVariable<sbyte>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ---- Local ----
    private Quaternion _cerradaA, _cerradaB;
    private Vector3 _ejeA, _ejeB;
    private float _angulo;
    private Vector3 _centro;                                       // centro del hueco, en local
    private Vector3 _normal;                                       // dirección "lado que abre", en local
    private readonly HashSet<ulong> _cerca = new HashSet<ulong>(); // jugadores ya junto a la puerta (servidor)
    private float _sinNadie;
    private readonly Dictionary<ulong, float> _proxAviso = new Dictionary<ulong, float>(); // cuándo repetir el aviso de "cerrada"

    void Reset() => BuscarHojas();

    void BuscarHojas()
    {
        Transform a = null, b = null;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform || !t.name.Contains("Hoja")) continue;
            if (a == null) a = t; else if (b == null) b = t;
        }
        if (a != null && b != null && b.name.EndsWith("_I")) { var x = a; a = b; b = x; }
        if (hoja == null) hoja = a;
        if (hojaDerecha == null) hojaDerecha = b;
        esDoble = b != null;
    }

    void Awake()
    {
        if (hoja == null) BuscarHojas();
        if (hoja != null) { _cerradaA = hoja.localRotation; _ejeA = EjeVertical(hoja); }
        if (hojaDerecha != null) { _cerradaB = hojaDerecha.localRotation; _ejeB = EjeVertical(hojaDerecha); }

        // Centro del hueco: el centro de todo lo que se ve de la puerta
        var rs = GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            _centro = transform.InverseTransformPoint(b.center);
        }
        _normal = transform.InverseTransformDirection(NormalMundo());
    }

    // Horizontal y perpendicular a la hoja cerrada; apunta al lado desde el que se abre
    Vector3 NormalMundo()
    {
        Vector3 ancho = transform.right;
        if (hoja != null)
        {
            var r = hoja.GetComponent<Renderer>();
            if (r != null) ancho = r.bounds.center - hoja.position;
        }
        ancho.y = 0f;
        if (ancho.sqrMagnitude < 1e-6f) ancho = Vector3.right;
        Vector3 n = Vector3.Cross(Vector3.up, ancho.normalized);
        return invertirLado ? -n : n;
    }

    bool EnLadoQueAbre(Vector3 jugador, Vector3 centro)
    {
        Vector3 n = transform.TransformDirection(_normal);
        Vector3 d = jugador - centro; d.y = 0f;
        return Vector3.Dot(d, n) >= 0f;
    }

    static Vector3 EjeVertical(Transform t)
    {
        Vector3 up = t.InverseTransformDirection(Vector3.up);
        Vector3 a = new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z));
        if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(up.x), 0, 0);
        if (a.y >= a.z) return new Vector3(0, Mathf.Sign(up.y), 0);
        return new Vector3(0, 0, Mathf.Sign(up.z));
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) netCerrada.Value = cerrada;
        _angulo = Objetivo();   // quien entra tarde la ve ya en su sitio
        Girar();
    }

    float Objetivo() => netAbierta.Value ? anguloApertura * netSentido.Value : 0f;

    void Update()
    {
        // Animación, en todas las máquinas
        float obj = Objetivo();
        if (!Mathf.Approximately(_angulo, obj))
        {
            _angulo = Mathf.MoveTowards(_angulo, obj, velocidad * Time.deltaTime);
            Girar();
        }

        if (IsServer && IsSpawned)
            ComprobarJugadores();
    }

    void Girar()
    {
        if (hoja != null)
            hoja.localRotation = _cerradaA * Quaternion.AngleAxis(_angulo, _ejeA);
        if (esDoble && hojaDerecha != null)   // en espejo: las dos abren hacia el mismo lado
            hojaDerecha.localRotation = _cerradaB * Quaternion.AngleAxis(-_angulo, _ejeB);
    }

    // ---------- Servidor ----------

    void ComprobarJugadores()
    {
        Vector3 centro = transform.TransformPoint(_centro);
        bool alguienCerca = false;
        Vector3 quien = Vector3.zero;

        foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (c.PlayerObject == null) continue;
            Vector3 p = c.PlayerObject.transform.position;
            Vector3 d = p - centro; float dy = d.y; d.y = 0f;
            // Con la puerta abierta el radio es algo mayor: no se cierra en la cara
            // de quien está cruzando justo en el borde
            float r = netAbierta.Value ? radio + 0.6f : radio;
            bool cerca = d.magnitude <= r && Mathf.Abs(dy) < 2.5f;

            if (!cerca) { _cerca.Remove(c.ClientId); _proxAviso.Remove(c.ClientId); continue; }
            _cerca.Add(c.ClientId);

            if (netCerrada.Value && seAbreDesdeElOtroLado)
            {
                if (!EnLadoQueAbre(p, centro))
                {
                    AvisarCadaPoco(c.ClientId, "Está cerrada por el otro lado.");
                    continue;
                }
                _proxAviso.Remove(c.ClientId);
                netCerrada.Value = false;       // abierta desde su lado: ya queda sin cerrar para siempre
            }
            else if (netCerrada.Value)
            {
                if (!Key.Tiene(c.ClientId, llave))
                {
                    AvisarCadaPoco(c.ClientId, "Está cerrada, hay una imagen de " + Key.Imagen(llave) + " en la cerradura.");
                    continue;
                }
                _proxAviso.Remove(c.ClientId);
                netCerrada.Value = false;       // desde ahora queda sin llave para siempre
                Avisar(c.ClientId, "Abres la puerta con la " + Key.Nombre(llave) + ".");
            }

            if (!alguienCerca) quien = p;
            alguienCerca = true;
        }

        if (alguienCerca)
        {
            _sinNadie = 0f;
            if (!netAbierta.Value)
            {
                if (hoja != null) netSentido.Value = SentidoLejosDe(quien);
                netAbierta.Value = true;
            }
        }
        else if (netAbierta.Value)
        {
            // Se cierra sola cuando ya no queda nadie junto a ella
            _sinNadie += Time.deltaTime;
            if (_sinNadie >= esperaCierre) netAbierta.Value = false;
        }
    }

    // Prueba a girar la hoja hacia los dos lados y elige el que aleja su canto libre del jugador
    sbyte SentidoLejosDe(Vector3 jugador)
    {
        var r = hoja.GetComponent<Renderer>();
        Vector3 canto = r != null ? r.bounds.center : hoja.position + hoja.right * 0.4f;
        Vector3 eje = hoja.TransformDirection(_ejeA);
        Vector3 desde = canto - hoja.position;
        Vector3 mas = hoja.position + Quaternion.AngleAxis(anguloApertura, eje) * desde;
        Vector3 menos = hoja.position + Quaternion.AngleAxis(-anguloApertura, eje) * desde;
        jugador.y = canto.y;
        return (sbyte)((mas - jugador).sqrMagnitude >= (menos - jugador).sqrMagnitude ? 1 : -1);
    }

    // Mientras sigas delante de la puerta, el aviso se mantiene en pantalla
    void AvisarCadaPoco(ulong clientId, string texto)
    {
        if (_proxAviso.TryGetValue(clientId, out float t) && Time.time < t) return;
        _proxAviso[clientId] = Time.time + 3f;
        Avisar(clientId, texto);
    }

    void Avisar(ulong clientId, string texto) => AvisarClientRpc(texto, new ClientRpcParams
    { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } });

    [ClientRpc]
    void AvisarClientRpc(string mensaje, ClientRpcParams p = default) => MensajePantalla.Mostrar(mensaje);

    void OnDrawGizmosSelected()
    {
        Gizmos.color = cerrada ? new Color(1f, 0.3f, 0.2f) : new Color(0.3f, 1f, 0.4f);
        Vector3 c = Application.isPlaying ? transform.TransformPoint(_centro) : CentroEditor();
        Gizmos.DrawWireSphere(c, radio);

        if (cerrada && seAbreDesdeElOtroLado)
        {
            if (!Application.isPlaying && hoja == null) BuscarHojas();
            Vector3 n = Application.isPlaying ? transform.TransformDirection(_normal) : NormalMundo();
            c.y += 0.2f;
            // Verde: se abre desde aquí. Rojo: "Está cerrada por el otro lado".
            Gizmos.color = Color.green;
            Vector3 punta = c + n * 1.2f;
            Gizmos.DrawLine(c, punta);
            Gizmos.DrawLine(punta, punta - n * 0.3f + Vector3.up * 0.2f);
            Gizmos.DrawLine(punta, punta - n * 0.3f - Vector3.up * 0.2f);
            Gizmos.DrawSphere(punta, 0.08f);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(c, c - n * 1.2f);
            Gizmos.DrawWireCube(c - n * 1.2f, Vector3.one * 0.2f);
        }
    }

    Vector3 CentroEditor()
    {
        var rs = GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return transform.position;
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b.center;
    }
}
