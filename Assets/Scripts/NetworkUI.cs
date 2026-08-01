using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Interfaz minima para crear o unirse a una partida.
// Se dibuja con OnGUI para no depender de un Canvas: basta con poner
// este script en el objeto NetworkManager de la escena.
//
// Dos modos:
//  - LOCAL: por IP. Util para probar en el mismo PC con Multiplayer Play Mode.
//  - ONLINE: por codigo de partida (Relay), para jugar desde casas distintas.
public class NetworkUI : MonoBehaviour
{
    [Header("Conexion local (mismo PC / LAN)")]
    [Tooltip("IP del host. 127.0.0.1 = este mismo PC")]
    public string joinIp = "127.0.0.1";
    public ushort port = 7777;

    [Header("Conexion online (codigo de partida)")]
    public OnlineSession onlineSession;

    [Header("Atajos de teclado")]
    public KeyCode hostKey = KeyCode.F1;
    public KeyCode clientKey = KeyCode.F2;

    private string _codeInput = "";

    void Awake()
    {
        if (onlineSession == null)
            onlineSession = GetComponent<OnlineSession>();
    }

    void Update()
    {
        // Mientras no estemos conectados hace falta el raton para pulsar los botones,
        // asi que lo liberamos (PlayerController lo bloquea al arrancar).
        if (!IsConnected())
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (Input.GetKeyDown(hostKey)) StartHost();
            else if (Input.GetKeyDown(clientKey)) StartClient();
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 330, 430));

        if (NetworkManager.Singleton == null)
        {
            GUILayout.Label("Falta el objeto NetworkManager en la escena.");
            GUILayout.EndArea();
            return;
        }

        if (IsConnected()) DrawStatus();
        else DrawStartButtons();

        GUILayout.EndArea();
    }

    private void DrawStartButtons()
    {
        GUILayout.Label("<b>POSUYING - COOP</b>", RichLabel(15));

        // ---------- ONLINE ----------
        GUILayout.Space(8);
        GUILayout.Label("<b>Jugar por internet</b>", RichLabel());

        if (onlineSession == null)
        {
            GUILayout.Label("Falta el componente OnlineSession.");
        }
        else if (onlineSession.Busy)
        {
            GUILayout.Label(onlineSession.Status);
        }
        else
        {
            if (GUILayout.Button("CREAR PARTIDA ONLINE", GUILayout.Height(32)))
                onlineSession.CreateOnlineGame();

            GUILayout.Space(4);
            GUILayout.Label("Codigo de tu companero:");
            _codeInput = GUILayout.TextField(_codeInput, 10);

            if (GUILayout.Button("UNIRSE CON CODIGO", GUILayout.Height(32)))
                onlineSession.JoinOnlineGame(_codeInput);

            if (!string.IsNullOrEmpty(onlineSession.Status))
                GUILayout.Label(onlineSession.Status);
        }

        // ---------- LOCAL ----------
        GUILayout.Space(12);
        GUILayout.Label("<b>Jugar en este PC / red local</b>", RichLabel());

        GUILayout.Label("IP del host:");
        joinIp = GUILayout.TextField(joinIp);

        if (GUILayout.Button("Host local  [" + hostKey + "]"))
            StartHost();

        if (GUILayout.Button("Cliente local  [" + clientKey + "]"))
            StartClient();
    }

    private void DrawStatus()
    {
        var nm = NetworkManager.Singleton;

        string rol = nm.IsHost ? "HOST" : (nm.IsServer ? "SERVIDOR" : "CLIENTE");
        GUILayout.Label("<b>" + rol + "</b>", RichLabel(15));
        GUILayout.Label("Mi id de cliente: " + nm.LocalClientId);

        if (nm.IsServer)
            GUILayout.Label("Jugadores conectados: " + nm.ConnectedClientsIds.Count);

        // Codigo de partida bien visible, para poder dictarselo al companero
        if (onlineSession != null && !string.IsNullOrEmpty(onlineSession.JoinCode))
        {
            GUILayout.Space(6);
            GUILayout.Label("<b>CODIGO: " + onlineSession.JoinCode + "</b>", RichLabel(18));

            if (GUILayout.Button("Copiar codigo"))
                GUIUtility.systemCopyBuffer = onlineSession.JoinCode;
        }

        GUILayout.Space(8);
        if (GUILayout.Button("Desconectar", GUILayout.Height(28)))
            Disconnect();
    }

    private void Disconnect()
    {
        if (onlineSession != null && onlineSession.HasSession)
            onlineSession.LeaveOnlineGame();   // cierra tambien la sesion de Relay
        else
            NetworkManager.Singleton.Shutdown();
    }

    // ---------- Conexion local por IP ----------

    private void StartHost()
    {
        ApplyConnectionData();
        if (NetworkManager.Singleton.StartHost())
            Debug.Log("Partida creada como HOST en el puerto " + port);
        else
            Debug.LogError("No se pudo crear la partida como host");
    }

    private void StartClient()
    {
        ApplyConnectionData();
        if (NetworkManager.Singleton.StartClient())
            Debug.Log("Conectando a " + joinIp + ":" + port + " ...");
        else
            Debug.LogError("No se pudo iniciar el cliente");
    }

    private void ApplyConnectionData()
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
            transport.SetConnectionData(joinIp, port);
    }

    private bool IsConnected()
    {
        var nm = NetworkManager.Singleton;
        return nm != null && (nm.IsClient || nm.IsServer);
    }

    private GUIStyle RichLabel(int size = 13)
    {
        return new GUIStyle(GUI.skin.label) { richText = true, fontSize = size };
    }
}
