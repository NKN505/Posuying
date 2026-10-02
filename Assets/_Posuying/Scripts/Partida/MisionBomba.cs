using Unity.Netcode;
using UnityEngine;

public enum FaseBomba { BuscarBomba = 0, LlevarBomba = 1, Escapar = 2, Explotada = 3 }

// La mision del juego:
//   1. Recoger la bomba (esta en el 2o tercio).
//   2. Colocarla en el edificio grande del 3er tercio.
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

    private readonly NetworkVariable<int> netFase = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Momento (reloj de red del servidor) en que explota. Se manda el instante y
    // no los segundos que quedan: asi cada maquina cuenta sola y no hay que
    // enviar nada mas durante toda la cuenta atras.
    private readonly NetworkVariable<double> netExplota = new NetworkVariable<double>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public FaseBomba Fase => (FaseBomba)netFase.Value;

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

        ComprobarTeclaColocar();
    }

    // ---------- Pasos de la mision (servidor) ----------

    public void RecogerBomba(NetworkPlayer jugador)
    {
        if (!IsServer || Fase != FaseBomba.BuscarBomba) return;
        if (MatchManager.Instance != null && MatchManager.Instance.MatchOver) return;

        netFase.Value = (int)FaseBomba.LlevarBomba;
        AvisarClientRpc(Nombre(jugador) + " ha recogido la bomba: llevadla al edificio del 3er tercio");
    }

    private void ColocarBomba(NetworkPlayer jugador)
    {
        if (!IsServer || Fase != FaseBomba.LlevarBomba) return;
        if (MatchManager.Instance != null && MatchManager.Instance.MatchOver) return;

        netExplota.Value = NetworkManager.ServerTime.Time + segundosParaEscapar;
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
        if (yo == null || Fase != FaseBomba.LlevarBomba) return null;

        var estado = yo.GetComponent<PlayerDownedState>();
        if (estado != null && !estado.CanAct) return null;

        foreach (var punto in PuntoBomba.Todos)
            if (punto != null && punto.tipo == PuntoBomba.Tipo.Colocar && punto.EstaCerca(yo.transform.position))
                return punto;
        return null;
    }

    private void ComprobarTeclaColocar()
    {
        if (!IsSpawned || UIState.BlocksGameplay || !Input.GetKeyDown(teclaColocar)) return;
        if (SitioCercano() != null) PedirColocarServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void PedirColocarServerRpc(ServerRpcParams rpc = default)
    {
        // El servidor no se fia: comprueba el que el que lo pide esta de verdad en el sitio
        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null || player.OwnerClientId != rpc.Receive.SenderClientId) continue;
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

        Matrix4x4 previous = GUI.matrix;
        float scale = Screen.height / ReferenceHeight;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        float refWidth = ReferenceHeight * ((float)Screen.width / Screen.height);

        string objetivo = "";
        switch (Fase)
        {
            case FaseBomba.BuscarBomba: objetivo = "OBJETIVO: encuentra la bomba en el 2º tercio"; break;
            case FaseBomba.LlevarBomba: objetivo = "OBJETIVO: coloca la bomba en el edificio del 3er tercio"; break;
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
