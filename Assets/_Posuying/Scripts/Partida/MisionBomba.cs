using Unity.Netcode;
using UnityEngine;

public enum FaseBomba { BuscarBomba = 0, LlevarBomba = 1, Escapar = 2, Explotada = 3 }

// La mision del juego:
//   1. Recoger la bomba (esta en el 2o tercio). La LLEVA quien la coge: no puede
//      correr, anda mas despacio y, si le abaten, la bomba se queda en el suelo
//      donde cayo y otro tiene que recogerla.
//   2. Colocarla en el edificio grande del 3er tercio (solo quien la lleva).
//   3. Se activa una cuenta atras que ven todos: hay que volver al 1er tercio y
//      escapar por el punto de salida antes de que explote.
// Si la cuenta llega a cero con el equipo dentro, la partida se pierde.
//
// Autoridad del servidor. Va en el mismo objeto que MatchManager (tiene
// NetworkObject). Los sitios de la escena son PuntoBomba (recoger y colocar) y
// DisparadorFinal (la salida).
public class MisionBomba : NetworkBehaviour
{
    public static MisionBomba Instance { get; private set; }

    [Tooltip("Segundos para escapar desde que se coloca la bomba")]
    public float segundosParaEscapar = 180f;

    [Tooltip("Tecla para colocar la bomba estando en el sitio")]
    public KeyCode teclaColocar = KeyCode.E;

    [Tooltip("La bomba como objeto del inventario: ocupa un hueco de quien la lleva. " +
             "Tiene que estar en el catalogo (ItemDatabase).")]
    public ItemData itemBomba;

    // Solo servidor: quien acaba de soltarla no la recoge al instante (la tiene a los pies)
    [Header("La bomba en el cuerpo de quien la lleva")]
    [Tooltip("Modelo de la bomba que se le ve al portador a la altura de la cintura")]
    public GameObject modeloBomba;
    [Tooltip("Donde va respecto al jugador: derecha, arriba, delante (metros)")]
    public Vector3 posicionEnCintura = new Vector3(0f, 1.02f, 0.21f);
    [Tooltip("Giro respecto al jugador (grados)")]
    public Vector3 rotacionEnCintura = new Vector3(-12f, 0f, 0f);
    public float escalaEnCintura = 0.85f;

    [Tooltip("Quien suelta la bomba tiene que alejarse esto de ella antes de poder recogerla otra vez")]
    public float distanciaParaRecogerOtraVez = 3f;

    private ulong _soltadaPor = ulong.MaxValue;
    private float _avisoLlenoHasta;

    private readonly NetworkVariable<int> netFase = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Momento (reloj de red del servidor) en que explota. Se manda el instante y
    // no los segundos que quedan: asi cada maquina cuenta sola y no hay que
    // enviar nada mas durante toda la cuenta atras.
    private readonly NetworkVariable<double> netExplota = new NetworkVariable<double>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Quien lleva la bomba (id de cliente). SinPortador = nadie.
    private const ulong SinPortador = ulong.MaxValue;
    private readonly NetworkVariable<ulong> netPortador = new NetworkVariable<ulong>(
        SinPortador, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Donde esta la bomba si se le ha caido a alguien. Mientras nadie la haya
    // movido esta en su sitio de la escena (PuntoBomba de tipo Recoger).
    private readonly NetworkVariable<bool> netCaida = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Vector3> netDondeCayo = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Cuanto anda quien lleva la bomba respecto a lo normal.</summary>
    public const float VelocidadConBomba = 0.8f;

    public FaseBomba Fase => (FaseBomba)netFase.Value;
    public bool BombaCaida => netCaida.Value;
    public Vector3 DondeCayo => netDondeCayo.Value;

    /// <summary>True en la maquina del jugador que carga con la bomba.</summary>
    public static bool LocalLlevaBomba
    {
        get
        {
            var m = Instance;
            return m != null && m.IsSpawned && m.Fase == FaseBomba.LlevarBomba &&
                   m.netPortador.Value == m.NetworkManager.LocalClientId;
        }
    }

    public float SegundosRestantes
    {
        get
        {
            if (Fase != FaseBomba.Escapar || NetworkManager == null) return 0f;
            return Mathf.Max(0f, (float)(netExplota.Value - NetworkManager.ServerTime.Time));
        }
    }

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (IsServer && Fase == FaseBomba.Escapar && NetworkManager.ServerTime.Time >= netExplota.Value)
            Explotar();

        if (IsServer && Fase == FaseBomba.LlevarBomba) VigilarPortador();
        if (IsSpawned) ActualizarBombaEnCintura();
        if (IsServer && _soltadaPor != ulong.MaxValue) VigilarQuienLaSolto();

        ComprobarTeclaColocar();
    }

