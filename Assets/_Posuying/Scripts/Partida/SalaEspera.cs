using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Sala de espera antes de la partida, al estilo de Fortnite: al crear o unirse
// no se aparece directamente en el mapa, sino en una sala donde se ve a todos los
// jugadores de pie, uno al lado de otro. Cada uno marca LISTO y el anfitrion
// empieza la partida cuando lo estan todos.
//
// Ocurre en la MISMA escena del mapa (no hay carga de escena en red): los
// jugadores se colocan en fila en un punto, quietos, y se miran con una camara
// fija. Mientras tanto no hay enemigos ni mision.
//
// QUIEN LLEGA CON LA PARTIDA EMPEZADA no entra por las buenas: se queda en
// espera y al anfitrion le sale la solicitud para aceptarla o rechazarla.
//
// Autoridad del servidor. Va en el mismo objeto que MatchManager (tiene
// NetworkObject). La parte visual (botones) la pinta MainMenuUI.
public class SalaEspera : NetworkBehaviour
{
    public static SalaEspera Instance { get; private set; }

    public enum Fase { Sala = 0, CuentaAtras = 1, EnJuego = 2 }

    [Header("Donde se colocan los jugadores")]
    [Tooltip("Centro de la fila; su 'adelante' (flecha azul) es hacia donde miran, que es " +
             "donde se pone la camara. Vacio = se elige solo, junto a los puntos de aparicion.")]
    public Transform punto;
    [Tooltip("Separacion entre jugadores en la fila (metros)")]
    public float separacion = 1.15f;

    [Header("Camara")]
    public float distanciaCamara = 5.4f;
    public float alturaCamara = 1.35f;
    public float campoDeVision = 38f;
    [Tooltip("A que altura del jugador apunta la camara. Mas alto = los personajes quedan mas abajo en pantalla")]
    public float alturaMirada = 1.45f;

    [Header("Luz de la sala")]
    [Tooltip("Foco que ilumina a los jugadores de frente. El mapa es de noche y, sin el, " +
             "se veian como siluetas negras a contraluz.")]
    public float intensidadLuz = 9f;
    public Color colorLuz = new Color(1f, 0.93f, 0.82f);

    [Header("Empezar")]
    [Tooltip("Segundos de cuenta atras desde que el anfitrion pulsa EMPEZAR")]
    public float segundosCuentaAtras = 3f;

    [Header("Solicitudes con la partida empezada")]
    public KeyCode teclaAceptar = KeyCode.F5;
    public KeyCode teclaRechazar = KeyCode.F6;
    [Tooltip("Segundos que una solicitud espera respuesta antes de rechazarse sola")]
    public float segundosSolicitud = 45f;

    private readonly NetworkVariable<int> netFase = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<double> netEmpieza = new NetworkVariable<double>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // Ids de cliente de quienes han marcado LISTO
    private readonly NetworkList<ulong> netListos = new NetworkList<ulong>();

    public Fase FaseActual => (Fase)netFase.Value;

    /// <summary>True si la partida esta en marcha. Sin sala en la escena, siempre.</summary>
    public static bool EnJuego => Instance == null || !Instance.IsSpawned || Instance.FaseActual == Fase.EnJuego;

    /// <summary>True mientras esta maquina debe ver la sala (o la pantalla de espera) en vez del juego.</summary>
    public bool Visible => IsSpawned && (FaseActual != Fase.EnJuego || EsperandoPermiso);

    /// <summary>Esta maquina ha llegado con la partida empezada y espera a que el anfitrion decida.</summary>
    public bool EsperandoPermiso { get; private set; }

    public float SegundosParaEmpezar =>
        FaseActual == Fase.CuentaAtras ? Mathf.Max(0f, (float)(netEmpieza.Value - NetworkManager.ServerTime.Time)) : 0f;

    // ---------- Solo servidor: solicitudes de entrada ----------
    private class Solicitud
    {
        public ulong cliente;
        public string nombre;
        public float llego;
        public bool avisado;
    }

    private readonly List<Solicitud> _solicitudes = new List<Solicitud>();
    private float _graciaMigracionHasta = -1f;

