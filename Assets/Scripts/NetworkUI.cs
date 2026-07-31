using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Interfaz minima para crear o unirse a una partida.
// Se dibuja con OnGUI para no depender de un Canvas: basta con poner
// este script en el objeto NetworkManager de la escena.
public class NetworkUI : MonoBehaviour
{
    [Header("Conexion")]
    [Tooltip("IP del host. 127.0.0.1 = este mismo PC (para probar con dos ventanas)")]
    public string joinIp = "127.0.0.1";
    public ushort port = 7777;

    [Header("Atajos de teclado")]
    public KeyCode hostKey = KeyCode.F1;
    public KeyCode clientKey = KeyCode.F2;

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
        GUILayout.BeginArea(new Rect(10, 10, 280, 260));

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
        GUILayout.Label("<b>POSUYING - COOP</b>", RichLabel());

        GUILayout.Space(6);
        GUILayout.Label("IP del host:");
        joinIp = GUILayout.TextField(joinIp);

        GUILayout.Space(6);

        if (GUILayout.Button("CREAR PARTIDA (Host)  [" + hostKey + "]", GUILayout.Height(34)))
            StartHost();

        if (GUILayout.Button("UNIRSE (Cliente)  [" + clientKey + "]", GUILayout.Height(34)))
            StartClient();

        GUILayout.Space(6);
        GUILayout.Label("El host crea la partida. El otro jugador\npone la IP del host y pulsa Unirse.");
    }

    private void DrawStatus()
    {
        var nm = NetworkManager.Singleton;

        string rol = nm.IsHost ? "HOST" : (nm.IsServer ? "SERVIDOR" : "CLIENTE");
        GUILayout.Label("<b>" + rol + "</b>", RichLabel());
        GUILayout.Label("Mi id de cliente: " + nm.LocalClientId);

        if (nm.IsServer)
            GUILayout.Label("Jugadores conectados: " + nm.ConnectedClientsIds.Count);

        GUILayout.Space(6);
        if (GUILayout.Button("Desconectar", GUILayout.Height(28)))
            nm.Shutdown();
    }

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

    private GUIStyle RichLabel()
    {
        return new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 };
    }
}
