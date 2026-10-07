using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

// Lleva la cuenta de lo que hace cada jugador durante la partida (bajas, veces
// que cae, companeros que levanta) y, cuando termina, reparte el resumen a todos
// para la pantalla de resultados.
//
// La cuenta la lleva SOLO el servidor. El resumen viaja una vez, al acabar, y se
// queda guardado en ResumenPartida, que sobrevive al cambio de escena: por eso se
// puede ensenar tambien despues del video final, cuando la red ya esta apagada.
//
// Va en el mismo objeto que MatchManager (tiene NetworkObject).
public class EstadisticasPartida : NetworkBehaviour
{
    public static EstadisticasPartida Instance { get; private set; }

    private class Fila
    {
        public string nombre = "Jugador";
        public int bajas, caidas, reanimaciones;
    }

    private readonly Dictionary<ulong, Fila> _filas = new Dictionary<ulong, Fila>();
    private float _inicio = -1f;
    private bool _publicado;

    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        _filas.Clear();
        _inicio = -1f;
        _publicado = false;
    }

    void Update()
    {
        // El reloj empieza cuando empieza la partida, no en la sala de espera
        if (IsServer && _inicio < 0f && SalaEspera.EnJuego && NetworkPlayer.AllPlayers.Count > 0)
            _inicio = Time.time;
    }

    private Fila De(ulong cliente)
    {
        if (!_filas.TryGetValue(cliente, out var fila)) _filas[cliente] = fila = new Fila();

        // El nombre se refresca mientras el jugador siga en la partida: si se va
        // antes del final, se conserva el ultimo que tuvo
        foreach (var p in NetworkPlayer.AllPlayers)
        {
            if (p == null || p.OwnerClientId != cliente) continue;
            var n = p.GetComponent<PlayerName>();
            if (n != null) fila.nombre = n.Name;
            break;
        }
        return fila;
    }

    private static bool Activa => Instance != null && Instance.IsSpawned && Instance.IsServer;

    public static void SumarBaja(ulong cliente)
    {
        if (Activa && cliente != ulong.MaxValue) Instance.De(cliente).bajas++;
    }

    public static void SumarCaida(ulong cliente)
    {
        if (Activa) Instance.De(cliente).caidas++;
    }

    public static void SumarReanimacion(ulong cliente)
    {
        if (Activa) Instance.De(cliente).reanimaciones++;
    }

    /// <summary>Al reiniciar la partida se empieza la cuenta de cero.</summary>
    public void Reiniciar()
    {
        if (!IsServer) return;
        _filas.Clear();
        _inicio = SalaEspera.EnJuego ? Time.time : -1f;
        _publicado = false;
    }

    /// <summary>Fin de la partida: manda el resumen a todos. Solo servidor, una vez por partida.</summary>
    public void Publicar(bool victoria, string motivo)
    {
        if (!IsServer || _publicado) return;
        _publicado = true;

        // Que salgan todos los que siguen dentro, aunque no hayan hecho nada
        foreach (var p in NetworkPlayer.AllPlayers)
            if (p != null && p.GetComponent<NetworkPlayer>() != null) De(p.OwnerClientId);

        // Una linea por jugador: nombre|bajas|caidas|reanimaciones
        var sb = new StringBuilder();
        foreach (var par in _filas)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(par.Value.nombre.Replace("|", " ").Replace("\n", " ")).Append('|')
              .Append(par.Value.bajas).Append('|').Append(par.Value.caidas).Append('|').Append(par.Value.reanimaciones);
        }

        float duracion = _inicio >= 0f ? Time.time - _inicio : 0f;
        int vidas = MatchManager.Instance != null ? MatchManager.Instance.Lives : 0;
        ResumenClientRpc(victoria, motivo ?? "", duracion, vidas, sb.ToString());
    }

    [ClientRpc]
    private void ResumenClientRpc(bool victoria, string motivo, float duracion, int vidas, string filas)
    {
        ResumenPartida.Guardar(victoria, motivo, duracion, vidas, filas);
    }
}

