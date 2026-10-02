using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Chat de voz de proximidad, pulsando una tecla para hablar.
//
// Manteniendo la tecla de voz (V por defecto, se cambia en Opciones > Controles)
// se captura el microfono y se envia a los demas. Cada voz suena DESDE el jugador
// que habla (ver VozDeJugador): se oye mas baja cuanto mas lejos esta, cambia de
// eco segun el sitio en el que se encuentra y se apaga si hay una pared en medio.
//
// EN RED: el audio viaja por la misma conexion de la partida, a trozos de 40 ms,
// sin garantia de entrega (un trozo perdido es un chasquido; reenviarlo tarde
// seria peor). Cliente -> servidor -> resto de clientes.
//
// Usa el audio de Unity (microfono y AudioSource), no Wwise: por eso el proyecto
// tiene que tener el audio de Unity ACTIVADO en Project Settings > Audio.
//
// Va en el mismo objeto que MatchManager (tiene NetworkObject).
public class ChatVoz : NetworkBehaviour
{
    public static ChatVoz Instance { get; private set; }

    public const int Frecuencia = 16000;      // de sobra para voz
    private const int MuestrasPorTrozo = 640; // 40 ms: cabe en un paquete sin partirlo

    [Header("Prueba")]
    [Tooltip("Te oyes a ti mismo, para probar el microfono sin un segundo jugador. " +
             "Dejar desmarcado para jugar.")]
    public bool oirmeAMiMismo = false;

    private AudioClip _micro;
    private string _dispositivo;
    private int _leido;
    private float _soltadoEn = -999f;
    private bool _avisadoSinMicro;
    private readonly float[] _muestras = new float[MuestrasPorTrozo];

    // Quien esta hablando ahora mismo (para el indicador en pantalla)
    private readonly Dictionary<ulong, float> _ultimaVoz = new Dictionary<ulong, float>();
    private bool _transmitiendo;

    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkDespawn()
    {
        PararMicro();
        _ultimaVoz.Clear();
    }

    void OnDisable()
    {
        PararMicro();
    }

    void Update()
    {
        if (!IsSpawned) return;

        AsegurarOyente();

        bool quiereHablar = GameSettings.VoiceEnabled && !UIState.ChatOpen && !MainMenuUI.CapturingKey &&
                            NetworkPlayer.LocalPlayer != null && Input.GetKey(GameSettings.VoiceKey);

        if (quiereHablar && _micro == null) EmpezarMicro();

        _transmitiendo = quiereHablar && _micro != null;
        if (_micro != null) BombearMicro(_transmitiendo);

        // El microfono se deja abierto unos segundos tras soltar la tecla: abrirlo
        // tarda, y si se cerrase cada vez se perderia el principio de cada frase.
        if (quiereHablar) _soltadoEn = Time.unscaledTime;
        else if (_micro != null && Time.unscaledTime - _soltadoEn > 5f) PararMicro();
    }

    // ---------- Microfono ----------

    private void EmpezarMicro()
    {
        if (Microphone.devices.Length == 0)
        {
            if (!_avisadoSinMicro) Notifications.Show("Chat de voz: no se encuentra ningun microfono");
            _avisadoSinMicro = true;
            return;
        }

        _dispositivo = null;   // el predeterminado del sistema
        _micro = Microphone.Start(_dispositivo, true, 1, Frecuencia);
        _leido = 0;
    }

    private void PararMicro()
    {
        if (_micro == null) return;
        Microphone.End(_dispositivo);
        _micro = null;
        _transmitiendo = false;
    }

    // Saca del microfono los trozos completos que haya. Si no se esta hablando se
    // descartan (hay que seguir leyendo para no enviar despues audio viejo).
    private void BombearMicro(bool enviar)
    {
        int posicion = Microphone.GetPosition(_dispositivo);
        int total = _micro.samples;
        int disponibles = (posicion - _leido + total) % total;

        while (disponibles >= MuestrasPorTrozo)
        {
            _micro.GetData(_muestras, _leido);
            _leido = (_leido + MuestrasPorTrozo) % total;
            disponibles -= MuestrasPorTrozo;

            if (!enviar) continue;

            byte[] datos = new byte[MuestrasPorTrozo];
            for (int i = 0; i < MuestrasPorTrozo; i++) datos[i] = Comprimir(_muestras[i]);

            EnviarVozServerRpc(datos);
            if (oirmeAMiMismo) Reproducir(NetworkManager.LocalClientId, datos);
        }
    }

