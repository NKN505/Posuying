using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Menu rapido DENTRO de la partida (tecla Escape): estado, codigo, abrir/cerrar
// la partida y desconectar.
//
// La pantalla inicial (crear / buscar / codigo / opciones) la lleva MainMenuUI,
// que es una interfaz de Canvas en condiciones.
//
// Este script tambien manda sobre el cursor y sobre si el jugador puede moverse.
public class NetworkUI : MonoBehaviour
{
    [Header("Conexion local (pruebas en el mismo PC, sin Relay)")]
    public string joinIp = "127.0.0.1";
    public ushort port = 7777;

    [Header("Referencias")]
    public OnlineSession onlineSession;

    [Header("Teclas")]
    public KeyCode menuKey = KeyCode.Escape;
    public KeyCode hostKey = KeyCode.F1;
    public KeyCode clientKey = KeyCode.F2;

    // Lo consulta PlayerController para no moverse mientras el menu esta abierto
    public static bool MenuOpen { get; private set; }

    private const float ReferenceHeight = 1080f;

    private bool _menuOpen = true;
    private bool _wasConnected = false;

    void Awake()
    {
        if (onlineSession == null)
            onlineSession = GetComponent<OnlineSession>();
    }

    void Update()
    {
        bool connected = IsConnected();

        if (!connected)
        {
            // Fuera de partida manda el menu principal: cursor libre
            _menuOpen = true;

            // Atajos de partida local para pruebas rapidas en un mismo PC
            if (Input.GetKeyDown(hostKey)) StartHost();
            else if (Input.GetKeyDown(clientKey)) StartClient();
        }
        else
        {
            if (!_wasConnected) _menuOpen = false;   // al entrar, a jugar
            if (Input.GetKeyDown(menuKey)) _menuOpen = !_menuOpen;
        }

        _wasConnected = connected;
        MenuOpen = _menuOpen;
        UIState.NetMenuOpen = _menuOpen;

        bool freeCursor = UIState.BlocksGameplay;
        Cursor.lockState = freeCursor ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = freeCursor;
    }

    void OnGUI()
    {
        // Fuera de partida no dibujamos nada: se ve el menu principal
        if (!IsConnected()) return;

        Matrix4x4 previousMatrix = GUI.matrix;
        float scale = Screen.height / ReferenceHeight;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        DrawInGame();

        GUI.matrix = previousMatrix;
    }

    private void DrawInGame()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (!_menuOpen)
        {
            string hint = menuKey + " = menu";
            if (onlineSession != null && !string.IsNullOrEmpty(onlineSession.JoinCode))
                hint += "   |   CODIGO: " + onlineSession.JoinCode;

            GUI.Label(new Rect(10, 10, 500, 26), "<b>" + hint + "</b>", RichLabel());
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 320, 420));

        string rol = nm.IsHost ? "HOST" : (nm.IsServer ? "SERVIDOR" : "CLIENTE");
        GUILayout.Label("<b>" + rol + "</b>", RichLabel(15));
        GUILayout.Label("Mi id de cliente: " + nm.LocalClientId);

        if (nm.IsServer)
            GUILayout.Label("Jugadores conectados: " + nm.ConnectedClientsIds.Count);

        if (onlineSession != null && !string.IsNullOrEmpty(onlineSession.JoinCode))
        {
            GUILayout.Space(6);
            GUILayout.Label("<b>CODIGO: " + onlineSession.JoinCode + "</b>", RichLabel(18));

            if (GUILayout.Button("Copiar codigo"))
                GUIUtility.systemCopyBuffer = onlineSession.JoinCode;
        }

        // Solo el host decide si puede entrar gente con la partida empezada
        if (onlineSession != null && onlineSession.IsHost)
        {
            GUILayout.Space(8);
            bool locked = onlineSession.IsGameLocked;

            GUILayout.Label(locked
                ? "Partida CERRADA (no entra nadie mas)"
                : "Partida ABIERTA (se puede entrar en marcha)");

            if (GUILayout.Button(locked ? "Abrir partida" : "Cerrar partida", GUILayout.Height(26)))
                onlineSession.SetGameLocked(!locked);
        }

        GUILayout.Space(8);
        if (GUILayout.Button("Seguir jugando  [" + menuKey + "]", GUILayout.Height(26)))
            _menuOpen = false;

        GUILayout.Space(4);
        if (GUILayout.Button("Salir de la partida", GUILayout.Height(28)))
            Disconnect();

        GUILayout.EndArea();
    }

    private void Disconnect()
    {
        if (onlineSession != null && onlineSession.HasSession)
            onlineSession.LeaveOnlineGame();   // cierra tambien la sesion de Relay
        else
            NetworkManager.Singleton.Shutdown();
    }

    // ---------- Partida local por IP (solo para pruebas en un mismo PC) ----------

    private void StartHost()
    {
        ApplyConnectionData();
        if (!NetworkManager.Singleton.StartHost())
            Debug.LogError("No se pudo crear la partida local");
    }

    private void StartClient()
    {
        ApplyConnectionData();
        if (!NetworkManager.Singleton.StartClient())
            Debug.LogError("No se pudo iniciar el cliente local");
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
