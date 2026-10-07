using UnityEngine;

// Ayuda para quien juega por primera vez: nadie le habia dicho que tecla hace que.
//
//   - Al empezar cada partida van saliendo, de uno en uno, los controles
//     principales (se puede quitar en Opciones > Controles).
//   - Manteniendo F1 se ve la lista completa en cualquier momento.
//
// Lee las teclas de GameSettings, asi que si el jugador las cambia la ayuda lo
// refleja. Se crea sola al arrancar el juego; no hay que ponerla en la escena.
public class GuiaJugador : MonoBehaviour
{
    public KeyCode teclaAyuda = KeyCode.F1;
    [Tooltip("Segundos que se queda cada consejo al empezar la partida")]
    public float segundosPorConsejo = 5f;

    private bool _enJuegoAntes;
    private float _empezo = -999f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CrearAlArrancar()
    {
        if (FindFirstObjectByType<GuiaJugador>() != null) return;
        var go = new GameObject("GuiaJugador");
        DontDestroyOnLoad(go);
        go.AddComponent<GuiaJugador>();
    }

    // El nombre del control en lo que el jugador este usando: tecla o boton del mando
    private static string N(Accion a) => Controles.Nombre(a);

    private static string[] Consejos()
    {
        bool mando = Controles.UsandoMando;
        return new[]
        {
            "Moverte: <b>" + (mando ? "stick izquierdo" : "W A S D") + "</b>      Correr: <b>" + N(Accion.Correr) +
                "</b>      Agacharte: <b>" + N(Accion.Agacharse) + "</b>      Saltar: <b>" + N(Accion.Saltar) + "</b>",
            "Disparar: <b>" + N(Accion.Disparar) + "</b>      Apuntar: <b>" + N(Accion.Apuntar) +
                "</b>      Recargar: <b>" + N(Accion.Recargar) + "</b>",
            "Golpe cuerpo a cuerpo: <b>" + N(Accion.Golpe) + "</b>  (no gasta balas)      Linterna: <b>" + N(Accion.Linterna) + "</b>",
            "Inventario: <b>" + N(Accion.Inventario) + "</b>      Usar objeto: <b>" + N(Accion.Usar) +
                "</b>      Soltarlo: <b>" + N(Accion.Soltar) + "</b>",
            "Marcar algo para el equipo: <b>" + N(Accion.Marcar) + "</b>      Hablar: mantener <b>" + N(Accion.Voz) + "</b>" +
                (mando ? "" : "      Chat: <b>" + N(Accion.Chat) + "</b>"),
            "Levantar a un companero caido: mantener <b>" + N(Accion.Interactuar) +
                "</b> junto a el      Todos los controles: " + (mando ? "en Opciones > Controles" : "mantener <b>F1</b>"),
        };
    }

    private static string[,] Lista()
    {
        bool mando = Controles.UsandoMando;
        int n = Controles.Cuantas;
        var lista = new string[n + 3, 2];
        lista[0, 0] = "Moverse"; lista[0, 1] = mando ? "Stick izquierdo" : "W A S D";
        lista[1, 0] = "Mirar"; lista[1, 1] = mando ? "Stick derecho" : "Raton";
        for (int i = 0; i < n; i++)
        {
            lista[i + 2, 0] = Controles.Etiqueta((Accion)i);
            lista[i + 2, 1] = N((Accion)i);
        }
        lista[n + 2, 0] = "Menu de pausa"; lista[n + 2, 1] = mando ? Controles.NombreBoton(BotonMando.Start) : "Escape";
        return lista;
    }

    void Update()
    {
        bool enJuego = NetworkPlayer.LocalPlayer != null && SalaEspera.EnJuego;
        if (enJuego && !_enJuegoAntes) _empezo = Time.unscaledTime;   // acaba de empezar la partida
        _enJuegoAntes = enJuego;
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (NetworkPlayer.LocalPlayer == null || !SalaEspera.EnJuego) return;
        if (MatchManager.Instance != null && MatchManager.Instance.MatchOver) return;

        float escala = Screen.height / 1080f;
        Matrix4x4 antesM = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(escala, escala, 1f));
        float ancho = 1080f * ((float)Screen.width / Screen.height);

        if (Input.GetKey(teclaAyuda) && !UIState.ChatOpen) DibujarLista(ancho);
        else if (GameSettings.ShowTips && !UIState.BlocksGameplay) DibujarConsejo(ancho);

        GUI.matrix = antesM;
    }

    private void DibujarConsejo(float ancho)
    {
        string[] consejos = Consejos();
        float t = Time.unscaledTime - _empezo;
        int indice = Mathf.FloorToInt(t / segundosPorConsejo);
        if (t < 0f || indice >= consejos.Length) return;

        // Entra y sale con un fundido corto
        float dentro = t - indice * segundosPorConsejo;
        float alfa = Mathf.Clamp01(dentro / 0.4f) * Mathf.Clamp01((segundosPorConsejo - dentro) / 0.4f);

        var estilo = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 20, alignment = TextAnchor.MiddleCenter };
        Rect caja = new Rect(ancho / 2f - 520f, 1080f - 250f, 1040f, 44f);

        Color antes = GUI.color;
        GUI.color = new Color(0.05f, 0.06f, 0.09f, 0.8f * alfa);
        GUI.DrawTexture(caja, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, alfa);
        GUI.Label(caja, consejos[indice], estilo);
        GUI.color = antes;
    }

    private void DibujarLista(float ancho)
    {
        string[,] lista = Lista();
        int filas = lista.GetLength(0);
        float w = 720f, h = 70f + filas * 30f;
        Rect caja = new Rect((ancho - w) / 2f, (1080f - h) / 2f, w, h);

        Color antes = GUI.color;
        GUI.color = new Color(0.05f, 0.06f, 0.09f, 0.92f);
        GUI.DrawTexture(caja, Texture2D.whiteTexture);
        GUI.color = new Color(0.55f, 0.12f, 0.12f, 1f);
        GUI.DrawTexture(new Rect(caja.x, caja.y, caja.width, 4f), Texture2D.whiteTexture);
        GUI.color = antes;

        var titulo = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 24, alignment = TextAnchor.MiddleCenter };
        var izq = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 18, alignment = TextAnchor.MiddleLeft };
        var der = new GUIStyle(izq) { alignment = TextAnchor.MiddleRight };

        GUI.Label(new Rect(caja.x, caja.y + 12f, w, 36f), "<b>CONTROLES</b>", titulo);
        for (int i = 0; i < filas; i++)
        {
            float y = caja.y + 58f + i * 30f;
            GUI.Label(new Rect(caja.x + 36f, y, w * 0.6f, 28f), lista[i, 0], izq);
            GUI.Label(new Rect(caja.x + w * 0.5f, y, w * 0.5f - 36f, 28f), "<color=#ffd27a><b>" + lista[i, 1] + "</b></color>", der);
        }
    }
}
