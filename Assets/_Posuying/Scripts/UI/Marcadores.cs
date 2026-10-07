using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Marcadores ("ping"): con una tecla (Q por defecto, se cambia en Opciones >
// Controles) senalas lo que tienes en el centro de la pantalla y todo el equipo
// ve la marca, con tu nombre y a que distancia esta, aunque quede fuera de vista.
//
//   - Sobre un enemigo: marca roja que le sigue mientras viva.
//   - Sobre un objeto (municion, botiquin, mochila, la bomba): marca verde.
//   - En cualquier otro sitio: marca amarilla en ese punto.
//
// Cada jugador tiene una sola marca: la nueva sustituye a la anterior.
//
// Va en el mismo objeto que MatchManager (tiene NetworkObject).
public class Marcadores : NetworkBehaviour
{
    public static Marcadores Instance { get; private set; }

    public enum Tipo { Punto = 0, Enemigo = 1, Objeto = 2 }

    [Tooltip("Hasta que distancia se puede marcar")]
    public float alcance = 200f;
    [Tooltip("Segundos que dura una marca")]
    public float duracion = 8f;
    [Tooltip("Tope de marcas por jugador cada 5 segundos, contra el spam")]
    public int maxCada5s = 4;

    private class Marca
    {
        public Vector3 punto;
        public Transform sigue;     // el enemigo marcado, si lo hay
        public Tipo tipo;
        public string nombre;
        public float nace;
    }

    private readonly Dictionary<ulong, Marca> _marcas = new Dictionary<ulong, Marca>();
    private readonly Dictionary<ulong, Queue<float>> _recientes = new Dictionary<ulong, Queue<float>>();

    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkDespawn()
    {
        _marcas.Clear();
        _recientes.Clear();
    }

    void Update()
    {
        if (!IsSpawned || UIState.BlocksGameplay || MainMenuUI.CapturingKey) return;
        if (Controles.Pulsado(Accion.Marcar)) Marcar();
    }

    // ---------- Marcar (jugador local) ----------

    private void Marcar()
    {
        var yo = NetworkPlayer.LocalPlayer;
        Camera camara = yo != null ? yo.GetComponentInChildren<Camera>() : null;
        if (camara == null || !camara.enabled) camara = Camera.main;   // espectador
        if (camara == null) return;

        Ray rayo = new Ray(camara.transform.position, camara.transform.forward);
        var golpes = Physics.RaycastAll(rayo, alcance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        // El primer impacto que no sea yo mismo
        bool hay = false;
        RaycastHit mejor = default;
        foreach (var g in golpes)
        {
            if (yo != null && g.collider.transform.IsChildOf(yo.transform)) continue;
            if (!hay || g.distance < mejor.distance) { mejor = g; hay = true; }
        }
        if (!hay) return;

        Tipo tipo = Tipo.Punto;
        Vector3 punto = mejor.point;
        ulong red = 0;

        var enemigo = mejor.collider.GetComponentInParent<EnemyBehaviour>();
        if (enemigo != null && !enemigo.IsDead)
        {
            tipo = Tipo.Enemigo;
            red = enemigo.NetworkObjectId;
            punto = enemigo.transform.position;
        }
        else if (ObjetoCerca(mejor.point, out Vector3 delObjeto))
        {
            // Los objetos del suelo no paran rayos (capa Ignore Raycast): se
            // reconocen por estar pegados al punto senalado
            tipo = Tipo.Objeto;
            punto = delObjeto;
        }

        MarcarServerRpc(punto, (int)tipo, red);
    }

    private static bool ObjetoCerca(Vector3 punto, out Vector3 posicion)
    {
        posicion = punto;
        float mejor = 2f * 2f;
        bool hay = false;

        foreach (var o in FindObjectsByType<ObjetoSoltado>(FindObjectsSortMode.None))
        {
            float d = (o.transform.position - punto).sqrMagnitude;
            if (d < mejor) { mejor = d; posicion = o.transform.position; hay = true; }
        }

        var mision = MisionBomba.Instance;
        if (mision != null && mision.IsSpawned && mision.Fase == FaseBomba.BuscarBomba)
            foreach (var p in PuntoBomba.Todos)
            {
                if (p == null || p.tipo != PuntoBomba.Tipo.Recoger) continue;
                float d = (p.transform.position - punto).sqrMagnitude;
                if (d < mejor) { mejor = d; posicion = p.transform.position; hay = true; }
            }
        return hay;
    }

    // ---------- Red ----------

    [ServerRpc(RequireOwnership = false)]
    private void MarcarServerRpc(Vector3 punto, int tipo, ulong objetoRed, ServerRpcParams rpc = default)
    {
        ulong quien = rpc.Receive.SenderClientId;

        if (!_recientes.TryGetValue(quien, out var cola)) _recientes[quien] = cola = new Queue<float>();
        while (cola.Count > 0 && Time.time - cola.Peek() > 5f) cola.Dequeue();
        if (cola.Count >= maxCada5s) return;
        cola.Enqueue(Time.time);

        string nombre = "Jugador";
        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null || player.OwnerClientId != quien) continue;
            var n = player.GetComponent<PlayerName>();
            if (n != null) { nombre = n.Name; break; }
        }

        MostrarClientRpc(quien, nombre, punto, Mathf.Clamp(tipo, 0, 2), objetoRed);
    }

    [ClientRpc]
    private void MostrarClientRpc(ulong quien, string nombre, Vector3 punto, int tipo, ulong objetoRed)
    {
        var marca = new Marca { punto = punto, tipo = (Tipo)tipo, nombre = nombre, nace = Time.time };

        if (marca.tipo == Tipo.Enemigo && objetoRed != 0 &&
            NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(objetoRed, out var objeto) && objeto != null)
            marca.sigue = objeto.transform;

        _marcas[quien] = marca;   // una por jugador: la nueva pisa a la anterior
    }

    // ---------- Dibujo ----------

    private readonly List<ulong> _caducadas = new List<ulong>();

    void OnGUI()
    {
        if (!IsSpawned || _marcas.Count == 0 || Event.current.type != EventType.Repaint) return;
        if (MatchManager.Instance != null && MatchManager.Instance.MatchOver) return;

        var yo = NetworkPlayer.LocalPlayer;
        Camera camara = yo != null ? yo.GetComponentInChildren<Camera>() : null;
        if (camara == null || !camara.enabled) camara = Camera.main;
        if (camara == null) return;

        var estilo = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter, richText = true, fontSize = Mathf.RoundToInt(Screen.height / 62f)
        };
        float margen = Screen.height * 0.07f;
        float rombo = Screen.height * 0.016f;

        _caducadas.Clear();
        foreach (var par in _marcas)
        {
            Marca m = par.Value;
            float edad = Time.time - m.nace;

            // El enemigo marcado ha muerto o ha desaparecido: la marca se va con el
            bool perdido = m.tipo == Tipo.Enemigo && (m.sigue == null || EstaMuerto(m.sigue));
            if (edad >= duracion || perdido) { _caducadas.Add(par.Key); continue; }

            Vector3 mundo = m.sigue != null ? m.sigue.position + Vector3.up * 1.2f : m.punto + Vector3.up * 0.3f;
            Vector3 p = camara.WorldToScreenPoint(mundo);
            float x = p.x, y = Screen.height - p.y;

            // Detras de la camara o fuera de la pantalla: se pega al borde, hacia donde esta
            bool detras = p.z < 0f;
            if (detras) { x = Screen.width - x; y = Screen.height - y; }
            bool fuera = detras || x < margen || x > Screen.width - margen || y < margen || y > Screen.height - margen;
            if (fuera)
            {
                Vector2 centro = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                Vector2 dir = new Vector2(x, y) - centro;
                if (dir.sqrMagnitude < 1f) dir = Vector2.up;
                if (detras) dir = dir.normalized * Screen.width;   // lejos: que el recorte lo lleve al borde
                float escala = Mathf.Min((centro.x - margen) / Mathf.Max(1f, Mathf.Abs(dir.x)),
                                         (centro.y - margen) / Mathf.Max(1f, Mathf.Abs(dir.y)));
                if (escala < 1f || detras) dir *= escala;
                x = centro.x + dir.x; y = centro.y + dir.y;
            }

            Color color = m.tipo == Tipo.Enemigo ? new Color(1f, 0.25f, 0.2f)
                        : m.tipo == Tipo.Objeto ? new Color(0.4f, 1f, 0.45f)
                        : new Color(1f, 0.85f, 0.2f);
            // Aparece con un pequeno "pop" y se desvanece el ultimo segundo
            float alfa = Mathf.Clamp01(duracion - edad);
            float tam = rombo * (1f + Mathf.Clamp01(1f - edad * 4f) * 0.8f);

            Matrix4x4 antesM = GUI.matrix;
            Color antes = GUI.color;
            GUIUtility.RotateAroundPivot(45f, new Vector2(x, y));
            GUI.color = new Color(0f, 0f, 0f, alfa * 0.8f);
            GUI.DrawTexture(new Rect(x - tam * 0.5f - 2f, y - tam * 0.5f - 2f, tam + 4f, tam + 4f), Texture2D.whiteTexture);
            GUI.color = new Color(color.r, color.g, color.b, alfa);
            GUI.DrawTexture(new Rect(x - tam * 0.5f, y - tam * 0.5f, tam, tam), Texture2D.whiteTexture);
            GUI.matrix = antesM;

            int metros = Mathf.RoundToInt(Vector3.Distance(camara.transform.position, mundo));
            string texto = m.nombre + (m.tipo == Tipo.Enemigo ? " · ENEMIGO" : m.tipo == Tipo.Objeto ? " · OBJETO" : "") +
                           "\n" + metros + " m";
            Rect zona = new Rect(x - 130f, y + rombo, 260f, 60f);
            GUI.color = new Color(0f, 0f, 0f, alfa * 0.85f);
            GUI.Label(new Rect(zona.x + 1.5f, zona.y + 1.5f, zona.width, zona.height), texto, estilo);
            GUI.color = new Color(color.r, color.g, color.b, alfa);
            GUI.Label(zona, texto, estilo);
            GUI.color = antes;
        }

        foreach (var clave in _caducadas) _marcas.Remove(clave);
    }

    private static bool EstaMuerto(Transform t)
    {
        var enemigo = t.GetComponent<EnemyBehaviour>();
        return enemigo != null && enemigo.IsDead;
    }
}
