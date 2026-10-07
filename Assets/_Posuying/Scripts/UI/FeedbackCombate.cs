using System.Collections.Generic;
using UnityEngine;

// Lo que te dice la pantalla durante el combate:
//   - AL ACERTAR: marca en la reticula (roja si el tiro mata) y el dano en un
//     numero que sube desde el punto de impacto.
//   - AL RECIBIR: fogonazo rojo en los bordes y una flecha hacia quien te ha pegado.
//   - CON POCA VIDA: los bordes se quedan en rojo, latiendo.
//
// Es solo visual y de cada jugador: nada de esto viaja por la red. Se crea solo
// al arrancar el juego, no hay que ponerlo en ninguna escena.
public class FeedbackCombate : MonoBehaviour
{
    private static FeedbackCombate _instancia;

    [Header("Al acertar")]
    public float segundosMarca = 0.22f;
    public float segundosNumero = 0.8f;

    [Header("Al recibir dano")]
    [Tooltip("Hasta que distancia se busca al enemigo que te ha pegado para senalarlo")]
    public float radioAtacante = 6f;
    public float segundosFlecha = 1.2f;

    [Header("Poca vida")]
    [Range(0f, 1f)] public float umbralPocaVida = 0.3f;

    private struct Numero
    {
        public Vector3 punto;
        public string texto;
        public float nace;
        public bool muerte;
    }

    private readonly List<Numero> _numeros = new List<Numero>();
    private float _marcaHasta;
    private bool _marcaMuerte;

    private Character _jugador;
    private float _vidaAnterior = -1f;
    private float _fogonazo;            // 0..1, se va apagando
    private Vector3 _atacante;
    private float _flechaHasta;

    private static Texture2D _bordes, _flecha;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CrearAlArrancar()
    {
        if (_instancia != null) return;
        var go = new GameObject("FeedbackCombate");
        DontDestroyOnLoad(go);
        _instancia = go.AddComponent<FeedbackCombate>();
    }

    /// <summary>Lo llama el arma del jugador local cuando su disparo da a un enemigo.</summary>
    public static void Impacto(Vector3 punto, float dano, bool muerte)
    {
        if (_instancia == null) return;
        _instancia._marcaHasta = Time.unscaledTime + _instancia.segundosMarca * (muerte ? 1.6f : 1f);
        _instancia._marcaMuerte = muerte;
        _instancia._numeros.Add(new Numero
        {
            punto = punto, texto = Mathf.RoundToInt(dano).ToString(), nace = Time.unscaledTime, muerte = muerte
        });
        if (_instancia._numeros.Count > 24) _instancia._numeros.RemoveAt(0);
    }

    void Update()
    {
        var actual = NetworkPlayer.LocalPlayer;
        if (actual != _jugador) { _jugador = actual; _vidaAnterior = -1f; _fogonazo = 0f; }
        if (_jugador == null) return;

        float vida = _jugador.GetHealth();
        if (_vidaAnterior >= 0f && vida < _vidaAnterior - 0.01f)
        {
            float perdida = (_vidaAnterior - vida) / Mathf.Max(1f, _jugador.GetMaxHealth());
            // Un golpe flojo tambien tiene que notarse: minimo un 35 % de fogonazo
            _fogonazo = Mathf.Clamp01(Mathf.Max(_fogonazo, 0.5f + perdida * 3f));
            BuscarAtacante();
        }
        _vidaAnterior = vida;

        _fogonazo = Mathf.MoveTowards(_fogonazo, 0f, Time.unscaledDeltaTime * 1.6f);
    }

    // El servidor no dice quien pega, asi que se senala al enemigo vivo mas cercano.
    // Casi siempre acierta: el dano cuerpo a cuerpo es por contacto.
    private void BuscarAtacante()
    {
        float mejor = radioAtacante * radioAtacante;
        bool hay = false;
        foreach (var enemigo in FindObjectsByType<EnemyBehaviour>(FindObjectsSortMode.None))
        {
            if (enemigo == null || enemigo.IsDead) continue;
            float d = (enemigo.transform.position - _jugador.transform.position).sqrMagnitude;
            if (d < mejor) { mejor = d; _atacante = enemigo.transform.position; hay = true; }
        }
        if (hay) _flechaHasta = Time.unscaledTime + segundosFlecha;
    }

    void OnGUI()
    {
        if (_jugador == null || Event.current.type != EventType.Repaint) return;
        if (MatchManager.Instance != null && MatchManager.Instance.MatchOver) return;
        if (UIState.NetMenuOpen) return;   // con el menu abierto se pintaba encima de el

        Camera camara = _jugador.GetComponentInChildren<Camera>();
        if (camara == null || !camara.enabled) camara = Camera.main;

        DibujarBordes();
        if (camara != null)
        {
            DibujarFlecha(camara);
            DibujarNumeros(camara);
        }
        DibujarMarca();
    }

    // ---------- Bordes rojos ----------

    private void DibujarBordes()
    {
        float alfa = _fogonazo * 0.9f;

        var estado = _jugador.GetComponent<PlayerDownedState>();
        bool enPie = estado == null || estado.CanAct;
        float fraccion = _jugador.GetHealth() / Mathf.Max(1f, _jugador.GetMaxHealth());
        if (enPie && fraccion < umbralPocaVida)
        {
            // Late mas fuerte cuanta menos vida queda
            float gravedad = 1f - fraccion / umbralPocaVida;
            float latido = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (3f + gravedad * 3f));
            alfa = Mathf.Max(alfa, (0.25f + 0.3f * gravedad) * (0.6f + 0.4f * latido));
        }
        if (alfa <= 0.01f) return;

