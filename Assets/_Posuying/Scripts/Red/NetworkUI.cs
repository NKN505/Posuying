using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Controla el menu DENTRO de la partida (tecla Escape): cuando esta abierto y
// la desconexion. El menu en si lo dibuja MainMenuUI, igual que el de inicio,
// para que los dos tengan el mismo aspecto; aqui solo queda el aviso de la
// esquina y el cartel de cambio de anfitrion.
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
        _instance = this;
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

            // Con las opciones abiertas, Escape las cierra en vez de volver al juego
            // Escape cierra antes el chat o cancela la eleccion de una tecla: ahi no es "abrir el menu"
            // (en el mando la pausa es el boton Start/Options, fijo)
            bool pausa = Input.GetKeyDown(menuKey) || Controles.BotonPulsado(BotonMando.Start);
            if (pausa && !UIState.ChatOpen && !MainMenuUI.CapturingKey)
            {
                if (MainMenuUI.Instance != null && MainMenuUI.Instance.OptionsOverlayOpen)
                    MainMenuUI.Instance.CloseOptionsOverlay();
                else
                    _menuOpen = !_menuOpen;
            }
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
        bool migrating = onlineSession != null && onlineSession.IsMigrating;

        // Fuera de partida no dibujamos nada: se ve el menu principal
        if (!IsConnected() && !migrating) return;

        Matrix4x4 previousMatrix = GUI.matrix;
        float scale = Screen.height / ReferenceHeight;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        if (migrating) DrawMigrationBanner();
        else DrawInGame();

        GUI.matrix = previousMatrix;
    }

    // Cartel a media pantalla mientras se rehace la conexion
    private void DrawMigrationBanner()
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            richText = true,
            fontSize = 30,
            alignment = TextAnchor.MiddleCenter
        };

        float w = 900f;
        float h = 120f;
        float x = (ReferenceHeight * ((float)Screen.width / Screen.height) - w) / 2f;
        float y = ReferenceHeight / 2f - h / 2f;

        GUI.Box(new Rect(x, y, w, h), GUIContent.none);
        GUI.Label(new Rect(x, y, w, h),
            "<b>CAMBIANDO DE ANFITRION</b>\nReconectando...", style);
    }

    private void DrawInGame()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        // Con las opciones abiertas manda esa pantalla: aqui no dibujamos nada
        if (MainMenuUI.Instance != null && MainMenuUI.Instance.OptionsOverlayOpen) return;

        if (!_menuOpen)
        {
            if (UIState.SalaAbierta) return;   // en la sala de espera, pantalla limpia

            string hint = menuKey + " = menu";
            if (onlineSession != null && !string.IsNullOrEmpty(onlineSession.JoinCode))
                hint += "   |   CODIGO: " + onlineSession.JoinCode;

            GUI.Label(new Rect(10, 10, 500, 26), "<b>" + hint + "</b>", RichLabel());
            return;
        }

        // El menu de pausa lo dibuja MainMenuUI, con el mismo aspecto que el de inicio
    }

    // ---------- Lo que usa el menu de pausa de MainMenuUI ----------

    private static NetworkUI _instance;

    public static void CloseMenu()
    {
        if (_instance != null) _instance._menuOpen = false;
    }

    public static void LeaveGame()
    {
        if (_instance != null) _instance.Disconnect();
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
