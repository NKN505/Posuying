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
//
// Luces de señal: si el muro trae lentes con materiales "Luz_Roja" / "Luz_Verde"
// (el portón de señales), se encienden las ROJAS mientras está cerrado y las
// VERDES en cuanto se mete la tarjeta y se abre. Se buscan solas junto a las
// hojas; si no las encuentra, arrastra el muro a "Luces Renderers".
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

    [Header("Luces de señal")]
    [Tooltip("Renderers con las lentes (materiales Luz_Roja / Luz_Verde). Vacío = buscarlos solos junto a las hojas.")]
    public Renderer[] lucesRenderers;
    [Tooltip("Distancia máxima al centro de la puerta para la búsqueda automática.")]
    public float radioBusquedaLuces = 20f;
    public Color colorRojo = new Color(1f, 0.06f, 0.03f);
    public Color colorVerde = new Color(0.1f, 1f, 0.2f);
    [Tooltip("Intensidad de la emisión de la lente encendida.")]
    public float intensidadLuz = 6f;
    [Tooltip("Cuánto color le queda a la lente apagada (0 = negra).")]
    [Range(0f, 1f)] public float brilloApagada = 0.12f;

    private readonly NetworkVariable<bool> netAbierta = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Datos de cada hoja, tomados con la puerta cerrada
    class Hoja { public Transform t; public Vector3 pos0; public Quaternion rot0; public Vector3 bisagra; public float signo; public Vector3 desliz; }
    private readonly List<Hoja> _hojas = new List<Hoja>();
    private Vector3 _centro, _ancho, _normal;   // mundo
    private float _mitadAncho;
    private float _t;                          // 0 cerrada, 1 abierta
    private readonly Dictionary<ulong, float> _proxAviso = new Dictionary<ulong, float>();

    // Luces: por cada renderer, qué huecos de material son rojos y cuáles verdes
    class Luz { public Renderer r; public Material[] mats; public List<int> rojas = new List<int>(); public List<int> verdes = new List<int>(); }
    private readonly List<Luz> _luces = new List<Luz>();
    private Material _rojaOn, _rojaOff, _verdeOn, _verdeOff;
    private int _estadoLuces = -1;             // -1 sin pintar, 0 rojo, 1 verde

    void Awake()
    {
        Calcular(true);
        PrepararLuces();
        PintarLuces(false);
    }

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
        PintarLuces(netAbierta.Value);
    }

    void Update()
    {
        PintarLuces(netAbierta.Value);
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

    // ---------- Luces de señal ----------

    void PrepararLuces()
    {
        _luces.Clear();
        var candidatos = new List<Renderer>();
        if (lucesRenderers != null && lucesRenderers.Length > 0)
        {
            foreach (var r in lucesRenderers) if (r != null) candidatos.Add(r);
        }
        else
        {
            // Todas las mallas de la escena con lentes de verdad, y cada una para el portón más cercano
            var portones = FindObjectsByType<PuertaTarjeta>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != gameObject.scene || !TieneLentes(r)) continue;
                // Distancia al BORDE de la malla, no a su centro: el muro del 1er tercio
                // ("Muro de contención") mide ~137 m y su centro queda a ~55 m del portón.
                Bounds b = r.bounds;
                float miDist = DistanciaPlana(b, _centro);
                if (miDist > radioBusquedaLuces) continue;
                float miDistCentro = DistanciaPlana(b.center, _centro);
                bool masCercaDeOtro = false;
                foreach (var otro in portones)
                {
                    if (otro == this || otro.hojas == null) continue;
                    // Sin tocar el estado del otro portón (no llamar a su Calcular)
                    if (!CentroHojas(otro.hojas, out Vector3 c2)) continue;
                    float d2 = DistanciaPlana(b, c2);
                    if (d2 < miDist || (Mathf.Approximately(d2, miDist) && DistanciaPlana(b.center, c2) < miDistCentro))
                    { masCercaDeOtro = true; break; }
                }
                if (!masCercaDeOtro) candidatos.Add(r);
            }
        }

        Material baseRoja = null, baseVerde = null;
        int nRojas = 0, nVerdes = 0;
        var nombres = new List<string>();
        foreach (var r in candidatos)
        {
            var mats = r.sharedMaterials;
            var luz = new Luz { r = r, mats = mats };
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string n = mats[i].name;
                if (n.Contains("Luz_Roja")) { luz.rojas.Add(i); if (baseRoja == null) baseRoja = mats[i]; }
                else if (n.Contains("Luz_Verde")) { luz.verdes.Add(i); if (baseVerde == null) baseVerde = mats[i]; }
            }
            if (luz.rojas.Count + luz.verdes.Count == 0) continue;
            _luces.Add(luz);
            nRojas += luz.rojas.Count; nVerdes += luz.verdes.Count;
            nombres.Add(r.name);
        }

        if (_luces.Count == 0)
        {
            Debug.LogWarning($"[Portón] {name}: no encuentro ninguna malla con materiales Luz_Roja / Luz_Verde a menos de {radioBusquedaLuces} m " +
                             $"(centro {_centro}). Arrastra el muro con las lentes a 'Luces Renderers'.", this);
            return;
        }
        Debug.Log($"[Portón] {name}: luces en {string.Join(", ", nombres)} ({nRojas} huecos rojos, {nVerdes} verdes, " +
                  $"shader {(baseRoja ?? baseVerde).shader.name}).", this);

        _rojaOn = CrearMaterial(baseRoja ?? baseVerde, colorRojo, true);
        _rojaOff = CrearMaterial(baseRoja ?? baseVerde, colorRojo, false);
        _verdeOn = CrearMaterial(baseVerde ?? baseRoja, colorVerde, true);
        _verdeOff = CrearMaterial(baseVerde ?? baseRoja, colorVerde, false);
    }

    // Distancia en horizontal (sin altura) desde un punto al borde de una caja; 0 si está dentro
    static float DistanciaPlana(Bounds b, Vector3 p)
    {
        Vector3 d = b.ClosestPoint(p) - p; d.y = 0f;
        return d.magnitude;
    }

    static float DistanciaPlana(Vector3 a, Vector3 p)
    {
        Vector3 d = a - p; d.y = 0f;
        return d.magnitude;
    }

    // Centro de unas hojas cerradas, calculado igual que en Calcular() pero sin guardar nada
    static bool CentroHojas(Transform[] hs, out Vector3 centro)
    {
        centro = Vector3.zero;
        int n = 0;
        foreach (var h in hs)
        {
            if (h == null) continue;
            var mf = h.GetComponent<MeshFilter>();
            Bounds lb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            for (int i = 0; i < 8; i++)
            {
                centro += h.TransformPoint(lb.center + Vector3.Scale(lb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                n++;
            }
        }
        if (n == 0) return false;
        centro /= n;
        return true;
    }

    // ¿Tiene este renderer algún hueco Luz_Roja / Luz_Verde con polígonos? (las hojas los traen vacíos)
    static bool TieneLentes(Renderer r)
    {
        var mats = r.sharedMaterials;
        var mf = r.GetComponent<MeshFilter>();
        Mesh mesh = mf != null ? mf.sharedMesh : null;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            string n = mats[i].name;
            if (!n.Contains("Luz_Roja") && !n.Contains("Luz_Verde")) continue;
            // Con static batching la malla es la combinada y los índices no cuadran: se da por buena
            if (mesh == null || r.isPartOfStaticBatch || i >= mesh.subMeshCount) return true;
            if (mesh.GetSubMesh(i).indexCount > 0) return true;
        }
        return false;
    }

    Material CrearMaterial(Material origen, Color color, bool encendida)
    {
        var m = new Material(origen) { name = origen.name + (encendida ? " (On)" : " (Off)") };
        Color c = encendida ? color : color * brilloApagada;
        c.a = 1f;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_EmissionColor"))
        {
            if (encendida)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * intensidadLuz);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
        }
        return m;
    }

    // Cerrada: rojas encendidas, verdes apagadas. Abierta: al revés.
    void PintarLuces(bool abierta)
    {
        int estado = abierta ? 1 : 0;
        if (estado == _estadoLuces || _luces.Count == 0) return;
        _estadoLuces = estado;
        foreach (var l in _luces)
        {
            if (l.r == null) continue;
            var mats = (Material[])l.mats.Clone();
            foreach (int i in l.rojas) mats[i] = abierta ? _rojaOff : _rojaOn;
            foreach (int i in l.verdes) mats[i] = abierta ? _verdeOn : _verdeOff;
            l.r.sharedMaterials = mats;   // solo cambia este renderer, no el material del proyecto
        }
    }

    public override void OnDestroy()
    {
        foreach (var m in new[] { _rojaOn, _rojaOff, _verdeOn, _verdeOff }) if (m != null) Destroy(m);
        base.OnDestroy();
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
