using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Puerta o portón que se abre con la tarjeta de acceso, SOLO desde un lado,
// y que una vez abierta se queda abierta para siempre (para todos).
//
// Cómo montarlo:
//   1. Crea un objeto vacío (por ejemplo "Porton_Tarjeta") en cualquier sitio.
//   2. Añádele este script (el NetworkObject se pone solo).
//   3. Arrastra a "Hojas" las piezas que se mueven (por ejemplo P_dr.001 y P_piz.001).
//   4. Selecciónalo: la flecha VERDE marca el lado desde el que se abre.
//      Si es al revés, marca "Invertir Lado".
//
// No hace falta que el origen de las hojas esté en la bisagra: el giro se
// calcula sobre el extremo exterior de cada hoja.
[RequireComponent(typeof(NetworkObject))]
public class PuertaTarjeta : NetworkBehaviour
{
    public enum Modo { Girar, Deslizar }

    [Tooltip("Qué tarjeta lo abre. Cada color abre solo sus portones.")]
    public ColorTarjeta tarjeta = ColorTarjeta.Azul;

    [Tooltip("Las piezas que se mueven al abrir (una o dos).")]
    public Transform[] hojas;

    [Tooltip("Cambia el lado desde el que se abre (la flecha verde).")]
    public bool invertirLado = false;

    [Tooltip("Girar: como una puerta, sobre el extremo exterior de cada hoja. Deslizar: cada hoja se aparta hacia su lado.")]
    public Modo modo = Modo.Girar;

    [Header("Avanzado")]
    [Range(30f, 180f)] public float anguloApertura = 95f;
    [Tooltip("Solo con una hoja: pone la bisagra en el otro extremo.")]
    public bool bisagraAlOtroExtremo = false;
    [Tooltip("Segundos que tarda en abrirse.")]
    public float duracion = 1.6f;
    [Tooltip("Distancia a la puerta (por delante o por detrás) a la que el jugador 'llega'.")]
    public float radio = 1.6f;

    private readonly NetworkVariable<bool> netAbierta = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Datos de cada hoja, tomados con la puerta cerrada
    class Hoja { public Transform t; public Vector3 pos0; public Quaternion rot0; public Vector3 bisagra; public float signo; public Vector3 desliz; }
    private readonly List<Hoja> _hojas = new List<Hoja>();
    private Vector3 _centro, _ancho, _normal;   // mundo
    private float _mitadAncho;
    private float _t;                          // 0 cerrada, 1 abierta
    private readonly Dictionary<ulong, float> _proxAviso = new Dictionary<ulong, float>();

    void Awake() => Calcular(true);