// El resumen de la ultima partida, guardado en esta maquina. Es estatico a
// proposito: tiene que seguir ahi tras cargar la escena del final.
public static class ResumenPartida
{
    public struct Fila
    {
        public string nombre;
        public int bajas, caidas, reanimaciones;
    }

    public static bool Hay { get; private set; }
    public static bool Victoria { get; private set; }
    public static string Motivo { get; private set; } = "";
    public static float Duracion { get; private set; }
    public static int Vidas { get; private set; }
    public static readonly List<Fila> Filas = new List<Fila>();

    public static void Borrar()
    {
        Hay = false;
        Filas.Clear();
    }

    public static void Guardar(bool victoria, string motivo, float duracion, int vidas, string filas)
    {
        Hay = true;
        Victoria = victoria;
        Motivo = motivo ?? "";
        Duracion = duracion;
        Vidas = vidas;

        Filas.Clear();
        foreach (string linea in (filas ?? "").Split('\n'))
        {
            string[] p = linea.Split('|');
            if (p.Length < 4) continue;
            int.TryParse(p[1], out int b);
            int.TryParse(p[2], out int c);
            int.TryParse(p[3], out int r);
            Filas.Add(new Fila { nombre = p[0], bajas = b, caidas = c, reanimaciones = r });
        }

        // Primero quien mas bajas lleva
        Filas.Sort((x, y) => y.bajas.CompareTo(x.bajas));
    }

    /// <summary>Alto que ocupa la tabla (en las mismas unidades que se le pasen a Dibujar).</summary>
    public static float Alto => 96f + Filas.Count * 30f;

    // Tabla de resultados dentro de 'zona'. Se llama desde un OnGUI ya escalado.
    public static void Dibujar(Rect zona)
    {
        if (!Hay) return;

        var titulo = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 18, alignment = TextAnchor.MiddleCenter };
        var celda = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 18, alignment = TextAnchor.MiddleLeft };
        var numero = new GUIStyle(celda) { alignment = TextAnchor.MiddleCenter };

        int min = Mathf.FloorToInt(Duracion / 60f), seg = Mathf.FloorToInt(Duracion % 60f);
        GUI.Label(new Rect(zona.x, zona.y, zona.width, 26f),
            "<color=#c8ccd2>Tiempo: <b>" + min.ToString("00") + ":" + seg.ToString("00") +
            "</b>      Vidas de equipo que quedaban: <b>" + Vidas + "</b></color>", titulo);

        float y = zona.y + 40f;
        float cNombre = zona.width * 0.40f, cNum = zona.width * 0.20f;

        Color antes = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.12f);
        GUI.DrawTexture(new Rect(zona.x, y, zona.width, 28f), Texture2D.whiteTexture);
        GUI.color = antes;

        GUI.Label(new Rect(zona.x + 12f, y, cNombre, 28f), "<color=#ffd27a><b>JUGADOR</b></color>", celda);
        GUI.Label(new Rect(zona.x + cNombre, y, cNum, 28f), "<color=#ffd27a><b>BAJAS</b></color>", numero);
        GUI.Label(new Rect(zona.x + cNombre + cNum, y, cNum, 28f), "<color=#ffd27a><b>CAIDAS</b></color>", numero);
        GUI.Label(new Rect(zona.x + cNombre + cNum * 2f, y, cNum, 28f), "<color=#ffd27a><b>REANIMA</b></color>", numero);
        y += 30f;

        foreach (var f in Filas)
        {
            GUI.Label(new Rect(zona.x + 12f, y, cNombre, 28f), f.nombre, celda);
            GUI.Label(new Rect(zona.x + cNombre, y, cNum, 28f), f.bajas.ToString(), numero);
            GUI.Label(new Rect(zona.x + cNombre + cNum, y, cNum, 28f), f.caidas.ToString(), numero);
            GUI.Label(new Rect(zona.x + cNombre + cNum * 2f, y, cNum, 28f), f.reanimaciones.ToString(), numero);
            y += 30f;
        }
    }
}