    private Camera _camara;
    private Light _luz;
    private Vector3 _centro, _adelante;
    private bool _sitioCalculado;
    private bool _estabaEnSala;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        ConfigurarRed();
    }

    // La aprobacion de conexiones tiene que estar puesta ANTES de arrancar la red,
    // y con el mismo valor en todas las maquinas (forma parte de la configuracion
    // que se compara al conectar).
    private void ConfigurarRed()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsListening) return;

        nm.NetworkConfig.ConnectionApproval = true;
        nm.ConnectionApprovalCallback = AprobarConexion;
        // El nombre viaja con la peticion de conexion: asi el anfitrion sabe quien pide entrar
        nm.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(PlayerProfile.Name ?? "");
    }

    private void AprobarConexion(NetworkManager.ConnectionApprovalRequest peticion,
                                 NetworkManager.ConnectionApprovalResponse respuesta)
    {
        respuesta.Approved = true;
        respuesta.Pending = false;
        respuesta.CreatePlayerObject = true;

        bool esElAnfitrion = peticion.ClientNetworkId == NetworkManager.ServerClientId;
        if (esElAnfitrion || !IsSpawned || FaseActual != Fase.EnJuego) return;

        // Tras un cambio de anfitrion todos vuelven a entrar: no son gente nueva
        if (Time.unscaledTime < _graciaMigracionHasta) return;

        // Partida empezada: se conecta, pero SIN personaje hasta que el anfitrion decida
        respuesta.CreatePlayerObject = false;

        string nombre = "";
        try { nombre = System.Text.Encoding.UTF8.GetString(peticion.Payload ?? new byte[0]); }
        catch (System.Exception) { }
        if (string.IsNullOrWhiteSpace(nombre)) nombre = "Un jugador";
        if (nombre.Length > 24) nombre = nombre.Substring(0, 24);

        _solicitudes.Add(new Solicitud { cliente = peticion.ClientNetworkId, nombre = nombre, llego = Time.unscaledTime });
    }

    public override void OnNetworkSpawn()
    {
        netFase.OnValueChanged += AlCambiarFase;

        if (IsServer)
        {
            netListos.Clear();
            NetworkManager.OnClientDisconnectCallback += AlDesconectarse;

            // Si somos el anfitrion NUEVO tras una migracion, la partida ya estaba en marcha
            var sesion = FindFirstObjectByType<OnlineSession>();
            bool migrando = sesion != null && sesion.IsMigrating;
            netFase.Value = (int)(migrando ? Fase.EnJuego : Fase.Sala);
            if (migrando) _graciaMigracionHasta = Time.unscaledTime + 45f;
        }

        _estabaEnSala = FaseActual != Fase.EnJuego;
    }

    public override void OnNetworkDespawn()
    {
        netFase.OnValueChanged -= AlCambiarFase;
        if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= AlDesconectarse;

        _solicitudes.Clear();
        EsperandoPermiso = false;
        UIState.SalaAbierta = false;
        if (_camara != null) _camara.enabled = false;
        if (_luz != null) _luz.enabled = false;
        _sitioCalculado = false;
    }

    private void AlDesconectarse(ulong cliente)
    {
        if (!IsServer) return;
        netListos.Remove(cliente);
        _solicitudes.RemoveAll(s => s.cliente == cliente);
    }

    // ---------- Acciones de los jugadores ----------

    public bool EstaListo(ulong cliente) => cliente == NetworkManager.ServerClientId || netListos.Contains(cliente);
    public bool LocalListo => IsSpawned && EstaListo(NetworkManager.LocalClientId);

    /// <summary>Cuantos jugadores faltan por marcar LISTO (el anfitrion no cuenta: lo esta siempre).</summary>
    public int FaltanPorEstarListos()
    {
        int faltan = 0;
        foreach (var p in NetworkPlayer.AllPlayers)
            if (p != null && p.GetComponent<NetworkPlayer>() != null && !EstaListo(p.OwnerClientId)) faltan++;
        return faltan;
    }

    public int JugadoresEnSala()
    {
        int n = 0;
        foreach (var p in NetworkPlayer.AllPlayers)
            if (p != null && p.GetComponent<NetworkPlayer>() != null) n++;
        return n;
    }

    public void CambiarListo()
    {
        if (IsSpawned && FaseActual != Fase.EnJuego) ListoServerRpc(!LocalListo);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ListoServerRpc(bool listo, ServerRpcParams rpc = default)
    {
        if (FaseActual == Fase.EnJuego) return;
        ulong quien = rpc.Receive.SenderClientId;

        if (listo && !netListos.Contains(quien)) netListos.Add(quien);
        if (!listo) netListos.Remove(quien);

        // Si alguien deja de estar listo en plena cuenta atras, se cancela
        if (!listo && FaseActual == Fase.CuentaAtras) netFase.Value = (int)Fase.Sala;
    }

    /// <summary>El anfitrion pulsa EMPEZAR. Solo arranca si estan todos listos.</summary>
    public void Empezar()
    {
        if (IsServer && FaseActual == Fase.Sala && FaltanPorEstarListos() == 0)
        {
            netEmpieza.Value = NetworkManager.ServerTime.Time + segundosCuentaAtras;
            netFase.Value = (int)Fase.CuentaAtras;
        }
    }

    /// <summary>Entra en partida ya, sin esperar a nadie (pruebas y cambio de anfitrion).</summary>
    public void EmpezarAhora()
    {
        if (IsServer && FaseActual != Fase.EnJuego) netFase.Value = (int)Fase.EnJuego;
    }

    // ---------- Solicitudes (servidor) ----------

    public int SolicitudesPendientes => _solicitudes.Count;

    private void AtenderSolicitudes()
    {
        if (_solicitudes.Count == 0) return;

        // Avisar una vez al que espera (cuando ya esta conectado) y al anfitrion
        foreach (var s in _solicitudes)
        {
            if (s.avisado || !NetworkManager.ConnectedClients.ContainsKey(s.cliente)) continue;
            s.avisado = true;
            EnEsperaClientRpc(Solo(s.cliente));
            Notifications.Show(s.nombre + " quiere entrar en la partida");
        }

        Solicitud primera = _solicitudes[0];
        bool caducada = Time.unscaledTime - primera.llego > segundosSolicitud;

        // Con el chat abierto o un menu, las teclas son letras
        bool teclas = !UIState.ChatOpen && !MainMenuUI.CapturingKey;
        if (teclas && Input.GetKeyDown(teclaAceptar)) Resolver(primera, true);
        else if (caducada || (teclas && Input.GetKeyDown(teclaRechazar))) Resolver(primera, false);
    }

    private void Resolver(Solicitud s, bool aceptar)
    {
        _solicitudes.Remove(s);
        if (!NetworkManager.ConnectedClients.ContainsKey(s.cliente)) return;   // ya se habia ido

        if (aceptar)
        {
            // Ahora si: su personaje, directamente en la partida
            GameObject prefab = NetworkManager.NetworkConfig.PlayerPrefab;
            if (prefab == null) return;
            var jugador = Instantiate(prefab);
            jugador.GetComponent<NetworkObject>().SpawnAsPlayerObject(s.cliente, true);
            AdmitidoClientRpc(Solo(s.cliente));
            Notifications.Show(s.nombre + " entra en la partida");
        }
        else
        {
            RechazadoClientRpc(Solo(s.cliente));
            Notifications.Show("Has rechazado a " + s.nombre);
        }
    }

    private static ClientRpcParams Solo(ulong cliente) => new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { cliente } }
    };

    [ClientRpc]
    private void EnEsperaClientRpc(ClientRpcParams p = default)
    {
        EsperandoPermiso = true;
    }

    [ClientRpc]
    private void AdmitidoClientRpc(ClientRpcParams p = default)
    {
        EsperandoPermiso = false;
    }

    [ClientRpc]
    private void RechazadoClientRpc(ClientRpcParams p = default)
    {
        EsperandoPermiso = false;
        Notifications.Show("El anfitrion no te ha dejado entrar en la partida");
        NetworkUI.LeaveGame();   // sale por la sesion, como si pulsara "Salir de la partida"
    }

    // ---------- Cada frame ----------

    void Update()
    {
        if (!IsSpawned)
        {
            ConfigurarRed();   // por si el nombre del jugador ha cambiado en Opciones
            UIState.SalaAbierta = false;
            if (_camara != null && _camara.enabled) _camara.enabled = false;
            if (_luz != null && _luz.enabled) _luz.enabled = false;
            return;
        }

        if (IsServer)
        {
            if (FaseActual == Fase.CuentaAtras && NetworkManager.ServerTime.Time >= netEmpieza.Value)
                netFase.Value = (int)Fase.EnJuego;

            AtenderSolicitudes();
        }

        // Si ya tengo personaje es que me han dejado entrar
        if (EsperandoPermiso && NetworkPlayer.LocalPlayer != null) EsperandoPermiso = false;

        bool enSala = FaseActual != Fase.EnJuego;
        UIState.SalaAbierta = enSala || EsperandoPermiso;

        var yo = NetworkPlayer.LocalPlayer;
        bool camaraDeSala = (enSala && yo != null) || EsperandoPermiso;

        if (camaraDeSala) ColocarCamara();
        if (_camara != null && _camara.enabled != camaraDeSala) _camara.enabled = camaraDeSala;
        if (_luz != null && _luz.enabled != camaraDeSala) _luz.enabled = camaraDeSala;

        if (yo != null)
        {
            // Mi camara de primera persona se apaga mientras miro la sala
            Camera propia = yo.GetComponentInChildren<Camera>(true);
            if (propia != null && propia != _camara && propia.enabled == camaraDeSala) propia.enabled = !camaraDeSala;

            if (enSala) ColocarmeEnLaFila(yo);
        }

        _estabaEnSala = enSala;
    }

    // Empieza la partida: cada uno lleva a SU personaje a un punto de aparicion
    // (la posicion la manda el dueno) y recupera su camara.
    private void AlCambiarFase(int antes, int ahora)
    {
        if ((Fase)ahora != Fase.EnJuego || (Fase)antes == Fase.EnJuego) return;

        var yo = NetworkPlayer.LocalPlayer;
        if (yo == null) return;

        var red = yo.GetComponent<NetworkPlayer>();
        if (red != null) red.MoveToSpawnPoint();

        Notifications.Show("¡Empieza la partida!");
    }

    // ---------- La fila y la camara ----------

    // Donde va la sala: el punto indicado o, si no hay, junto a los puntos de
    // aparicion del 1er tercio, mirando hacia donde haya mas sitio libre (ahi
    // se pone la camara y no debe quedar dentro de una pared).
    private void CalcularSitio()
    {
        if (_sitioCalculado) return;
        _sitioCalculado = true;

        if (punto != null)
        {
            _centro = punto.position;
            _adelante = Vector3.ProjectOnPlane(punto.forward, Vector3.up).normalized;
            return;
        }

        Vector3 suma = Vector3.zero;
        int n = 0;
        foreach (var s in FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
            if (s.tercio == 1) { suma += s.transform.position; n++; }
        _centro = n > 0 ? suma / n : transform.position;

        float mejor = -1f;
        _adelante = Vector3.forward;
        for (int a = 0; a < 360; a += 15)
        {
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            float libre = Physics.Raycast(_centro + Vector3.up * 1.2f, dir, out RaycastHit h, 30f,
                                          Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        ? h.distance : 30f;
            if (libre > mejor) { mejor = libre; _adelante = dir; }
        }
    }

    /// <summary>Sitio de la fila para el jugador numero 'indice' de 'total'.</summary>
    public Vector3 Hueco(int indice, int total)
    {
        CalcularSitio();
        Vector3 lado = Vector3.Cross(Vector3.up, _adelante);
        // Centrados: con 1 jugador en medio, con 2 uno a cada lado...
        float desvio = (indice - (total - 1) * 0.5f) * separacion;
        return _centro + lado * desvio;
    }

    private readonly List<ulong> _orden = new List<ulong>();

    private void ColocarmeEnLaFila(PlayerController yo)
    {
        // El orden de la fila es el de los ids de cliente: igual en todas las maquinas
        _orden.Clear();
        foreach (var p in NetworkPlayer.AllPlayers)
            if (p != null && p.GetComponent<NetworkPlayer>() != null) _orden.Add(p.OwnerClientId);
        _orden.Sort();

        int indice = Mathf.Max(0, _orden.IndexOf(yo.OwnerClientId));
        Vector3 sitio = Hueco(indice, _orden.Count);

        Vector3 d = sitio - yo.transform.position;
        if (new Vector2(d.x, d.z).magnitude > 0.05f || Mathf.Abs(d.y) > 1.5f)
        {
            var cc = yo.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            yo.transform.position = new Vector3(sitio.x, Mathf.Abs(d.y) > 1.5f ? sitio.y : yo.transform.position.y, sitio.z);
            if (cc != null) cc.enabled = true;
        }

        // De cara a la camara
        yo.transform.rotation = Quaternion.LookRotation(_adelante, Vector3.up);
    }

    private void ColocarCamara()
    {
        CalcularSitio();

        if (_camara == null)
        {
            var go = new GameObject("CamaraSala");
            _camara = go.AddComponent<Camera>();
            _camara.enabled = false;
            _camara.depth = 5f;
            // Ve los cuerpos de todos (tambien el mio) y nunca las manos de primera persona
            go.AddComponent<SpectatorCameraVisual>();

            // Foco de frente, pegado a la camara: se enciende y se apaga con ella
            var luzGo = new GameObject("LuzSala");
            luzGo.transform.SetParent(go.transform, false);
            luzGo.transform.localPosition = new Vector3(1.2f, 1.1f, 0f);
            _luz = luzGo.AddComponent<Light>();
            _luz.type = LightType.Spot;
            _luz.spotAngle = 70f;
            _luz.innerSpotAngle = 45f;
            _luz.range = 16f;
            _luz.shadows = LightShadows.None;
            _luz.enabled = false;
        }

        _luz.color = colorLuz;
        _luz.intensity = intensidadLuz;
        _luz.transform.rotation = Quaternion.LookRotation((_centro + Vector3.up * 1.1f) - _luz.transform.position, Vector3.up);

        _camara.fieldOfView = campoDeVision;
        _camara.transform.position = _centro + _adelante * distanciaCamara + Vector3.up * alturaCamara;
        _camara.transform.rotation = Quaternion.LookRotation(
            (_centro + Vector3.up * alturaMirada) - _camara.transform.position, Vector3.up);
    }

    // ---------- Sobre cada jugador: nombre y si esta listo ----------

    void OnGUI()
    {
        if (!IsSpawned || Event.current.type != EventType.Repaint) return;

        if (FaseActual != Fase.EnJuego && _camara != null && _camara.enabled && !NetworkUI.MenuOpen)
            DibujarEtiquetas();

        if (IsServer && _solicitudes.Count > 0) DibujarSolicitud();
    }

    private void DibujarEtiquetas()
    {
        var estilo = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.LowerCenter, richText = true, fontSize = Mathf.RoundToInt(Screen.height / 42f)
        };

        foreach (var p in NetworkPlayer.AllPlayers)
        {
            if (p == null || p.GetComponent<NetworkPlayer>() == null) continue;

            Vector3 sp = _camara.WorldToScreenPoint(p.transform.position + Vector3.up * 2.05f);
            if (sp.z <= 0f) continue;

            var nombre = p.GetComponent<PlayerName>();
            bool anfitrion = p.OwnerClientId == NetworkManager.ServerClientId;
            bool listo = EstaListo(p.OwnerClientId);

            string texto = "<b>" + (nombre != null ? nombre.Name : "Jugador") + "</b>\n" +
                           (anfitrion ? "<color=#ffd27a>ANFITRION</color>"
                            : listo ? "<color=#7CFF8A>LISTO</color>" : "<color=#ff8a7a>NO LISTO</color>");

            Rect zona = new Rect(sp.x - 160f, Screen.height - sp.y - 80f, 320f, 80f);
            Color antes = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(zona.x + 2f, zona.y + 2f, zona.width, zona.height),
                      System.Text.RegularExpressions.Regex.Replace(texto, "</?color[^>]*>", ""), estilo);
            GUI.color = antes;
            GUI.Label(zona, texto, estilo);
        }
    }

    // Aviso al anfitrion, arriba a la derecha, sin sacarle del juego: responde con dos teclas
    private void DibujarSolicitud()
    {
        Solicitud s = _solicitudes[0];
        float escala = Screen.height / 1080f;
        float w = 520f * escala, h = 96f * escala;
        Rect caja = new Rect(Screen.width - w - 24f * escala, 190f * escala, w, h);

        Color antes = GUI.color;
        GUI.color = new Color(0.05f, 0.06f, 0.09f, 0.9f);
        GUI.DrawTexture(caja, Texture2D.whiteTexture);
        GUI.color = new Color(0.55f, 0.12f, 0.12f, 1f);
        GUI.DrawTexture(new Rect(caja.x, caja.y, 5f * escala, caja.height), Texture2D.whiteTexture);
        GUI.color = antes;

        var estilo = new GUIStyle(GUI.skin.label)
        {
            richText = true, alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(20f * escala)
        };
        int quedan = Mathf.CeilToInt(segundosSolicitud - (Time.unscaledTime - s.llego));
        string mas = _solicitudes.Count > 1 ? "  (+" + (_solicitudes.Count - 1) + " mas)" : "";
        GUI.Label(new Rect(caja.x + 18f * escala, caja.y, caja.width - 24f * escala, caja.height),
            "<b>" + s.nombre + "</b> quiere entrar en la partida" + mas + "\n" +
            "<color=#7CFF8A><b>" + teclaAceptar + "</b> aceptar</color>     " +
            "<color=#ff8a7a><b>" + teclaRechazar + "</b> rechazar</color>     " +
            "<color=#9da3ad>(" + quedan + " s)</color>", estilo);
    }
}