    // Geometría de la puerta cerrada: centro, eje a lo ancho, normal (lado que abre)
    void Calcular(bool guardar)
    {
        _hojas.Clear();
        if (hojas == null || hojas.Length == 0) return;

        var esquinas = new List<Vector3>();
        Vector3 ejeAncho = Vector3.zero;
        foreach (var h in hojas)
        {
            if (h == null) continue;
            var mf = h.GetComponent<MeshFilter>();
            Bounds lb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            for (int i = 0; i < 8; i++)
                esquinas.Add(h.TransformPoint(lb.center + Vector3.Scale(lb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            if (ejeAncho == Vector3.zero)
            {
                // El eje local más largo en horizontal es el ancho de la hoja
                float mejor = -1f;
                foreach (var ax in new[] { Vector3.right, Vector3.up, Vector3.forward })
                {
                    Vector3 w = h.TransformVector(Vector3.Scale(ax, lb.size)); w.y = 0f;
                    if (w.magnitude > mejor) { mejor = w.magnitude; ejeAncho = w.normalized; }
                }
            }
        }
        if (esquinas.Count == 0) return;

        Vector3 c = Vector3.zero;
        foreach (var e in esquinas) c += e;
        c /= esquinas.Count;
        _centro = c;
        _ancho = ejeAncho;
        _normal = Vector3.Cross(Vector3.up, _ancho);
        if (invertirLado) _normal = -_normal;

        float min = float.MaxValue, max = float.MinValue;
        foreach (var e in esquinas) { float a = Vector3.Dot(e - c, _ancho); min = Mathf.Min(min, a); max = Mathf.Max(max, a); }
        _mitadAncho = (max - min) * 0.5f;

        if (!guardar) return;
        int n = 0;
        foreach (var h in hojas) if (h != null) n++;
        foreach (var h in hojas)
        {
            if (h == null) continue;
            // Extremos de esta hoja a lo ancho
            var mf = h.GetComponent<MeshFilter>();
            Bounds lb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            float hmin = float.MaxValue, hmax = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = h.TransformPoint(lb.center + Vector3.Scale(lb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                float a = Vector3.Dot(p - c, _ancho);
                hmin = Mathf.Min(hmin, a); hmax = Mathf.Max(hmax, a);
            }
            float medio = (hmin + hmax) * 0.5f;
            // Bisagra: el extremo más alejado del centro del hueco
            bool haciaMax = n > 1 ? medio >= 0f : !bisagraAlOtroExtremo;
            float aBis = haciaMax ? hmax : hmin;
            Vector3 centroHoja = h.TransformPoint(lb.center);
            Vector3 bisagra = c + _ancho * aBis; bisagra.y = centroHoja.y;
            Vector3 radioH = c + _ancho * (haciaMax ? hmin : hmax) - (c + _ancho * aBis);
            // Gira hacia el lado contrario al que abre (se empuja)
            float signo = Vector3.Dot(Vector3.Cross(Vector3.up, radioH), -_normal) >= 0f ? 1f : -1f;
            _hojas.Add(new Hoja
            {
                t = h, pos0 = h.position, rot0 = h.rotation, bisagra = bisagra, signo = signo,
                desliz = _ancho * (haciaMax ? 1f : -1f) * (hmax - hmin) * 0.95f,
            });
        }
    }

    public override void OnNetworkSpawn()
    {
        if (netAbierta.Value) { _t = 1f; Mover(); }   // quien entra tarde la ve ya abierta
    }

    void Update()
    {
        if (netAbierta.Value && _t < 1f)
        {
            _t = Mathf.MoveTowards(_t, 1f, Time.deltaTime / Mathf.Max(0.05f, duracion));
            Mover();
        }
        if (IsServer && IsSpawned && !netAbierta.Value) ComprobarJugadores();
    }

    void Mover()
    {
        float k = Mathf.SmoothStep(0f, 1f, _t);
        foreach (var h in _hojas)
        {
            if (modo == Modo.Deslizar)
            {
                h.t.position = h.pos0 + h.desliz * k;
                continue;
            }
            Quaternion q = Quaternion.AngleAxis(anguloApertura * h.signo * k, Vector3.up);
            h.t.position = h.bisagra + q * (h.pos0 - h.bisagra);
            h.t.rotation = q * h.rot0;
        }
    }

    // ---------- Servidor ----------

    void ComprobarJugadores()
    {
        foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (c.PlayerObject == null) continue;
            Vector3 d = c.PlayerObject.transform.position - _centro;
            if (Mathf.Abs(d.y) > 3f) { _proxAviso.Remove(c.ClientId); continue; }
            float aLoAncho = Vector3.Dot(d, _ancho);
            float delante = Vector3.Dot(d, _normal);
            bool cerca = Mathf.Abs(delante) <= radio && Mathf.Abs(aLoAncho) <= _mitadAncho + 0.5f;
            if (!cerca) { _proxAviso.Remove(c.ClientId); continue; }

            if (delante < 0f) { Avisar(c.ClientId, "No se puede abrir desde este lado."); continue; }
            if (!Tarjeta.Tiene(c.ClientId, tarjeta)) { Avisar(c.ClientId, "Está cerrada. Necesitas la " + Tarjeta.Nombre(tarjeta) + "."); continue; }

            netAbierta.Value = true;   // y ya no se cierra nunca
            _proxAviso.Remove(c.ClientId);
            MensajeClientRpc("Abres la puerta con la " + Tarjeta.Nombre(tarjeta) + ".", Para(c.ClientId));
            return;
        }
    }

    // Mientras sigas delante, el aviso se mantiene en pantalla
    void Avisar(ulong id, string texto)
    {
        if (_proxAviso.TryGetValue(id, out float t) && Time.time < t) return;
        _proxAviso[id] = Time.time + 3f;
        MensajeClientRpc(texto, Para(id));
    }

    static ClientRpcParams Para(ulong id) => new ClientRpcParams
    { Send = new ClientRpcSendParams { TargetClientIds = new[] { id } } };

    [ClientRpc]
    void MensajeClientRpc(string mensaje, ClientRpcParams p = default) => MensajePantalla.Mostrar(mensaje);

    /// <summary>Centro de la puerta cerrada y dirección hacia el lado que abre (para herramientas de editor).</summary>
    public void Geometria(out Vector3 centro, out Vector3 ladoQueAbre)
    {
        Calcular(false);
        centro = _centro; ladoQueAbre = _normal;
    }

    // ---------- Editor ----------

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) Calcular(false);
        if (hojas == null || hojas.Length == 0) return;

        Vector3 c = _centro;
        // Zona de detección
        Gizmos.color = Tarjeta.ColorDe(tarjeta);
        Matrix4x4 m = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(c, Quaternion.LookRotation(_normal, Vector3.up), Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3((_mitadAncho + 0.5f) * 2f, 2f, radio * 2f));
        Gizmos.matrix = m;

        // Verde: desde aquí se abre. Rojo: "No se puede abrir desde este lado".
        Gizmos.color = Color.green;
        Vector3 punta = c + _normal * 1.5f;
        Gizmos.DrawLine(c, punta);
        Gizmos.DrawLine(punta, punta - _normal * 0.35f + Vector3.up * 0.25f);
        Gizmos.DrawLine(punta, punta - _normal * 0.35f - Vector3.up * 0.25f);
        Gizmos.DrawSphere(punta, 0.1f);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(c, c - _normal * 1.5f);
        Gizmos.DrawWireCube(c - _normal * 1.5f, Vector3.one * 0.25f);
    }
}