        if (_bordes == null) _bordes = CrearBordes(128);
        Color antes = GUI.color;
        GUI.color = new Color(0.75f, 0f, 0f, alfa);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bordes, ScaleMode.StretchToFill);
        GUI.color = antes;
    }

    // ---------- Flecha hacia el atacante ----------

    private void DibujarFlecha(Camera camara)
    {
        float queda = _flechaHasta - Time.unscaledTime;
        if (queda <= 0f) return;

        // Angulo en el plano del suelo entre hacia donde miras y el atacante:
        // 0 = delante, 90 = a la derecha, 180 = detras
        Vector3 hacia = _atacante - camara.transform.position; hacia.y = 0f;
        Vector3 frente = camara.transform.forward; frente.y = 0f;
        if (hacia.sqrMagnitude < 0.01f || frente.sqrMagnitude < 0.01f) return;
        float angulo = Vector3.SignedAngle(frente, hacia, Vector3.up);

        if (_flecha == null) _flecha = CrearFlecha(64);
        Vector2 centro = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float radio = Screen.height * 0.17f, lado = Screen.height * 0.06f;

        Matrix4x4 antesM = GUI.matrix;
        Color antes = GUI.color;
        GUIUtility.RotateAroundPivot(angulo, centro);
        GUI.color = new Color(1f, 0.15f, 0.1f, Mathf.Clamp01(queda / 0.4f) * 0.9f);
        GUI.DrawTexture(new Rect(centro.x - lado * 0.5f, centro.y - radio - lado, lado, lado), _flecha);
        GUI.color = antes;
        GUI.matrix = antesM;
    }

    // ---------- Numeros de dano ----------

    private void DibujarNumeros(Camera camara)
    {
        if (_numeros.Count == 0) return;
        var estilo = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

        for (int i = _numeros.Count - 1; i >= 0; i--)
        {
            float t = (Time.unscaledTime - _numeros[i].nace) / segundosNumero;
            if (t >= 1f) { _numeros.RemoveAt(i); continue; }

            Vector3 p = camara.WorldToScreenPoint(_numeros[i].punto);
            if (p.z <= 0f) continue;

            float x = p.x, y = Screen.height - p.y - t * Screen.height * 0.07f;
            estilo.fontSize = Mathf.RoundToInt(Screen.height / (_numeros[i].muerte ? 30f : 38f));
            float alfa = 1f - t * t;

            Rect zona = new Rect(x - 60f, y - 20f, 120f, 40f);
            Color antes = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alfa * 0.8f);
            GUI.Label(new Rect(zona.x + 2f, zona.y + 2f, zona.width, zona.height), _numeros[i].texto, estilo);
            GUI.color = _numeros[i].muerte ? new Color(1f, 0.3f, 0.25f, alfa) : new Color(1f, 0.95f, 0.7f, alfa);
            GUI.Label(zona, _numeros[i].texto, estilo);
            GUI.color = antes;
        }
    }

    // ---------- Marca de impacto en la reticula ----------

    private void DibujarMarca()
    {
        float queda = _marcaHasta - Time.unscaledTime;
        if (queda <= 0f) return;

        Vector2 centro = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float largo = Screen.height * 0.016f, grosor = Mathf.Max(2f, Screen.height / 400f), hueco = Screen.height * 0.012f;

        Matrix4x4 antesM = GUI.matrix;
        Color antes = GUI.color;
        GUI.color = _marcaMuerte ? new Color(1f, 0.2f, 0.15f, Mathf.Clamp01(queda / 0.1f))
                                 : new Color(1f, 1f, 1f, Mathf.Clamp01(queda / 0.1f));
        // Un aspa: cuatro rayas en diagonal que dejan libre el centro
        for (int i = 0; i < 4; i++)
        {
            GUIUtility.RotateAroundPivot(45f + i * 90f, centro);
            GUI.DrawTexture(new Rect(centro.x - grosor * 0.5f, centro.y - hueco - largo, grosor, largo), Texture2D.whiteTexture);
            GUI.matrix = antesM;
        }
        GUI.color = antes;
    }

    // ---------- Texturas hechas por codigo ----------

    // Blanco con alfa: transparente en el centro y opaco hacia los bordes
    private static Texture2D CrearBordes(int lado)
    {
        var tex = new Texture2D(lado, lado, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[lado * lado];
        for (int y = 0; y < lado; y++)
            for (int x = 0; x < lado; x++)
            {
                float dx = (x / (lado - 1f) - 0.5f) * 2f, dy = (y / (lado - 1f) - 0.5f) * 2f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;   // 0 centro, 1 esquina
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
                px[y * lado + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }

    // Triangulo apuntando hacia arriba
    private static Texture2D CrearFlecha(int lado)
    {
        var tex = new Texture2D(lado, lado, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[lado * lado];
        for (int y = 0; y < lado; y++)
            for (int x = 0; x < lado; x++)
            {
                float v = y / (lado - 1f);                        // 0 abajo, 1 arriba (la punta)
                float mitad = (1f - v) * 0.5f;                    // media anchura a esa altura
                float dx = Mathf.Abs(x / (lado - 1f) - 0.5f);
                float a = Mathf.Clamp01((mitad - dx) * lado * 0.5f);
                px[y * lado + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }
}