    // ---------- Pasos de la mision (servidor) ----------

    public void RecogerBomba(NetworkPlayer jugador)
    {
        if (!IsServer || Fase != FaseBomba.BuscarBomba) return;
        if (MatchManager.Instance != null && MatchManager.Instance.MatchOver) return;

        // Quien la acaba de soltar no la recoge hasta que se haya apartado de ella:
        // la tiene a los pies y, si no, volveria a su inventario sola.
        // (el bloqueo se levanta en VigilarQuienLaSolto, cuando se aleja)
        if (jugador.OwnerClientId == _soltadaPor) return;

        // Tiene que caberle: la bomba ocupa un hueco del inventario
        var inventario = jugador.GetComponent<Inventory>();
        if (itemBomba != null && inventario != null && inventario.AddItem(itemBomba, 1) > 0)
        {
            if (Time.time >= _avisoLlenoHasta)
            {
                _avisoLlenoHasta = Time.time + 4f;
                AvisarClientRpc(Nombre(jugador) + " no puede coger la bomba: tiene el inventario lleno");
            }
            return;
        }

        netPortador.Value = jugador.OwnerClientId;
        netFase.Value = (int)FaseBomba.LlevarBomba;
        AvisarClientRpc(Nombre(jugador) + " lleva la bomba: protegedle hasta el edificio del 3er tercio");
    }

    private PlayerController JugadorDe(ulong cliente)
    {
        foreach (var player in NetworkPlayer.AllPlayers)
            if (player != null && player.OwnerClientId == cliente &&
                player.GetComponent<NetworkPlayer>() != null) return player;
        return null;
    }

    private void QuitarDelInventario(PlayerController jugador)
    {
        if (jugador == null || itemBomba == null) return;
        var inventario = jugador.GetComponent<Inventory>();
        if (inventario != null) inventario.RemoveItem(itemBomba, 1);
    }

