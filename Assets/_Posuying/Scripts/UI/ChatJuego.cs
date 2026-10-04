using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Chat de texto de la partida.
//
// Con la tecla de chat (T por defecto, se cambia en Opciones > Controles) se abre
// una linea para escribir; Intro envia y Escape cancela. Los mensajes aparecen a
// la izquierda y se desvanecen solos.
//
// EN RED: el mensaje va al servidor, que le pone el nombre de quien lo envia (un
// cliente no puede hacerse pasar por otro) y lo reparte a todos.
//
// Va en el mismo objeto que MatchManager (tiene NetworkObject).
public class ChatJuego : NetworkBehaviour
{
    public static ChatJuego Instance { get; private set; }

    [Header("Mensajes")]
    public int maxCaracteres = 120;
    [Tooltip("Cuantas lineas se ven a la vez")]
    public int lineasVisibles = 8;
    [Tooltip("Segundos que un mensaje se queda en pantalla con el chat cerrado")]
    public float segundosVisible = 10f;
    [Tooltip("Tope de mensajes por jugador cada 5 segundos, contra el spam")]
    public int maxMensajesCada5s = 5;

    private struct Linea
    {
        public string texto;
        public float momento;
    }

    private readonly List<Linea> _lineas = new List<Linea>();
    private bool _abierto;
    private string _escribiendo = "";
    private int _frameApertura;

    // Solo servidor: momentos de los ultimos mensajes de cada cliente
    private readonly Dictionary<ulong, Queue<float>> _recientes = new Dictionary<ulong, Queue<float>>();

    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkDespawn()
    {
        Cerrar();
        _lineas.Clear();
        _recientes.Clear();
    }

    void OnDisable()
    {
        Cerrar();
    }

    void Update()
    {
        if (!IsSpawned) { if (_abierto) Cerrar(); return; }

        // Se abre solo jugando: con un menu o el inventario abiertos la T es una letra mas
        // (en la sala de espera si se puede: es donde mas falta hace hablar)
        bool ocupado = UIState.NetMenuOpen || UIState.InventoryOpen || MainMenuUI.CapturingKey;
        if (!_abierto && !ocupado && Input.GetKeyDown(GameSettings.ChatKey))
        {
            _abierto = true;
            _escribiendo = "";
            _frameApertura = Time.frameCount;
            UIState.ChatOpen = true;
        }
    }

    private void Cerrar()
    {
        _abierto = false;
        _escribiendo = "";
        UIState.ChatOpen = false;
    }

    private void Enviar()
    {
        string texto = _escribiendo.Trim();
        Cerrar();
        if (texto.Length > 0) EnviarServerRpc(texto);
    }

    // ---------- Red ----------

    [ServerRpc(RequireOwnership = false)]
    private void EnviarServerRpc(string texto, ServerRpcParams rpc = default)
    {
        if (string.IsNullOrWhiteSpace(texto)) return;
        ulong quien = rpc.Receive.SenderClientId;

        // Antispam: como mucho N mensajes cada 5 segundos por jugador
        if (!_recientes.TryGetValue(quien, out var cola)) _recientes[quien] = cola = new Queue<float>();
        while (cola.Count > 0 && Time.time - cola.Peek() > 5f) cola.Dequeue();
        if (cola.Count >= maxMensajesCada5s) return;
        cola.Enqueue(Time.time);

        // Nada de saltos de linea ni etiquetas de color metidas a mano
        texto = texto.Replace("\n", " ").Replace("\r", " ").Replace("<", "‹").Replace(">", "›").Trim();
        if (texto.Length > maxCaracteres) texto = texto.Substring(0, maxCaracteres);

        string nombre = "Jugador";
        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null || player.OwnerClientId != quien) continue;
            var n = player.GetComponent<PlayerName>();
            if (n != null) { nombre = n.Name; break; }
        }

        RecibirClientRpc(nombre.Replace("<", "‹").Replace(">", "›"), texto);
    }

    [ClientRpc]
    private void RecibirClientRpc(string nombre, string texto)
    {
        _lineas.Add(new Linea { texto = "<color=#ffd27a><b>" + nombre + ":</b></color> " + texto, momento = Time.time });
        if (_lineas.Count > 50) _lineas.RemoveAt(0);
    }

    // ---------- Interfaz ----------

    private const float ReferenceHeight = 1080f;

    void OnGUI()
    {
        if (!IsSpawned) return;

        // Las teclas se miran ANTES de pintar el campo: si no, Intro se lo queda el campo de texto
        Event e = Event.current;
        if (_abierto && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Enviar(); e.Use(); return; }
            if (e.keyCode == KeyCode.Escape) { Cerrar(); e.Use(); return; }

            // La pulsacion que abre el chat no debe escribirse en el
            if (Time.frameCount <= _frameApertura + 1) { e.Use(); return; }
        }

        Matrix4x4 previous = GUI.matrix;
        float scale = Screen.height / ReferenceHeight;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        const float x = 24f, ancho = 560f, altoLinea = 26f;
        float yCampo = ReferenceHeight - 300f;   // por encima del minimapa

        var estilo = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 18, wordWrap = true, alignment = TextAnchor.LowerLeft };

        // Mensajes, del mas nuevo (abajo) al mas viejo (arriba)
        float y = yCampo - 8f;
        int pintadas = 0;
        for (int i = _lineas.Count - 1; i >= 0 && pintadas < lineasVisibles; i--)
        {
            float edad = Time.time - _lineas[i].momento;
            float alfa = _abierto ? 1f : Mathf.Clamp01(segundosVisible - edad);   // el ultimo segundo se desvanece
            if (alfa <= 0f) break;

            float alto = Mathf.Max(altoLinea, estilo.CalcHeight(new GUIContent(_lineas[i].texto), ancho));
            y -= alto;

            Color antes = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f * alfa);
            GUI.Label(new Rect(x + 2f, y + 2f, ancho, alto), SinColor(_lineas[i].texto), estilo);
            GUI.color = new Color(1f, 1f, 1f, alfa);
            GUI.Label(new Rect(x, y, ancho, alto), _lineas[i].texto, estilo);
            GUI.color = antes;
            pintadas++;
        }

        if (_abierto)
        {
            Color antes = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(x - 6f, yCampo - 2f, ancho + 12f, 38f), Texture2D.whiteTexture);
            GUI.color = antes;

            var campo = new GUIStyle(GUI.skin.textField) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
            GUI.SetNextControlName("chat");
            _escribiendo = GUI.TextField(new Rect(x, yCampo + 2f, ancho, 30f), _escribiendo, maxCaracteres, campo);
            GUI.FocusControl("chat");

            estilo.fontSize = 14;
            estilo.alignment = TextAnchor.UpperLeft;
            GUI.Label(new Rect(x, yCampo + 38f, ancho, 22f), "<color=#c8ccd2>Intro: enviar   Esc: cancelar</color>", estilo);
        }

        GUI.matrix = previous;
    }

    private static string SinColor(string texto)
    {
        return System.Text.RegularExpressions.Regex.Replace(texto, "</?color[^>]*>", "");
    }
}