    // ---------- Red ----------

    [ServerRpc(RequireOwnership = false, Delivery = RpcDelivery.Unreliable)]
    private void EnviarVozServerRpc(byte[] datos, ServerRpcParams rpc = default)
    {
        if (datos == null || datos.Length == 0 || datos.Length > MuestrasPorTrozo) return;
        RecibirVozClientRpc(rpc.Receive.SenderClientId, datos);
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void RecibirVozClientRpc(ulong quien, byte[] datos)
    {
        if (quien == NetworkManager.LocalClientId) return;   // la mia no me la devuelvo
        if (!GameSettings.VoiceEnabled) return;
        Reproducir(quien, datos);
    }

    private void Reproducir(ulong quien, byte[] datos)
    {
        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null || player.OwnerClientId != quien) continue;
            if (player.GetComponent<NetworkPlayer>() == null) continue;   // un NPC no habla

            var voz = player.GetComponent<VozDeJugador>();
            if (voz == null) voz = player.gameObject.AddComponent<VozDeJugador>();
            voz.Recibir(datos);
            _ultimaVoz[quien] = Time.unscaledTime;
            return;
        }
    }

    // ---------- Oyente ----------

    private AudioListener _oyente;

    // Las voces son AudioSource de Unity y necesitan un AudioListener en la cabeza
    // del jugador local. Wwise quita el de la camara principal para poner el suyo,
    // asi que el nuestro va en un hijo aparte y los dos conviven.
    private void AsegurarOyente()
    {
        var yo = NetworkPlayer.LocalPlayer;
        if (yo == null || _oyente != null) return;

        Camera camara = yo.GetComponentInChildren<Camera>();
        if (camara == null) return;

        // Si ya hay alguno activo (la camara del menu, por ejemplo) no puede haber dos
        foreach (var otro in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (otro != null && otro.enabled) otro.enabled = false;

        var go = new GameObject("OyenteVoz");
        go.transform.SetParent(camara.transform, false);
        _oyente = go.AddComponent<AudioListener>();
    }

    // ---------- Compresion (ley mu: 8 bits por muestra, la de la telefonia) ----------

    public static byte Comprimir(float muestra)
    {
        float x = Mathf.Clamp(muestra, -1f, 1f);
        float y = Mathf.Sign(x) * Mathf.Log(1f + 255f * Mathf.Abs(x)) / Mathf.Log(256f);
        return (byte)Mathf.RoundToInt((y + 1f) * 127.5f);
    }

    public static float Descomprimir(byte dato)
    {
        float y = dato / 127.5f - 1f;
        return Mathf.Sign(y) * (Mathf.Pow(256f, Mathf.Abs(y)) - 1f) / 255f;
    }

    // ---------- Indicador en pantalla ----------

    private const float ReferenceHeight = 1080f;

    void OnGUI()
    {
        if (!IsSpawned || Event.current.type != EventType.Repaint) return;

        string texto = "";
        if (_transmitiendo) texto += "<color=#7CFF8A><b>● HABLANDO</b></color>\n";

        foreach (var par in _ultimaVoz)
        {
            if (Time.unscaledTime - par.Value > 0.4f) continue;
            if (par.Key == NetworkManager.LocalClientId) continue;
            texto += "<color=#ffd27a>♪ " + NombreDe(par.Key) + "</color>\n";
        }
        if (texto.Length == 0) return;

        Matrix4x4 previous = GUI.matrix;
        float scale = Screen.height / ReferenceHeight;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        var estilo = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 18, alignment = TextAnchor.UpperLeft };
        Rect zona = new Rect(24f, ReferenceHeight - 252f, 400f, 120f);   // entre el chat y el minimapa

        Color antes = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(zona.x + 2f, zona.y + 2f, zona.width, zona.height),
                  System.Text.RegularExpressions.Regex.Replace(texto, "</?color[^>]*>", ""), estilo);
        GUI.color = antes;
        GUI.Label(zona, texto, estilo);

        GUI.matrix = previous;
    }

    private static string NombreDe(ulong cliente)
    {
        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null || player.OwnerClientId != cliente) continue;
            var nombre = player.GetComponent<PlayerName>();
            if (nombre != null) return nombre.Name;
        }
        return "Jugador";
    }
}