    // Deja la bomba en el suelo en 'donde' y la mision vuelve a "buscarla"
    private void DejarEnElSuelo(PlayerController portador, Vector3 donde, string aviso)
    {
        var golpes = Physics.RaycastAll(donde + Vector3.up, Vector3.down, 20f,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float mejor = float.NegativeInfinity;
        foreach (var g in golpes)
        {
            // Sin contar a los personajes, como los objetos soltados
            if (g.collider.GetComponentInParent<Character>() != null) continue;
            if (g.point.y > mejor) { mejor = g.point.y; donde = g.point; }
        }

        QuitarDelInventario(portador);
        netDondeCayo.Value = donde;
        netCaida.Value = true;
        netPortador.Value = SinPortador;
        netFase.Value = (int)FaseBomba.BuscarBomba;
        AvisarClientRpc(aviso);
    }

    // Lo llama Pertenencias justo antes de vaciar el inventario de un caido
    public void SoltarSiLaLleva(ulong cliente)
    {
        if (!IsServer || Fase != FaseBomba.LlevarBomba || netPortador.Value != cliente) return;
        var portador = JugadorDe(cliente);
        if (portador == null) return;
        DejarEnElSuelo(portador, portador.transform.position,
                       Nombre(portador.GetComponent<NetworkPlayer>()) + " ha caido: la bomba esta en el suelo");
    }

    // Se mira aqui y no al recogerla: mientras esta lejos no hay nada que avise
    // de que ya puede volver a por ella.
    private void VigilarQuienLaSolto()
    {
        var jugador = JugadorDe(_soltadaPor);
        if (jugador == null || Fase != FaseBomba.BuscarBomba) { _soltadaPor = ulong.MaxValue; return; }

        Vector3 d = jugador.transform.position - netDondeCayo.Value; d.y = 0f;
        if (d.magnitude >= distanciaParaRecogerOtraVez) _soltadaPor = ulong.MaxValue;
    }

    // ---------- La bomba a la vista en quien la lleva ----------
    // En TODAS las maquinas: quien la lleva viaja por red, asi que cada una le
    // cuelga la bomba a la copia de ese jugador. Va sujeta al hueso de la
    // columna para que acompane al cuerpo al andar y al agacharse.

    private GameObject _bombaVisible;
    private PlayerController _bombaEn;

    private void ActualizarBombaEnCintura()
    {
        PlayerController portador = Fase == FaseBomba.LlevarBomba ? JugadorDe(netPortador.Value) : null;
        if (portador == _bombaEn && (portador == null || _bombaVisible != null)) return;

        if (_bombaVisible != null) Destroy(_bombaVisible);
        _bombaVisible = null;
        _bombaEn = portador;
        if (portador == null || modeloBomba == null) return;

        // Hueso al que se sujeta: la columna si el esqueleto es humanoide; si no, la raiz
        Transform hueso = portador.transform;
        var animador = portador.GetComponent<Animator>();
        if (animador != null && animador.isHuman)
        {
            Transform columna = animador.GetBoneTransform(HumanBodyBones.Spine);
            if (columna == null) columna = animador.GetBoneTransform(HumanBodyBones.Hips);
            if (columna != null) hueso = columna;
        }

        _bombaVisible = Instantiate(modeloBomba);
        _bombaVisible.name = "Bomba_En_Cintura";
        foreach (var c in _bombaVisible.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var l in _bombaVisible.GetComponentsInChildren<Light>(true)) l.enabled = false;

        // Se coloca respecto al jugador (que es lo facil de ajustar) y luego se
        // cuelga del hueso conservando esa postura
        Transform raiz = portador.transform;
        _bombaVisible.transform.SetPositionAndRotation(
            raiz.TransformPoint(posicionEnCintura), raiz.rotation * Quaternion.Euler(rotacionEnCintura));
        _bombaVisible.transform.localScale = Vector3.one * escalaEnCintura;
        _bombaVisible.transform.SetParent(hueso, true);

        // El que la lleva no debe verla flotando delante de su camara: va en la
        // misma capa que su cuerpo, que su camara no dibuja (los demas si la ven)
        int capa = CapaPorNombre(portador.IsOwner ? PlayerVisual.LocalBodyLayerName : PlayerVisual.RemoteBodyLayerName);
        if (capa >= 0)
            foreach (var t in _bombaVisible.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = capa;
    }

    // Algunas capas del proyecto estan guardadas con un espacio al final
    private static int CapaPorNombre(string nombre)
    {
        int capa = LayerMask.NameToLayer(nombre);
        if (capa >= 0) return capa;
        for (int i = 0; i < 32; i++)
            if (string.Equals(LayerMask.LayerToName(i).Trim(), nombre, System.StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public override void OnNetworkDespawn()
    {
        if (_bombaVisible != null) Destroy(_bombaVisible);
        _bombaVisible = null;
        _bombaEn = null;
    }

    // ---------- Soltarla a proposito ----------

    /// <summary>El jugador local pide soltar la bomba (tecla o clic derecho en el inventario).</summary>
    public void PedirSoltar()
    {
        if (LocalLlevaBomba) SoltarServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SoltarServerRpc(ServerRpcParams rpc = default)
    {
        ulong quien = rpc.Receive.SenderClientId;
        if (Fase != FaseBomba.LlevarBomba || netPortador.Value != quien) return;

        var portador = JugadorDe(quien);
        if (portador == null) return;

        // Un paso por delante; el mismo no la recoge hasta que se aparte
        _soltadaPor = quien;
        DejarEnElSuelo(portador, portador.transform.position + portador.transform.forward * 1.2f,
                       Nombre(portador.GetComponent<NetworkPlayer>()) + " ha soltado la bomba");
    }

    // Si quien la lleva cae abatido, queda eliminado o se desconecta, la bomba se
    // queda en el suelo donde estaba y hay que volver a recogerla.
    private void VigilarPortador()
    {
        PlayerController portador = JugadorDe(netPortador.Value);

        if (portador == null)
        {
            // Se ha ido de la partida: la bomba vuelve a su sitio inicial
            netCaida.Value = false;
            netPortador.Value = SinPortador;
            netFase.Value = (int)FaseBomba.BuscarBomba;
            AvisarClientRpc("Quien llevaba la bomba se ha ido: ha vuelto a su sitio");
            return;
        }

        var estado = portador.GetComponent<PlayerDownedState>();
        if (estado == null || estado.CanAct) return;

        var nombre = portador.GetComponent<PlayerName>();
        DejarEnElSuelo(portador, portador.transform.position,
                       (nombre != null ? nombre.Name : "Un jugador") + " ha caido: la bomba esta en el suelo");
    }

    private void ColocarBomba(NetworkPlayer jugador)
    {
        if (!IsServer || Fase != FaseBomba.LlevarBomba) return;
        if (MatchManager.Instance != null && MatchManager.Instance.MatchOver) return;

        QuitarDelInventario(JugadorDe(netPortador.Value));   // ya no la lleva: esta puesta
        netExplota.Value = NetworkManager.ServerTime.Time + segundosParaEscapar;
        netPortador.Value = SinPortador;
        netFase.Value = (int)FaseBomba.Escapar;
        AvisarClientRpc(Nombre(jugador) + " ha colocado la bomba. ¡Volved al 1er tercio y escapad!");
    }

    private void Explotar()
    {
        netFase.Value = (int)FaseBomba.Explotada;
        AvisarClientRpc("La bomba ha explotado con vosotros dentro");
        if (MatchManager.Instance != null) MatchManager.Instance.DeclararDerrota();
    }

    // Al reiniciar la partida la mision vuelve a empezar
    public void Reiniciar()
    {
        if (!IsServer) return;
        netFase.Value = (int)FaseBomba.BuscarBomba;
        netExplota.Value = 0;
        netPortador.Value = SinPortador;
        netCaida.Value = false;
        _soltadaPor = ulong.MaxValue;

        // Que no quede ninguna bomba en un inventario de la partida anterior
        foreach (var player in NetworkPlayer.AllPlayers) QuitarDelInventario(player);
    }

    // ---------- Cambio de anfitrion ----------

    // Lo llama el anfitrion actual al fotografiar la partida
    public void Guardar(WorldState.MatchState estado)
    {
        estado.faseBomba = netFase.Value;
        estado.bombaEnSuelo = netCaida.Value;
        estado.bombaPosicion = netDondeCayo.Value;
        estado.segundosParaExplotar = SegundosRestantes;

        // Si alguien la lleva, para el anfitrion nuevo estara en el suelo donde iba
        if (Fase == FaseBomba.LlevarBomba)
        {
            var portador = JugadorDe(netPortador.Value);
            estado.faseBomba = (int)FaseBomba.BuscarBomba;
            estado.bombaEnSuelo = portador != null;
            if (portador != null) estado.bombaPosicion = portador.transform.position;
        }
    }

    // Lo llama el anfitrion nuevo al reconstruir la partida
    public void Restaurar(WorldState.MatchState estado)
    {
        if (!IsServer) return;

        netPortador.Value = SinPortador;
        netCaida.Value = estado.bombaEnSuelo;
        netDondeCayo.Value = estado.bombaPosicion;

        var fase = (FaseBomba)Mathf.Clamp(estado.faseBomba, 0, 3);
        if (fase == FaseBomba.Escapar)
            netExplota.Value = NetworkManager.ServerTime.Time + Mathf.Max(5f, estado.segundosParaExplotar);
        if (fase == FaseBomba.Explotada || fase == FaseBomba.LlevarBomba) fase = FaseBomba.BuscarBomba;

        netFase.Value = (int)fase;
    }

    private string NombrePortador()
    {
        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null || player.OwnerClientId != netPortador.Value) continue;
            var nombre = player.GetComponent<PlayerName>();
            if (nombre != null) return nombre.Name;
        }
        return "Un companero";
    }

    [ClientRpc]
    private void AvisarClientRpc(string texto)
    {
        Notifications.Show(texto);
    }

    private static string Nombre(NetworkPlayer jugador)
    {
        var nombre = jugador != null ? jugador.GetComponent<PlayerName>() : null;
        return nombre != null ? nombre.Name : "Un jugador";
    }

    // ---------- Colocar: lo pide el jugador con una tecla ----------

    // El sitio de colocar junto al que esta el jugador local, o null
    private PuntoBomba SitioCercano()
    {
        var yo = NetworkPlayer.LocalPlayer;
        if (yo == null || !LocalLlevaBomba) return null;   // solo la coloca quien la lleva

        var estado = yo.GetComponent<PlayerDownedState>();
        if (estado != null && !estado.CanAct) return null;

        foreach (var punto in PuntoBomba.Todos)
            if (punto != null && punto.tipo == PuntoBomba.Tipo.Colocar && punto.EstaCerca(yo.transform.position))
                return punto;
        return null;
    }

    private void ComprobarTeclaColocar()
    {
        if (!IsSpawned || UIState.BlocksGameplay) return;

        // Soltar la bomba lo lleva el inventario (tecla de soltar o clic derecho en su hueco)

        if (Input.GetKeyDown(teclaColocar) && SitioCercano() != null) PedirColocarServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void PedirColocarServerRpc(ServerRpcParams rpc = default)
    {
        // El servidor no se fia: comprueba el que el que lo pide esta de verdad en el sitio
        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null || player.OwnerClientId != rpc.Receive.SenderClientId) continue;
            if (player.OwnerClientId != netPortador.Value) return;   // solo quien la lleva
            var jugador = player.GetComponent<NetworkPlayer>();
            if (jugador == null) continue;   // un NPC no coloca bombas

            var estado = player.GetComponent<PlayerDownedState>();
            if (estado != null && !estado.CanAct) return;

            foreach (var punto in PuntoBomba.Todos)
                if (punto != null && punto.tipo == PuntoBomba.Tipo.Colocar &&
                    punto.EstaCerca(player.transform.position, 1.5f))
                {
                    ColocarBomba(jugador);
                    return;
                }
        }
    }

    // ---------- Interfaz: objetivo y cuenta atras, en todas las maquinas ----------

    private const float ReferenceHeight = 1080f;

    void OnGUI()
    {
        if (!IsSpawned || Event.current.type != EventType.Repaint) return;
        if (MatchManager.Instance != null && MatchManager.Instance.MatchOver) return;
        if (!SalaEspera.EnJuego) return;   // el objetivo se ensena cuando empieza la partida

        Matrix4x4 previous = GUI.matrix;
        float scale = Screen.height / ReferenceHeight;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        float refWidth = ReferenceHeight * ((float)Screen.width / Screen.height);

        string objetivo = "";
        switch (Fase)
        {
            case FaseBomba.BuscarBomba:
                objetivo = BombaCaida ? "OBJETIVO: recupera la bomba, se ha quedado en el suelo"
                                      : "OBJETIVO: encuentra la bomba en el 2º tercio";
                break;
            case FaseBomba.LlevarBomba:
                objetivo = LocalLlevaBomba
                    ? "LLEVAS LA BOMBA (no puedes correr, " + GameSettings.KeyLabel(GameSettings.DropKey) +
                      " la suelta): colocala en el edificio del 3er tercio"
                    : "OBJETIVO: protege a " + NombrePortador() + ", que lleva la bomba al edificio del 3er tercio";
                break;
            case FaseBomba.Escapar: objetivo = "OBJETIVO: vuelve al 1er tercio y escapa"; break;
        }

        var estilo = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter, fontSize = 18 };
        // Justo debajo de las vidas y por encima de los avisos, que empiezan en y = 60
        Texto(new Rect(refWidth / 2f - 400f, 38f, 800f, 24f), "<color=#ffe08a>" + objetivo + "</color>", estilo);

        if (Fase == FaseBomba.Escapar)
        {
            float t = SegundosRestantes;
            int minutos = Mathf.FloorToInt(t / 60f), segundos = Mathf.FloorToInt(t % 60f);
            // Rojo y parpadeando en el ultimo medio minuto
            bool apurado = t <= 30f;
            string color = !apurado ? "#ffffff" : (Mathf.FloorToInt(Time.unscaledTime * 2f) % 2 == 0 ? "#ff5555" : "#ffaaaa");
            // Arriba a la derecha: en el centro lo tapaban los avisos
            estilo.alignment = TextAnchor.MiddleRight;
            estilo.fontSize = 20;
            Texto(new Rect(refWidth - 440f, 70f, 400f, 26f), "<color=#ffe08a>LA BOMBA EXPLOTA EN</color>", estilo);
            estilo.fontSize = 64;
            Texto(new Rect(refWidth - 440f, 92f, 400f, 76f),
                  "<b><color=" + color + ">" + minutos.ToString("00") + ":" + segundos.ToString("00") + "</color></b>", estilo);
            estilo.alignment = TextAnchor.MiddleCenter;
        }
        else if (SitioCercano() != null && !UIState.BlocksGameplay)
        {
            estilo.fontSize = 26;
            Texto(new Rect(refWidth / 2f - 400f, ReferenceHeight * 0.62f, 800f, 40f),
                  "Pulsa <b>" + teclaColocar + "</b> para colocar la bomba", estilo);
        }

        GUI.matrix = previous;
    }

    // Con sombra, para que se lea sobre cualquier fondo
    private static void Texto(Rect zona, string texto, GUIStyle estilo)
    {
        Color antes = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        string plano = System.Text.RegularExpressions.Regex.Replace(texto, "</?color[^>]*>", "");
        GUI.Label(new Rect(zona.x + 2f, zona.y + 2f, zona.width, zona.height), plano, estilo);
        GUI.color = antes;
        GUI.Label(zona, texto, estilo);
    }
}
