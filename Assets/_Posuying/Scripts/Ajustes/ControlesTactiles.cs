using System.Collections.Generic;
using UnityEngine;

// Controles en pantalla para movil (y para un navegador de movil).
//
//   - MITAD IZQUIERDA: donde pones el dedo aparece un joystick para andar.
//     Empujado hasta el borde, corres.
//   - MITAD DERECHA: arrastrar el dedo gira la camara. El boton de disparo
//     tambien gira la camara mientras lo arrastras (disparar y apuntar a la vez).
//   - BOTONES: disparar, apuntar, recargar, saltar, agacharse, golpe, linterna,
//     interactuar, y arriba inventario, usar, marcar, voz, chat y pausa.
//
// No sustituye a nada: rellena lo que Controles devuelve (Pulsado, Mantenido,
// Mover, Mirar), asi que el resto del juego no sabe si juegas con teclado, mando
// o dedos. Se crea solo y solo se ve cuando Plataforma.Tactil.
//
// Para probarlo en el editor: Plataforma.ForzarTactil = true y el raton hace de dedo.
[DefaultExecutionOrder(-1000)]   // antes que nadie lea los controles en este frame
public class ControlesTactiles : MonoBehaviour
{
    private static ControlesTactiles _instancia;

    /// <summary>Se estan mostrando y leyendo los controles en pantalla.</summary>
    public static bool Activos { get; private set; }

    /// <summary>Se ha pulsado el boton de pausa este frame.</summary>
    public static bool PausaPulsada { get; private set; }

    [Tooltip("Cuanto gira la camara al arrastrar el dedo (se multiplica por la sensibilidad de Opciones)")]
    public float sensibilidadDedo = 0.16f;
    [Range(0.3f, 1f)] public float opacidad = 0.55f;

    // ---------- Lo que lee Controles ----------

    private static readonly int N = System.Enum.GetValues(typeof(Accion)).Length;
    private static readonly bool[] _mantenido = new bool[N];
    private static readonly bool[] _pulsado = new bool[N];
    private static Vector2 _mover, _mirar;

    public static bool Pulsado(Accion a) => Activos && _pulsado[(int)a];
    public static bool Mantenido(Accion a) => Activos && (_mantenido[(int)a] || (a == Accion.Correr && _mover.magnitude > 0.95f));
    public static Vector2 Mover => Activos ? _mover : Vector2.zero;
    public static Vector2 Mirar => Activos ? _mirar : Vector2.zero;

    // ---------- Botones ----------

    private class Boton
    {
        public Accion accion;
        public string texto;
        public bool redondo;          // redondo abajo a la derecha, o pastilla arriba
        public Vector2 desdeEsquina;  // redondos: desde la esquina inferior derecha; pastillas: hueco en la fila
        public float radio;
        public bool giraCamara;       // arrastrarlo tambien mueve la camara
        public bool siempre;          // se ve aunque haya un menu abierto (inventario)
        public Rect zona;
        public int dedo = SinDedo;
    }

    private const int SinDedo = int.MinValue;
    private const int PausaId = -1;

    private readonly List<Boton> _botones = new List<Boton>
    {
        Redondo(Accion.Disparar, "FUEGO", 230, 450, 120, true),
        Redondo(Accion.Apuntar, "APUNTAR", 470, 340, 75),
        Redondo(Accion.Recargar, "RECARGAR", 230, 690, 70),
        Redondo(Accion.Saltar, "SALTAR", 100, 260, 75),
        Redondo(Accion.Agacharse, "AGACHAR", 300, 230, 65),
        Redondo(Accion.Golpe, "GOLPE", 440, 560, 65),
        Redondo(Accion.Linterna, "LUZ", 100, 640, 60),
        Redondo(Accion.Interactuar, "USAR\nPUERTA", 420, 780, 65),

        Pastilla(Accion.Inventario, "INVENTARIO", 0, true),
        Pastilla(Accion.Usar, "USAR OBJETO", 1),
        Pastilla(Accion.Marcar, "MARCAR", 2),
        Pastilla(Accion.Voz, "HABLAR", 3),
        Pastilla(Accion.Chat, "CHAT", 4),

        Flecha(Accion.HuecoAnterior, "<", -1),
        Flecha(Accion.HuecoSiguiente, ">", 1),
    };

    private static Boton Redondo(Accion a, string t, float x, float y, float r, bool camara = false) =>
        new Boton { accion = a, texto = t, redondo = true, desdeEsquina = new Vector2(x, y), radio = r, giraCamara = camara };

    private static Boton Pastilla(Accion a, string t, int hueco, bool siempre = false) =>
        new Boton { accion = a, texto = t, desdeEsquina = new Vector2(hueco, 0), siempre = siempre };

    private static Boton Flecha(Accion a, string t, int lado) =>
        new Boton { accion = a, texto = t, desdeEsquina = new Vector2(lado, -1) };

    // ---------- Dedos ----------

    private int _dedoJoystick = SinDedo, _dedoMirar = SinDedo, _dedoPausa = SinDedo;
    private Vector2 _centroJoystick, _puntaJoystick;
    private Rect _zonaPausa;
    private readonly Dictionary<int, Vector2> _ultimaPos = new Dictionary<int, Vector2>();

    private struct Dedo { public int id; public Vector2 pos; public TouchPhase fase; }
    private readonly List<Dedo> _dedos = new List<Dedo>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Crear()
    {
        if (_instancia != null) return;
        var go = new GameObject("ControlesTactiles");
        DontDestroyOnLoad(go);
        _instancia = go.AddComponent<ControlesTactiles>();
    }

    void Update()
    {
        System.Array.Clear(_pulsado, 0, N);
        PausaPulsada = false;
        _mirar = Vector2.zero;

        // En el navegador no siempre se sabe de antemano si es un movil o tableta (iPad)
        if (Plataforma.EsWeb && Input.touchCount > 0) Plataforma.HanTocado();

        bool enPartida = Plataforma.Tactil && NetworkPlayer.LocalPlayer != null && SalaEspera.EnJuego &&
                         !(MatchManager.Instance != null && MatchManager.Instance.MatchOver);
        bool jugando = enPartida && !UIState.BlocksGameplay;

        if (!enPartida)
        {
            if (Activos) SoltarTodo();
            Activos = false;
            return;
        }
        Activos = true;

        Colocar();
        LeerDedos();

        foreach (var d in _dedos)
        {
            switch (d.fase)
            {
                case TouchPhase.Began: Empieza(d, jugando); break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary: Sigue(d); break;
                default: Acaba(d.id); break;
            }
            _ultimaPos[d.id] = d.pos;
        }

        // Con un menu abierto solo valen los que se ven siempre: se suelta el resto
        if (!jugando)
        {
            foreach (var b in _botones)
                if (!b.siempre && b.dedo != SinDedo) { b.dedo = SinDedo; _mantenido[(int)b.accion] = false; }
            _dedoJoystick = _dedoMirar = SinDedo;
            _mover = Vector2.zero;
        }
    }

    // Toques de verdad o, para probar en el PC, el raton como si fuera un dedo.
    // Coordenadas de GUI: (0,0) arriba a la izquierda.
    private void LeerDedos()
    {
        _dedos.Clear();
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            _dedos.Add(new Dedo { id = t.fingerId, pos = new Vector2(t.position.x, Screen.height - t.position.y), fase = t.phase });
        }

        if (Input.touchCount == 0 && Plataforma.ForzarTactil)
        {
            Vector2 p = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (Input.GetMouseButtonDown(0)) _dedos.Add(new Dedo { id = 99, pos = p, fase = TouchPhase.Began });
            else if (Input.GetMouseButton(0)) _dedos.Add(new Dedo { id = 99, pos = p, fase = TouchPhase.Moved });
            else if (Input.GetMouseButtonUp(0)) _dedos.Add(new Dedo { id = 99, pos = p, fase = TouchPhase.Ended });
        }
    }

    private void Empieza(Dedo d, bool jugando)
    {
        if (_zonaPausa.Contains(d.pos)) { _dedoPausa = d.id; PausaPulsada = true; return; }

        foreach (var b in _botones)
        {
            if ((!jugando && !b.siempre) || b.dedo != SinDedo || !Dentro(b, d.pos)) continue;
            b.dedo = d.id;
            _pulsado[(int)b.accion] = true;
            _mantenido[(int)b.accion] = true;
            return;
        }

        if (!jugando) return;

        // Izquierda: joystick donde se apoya el dedo. Derecha: mirar
        if (d.pos.x < Screen.width * 0.4f && d.pos.y > Screen.height * 0.25f)
        {
            if (_dedoJoystick == SinDedo) { _dedoJoystick = d.id; _centroJoystick = _puntaJoystick = d.pos; }
        }
        else if (_dedoMirar == SinDedo) _dedoMirar = d.id;
    }

    private void Sigue(Dedo d)
    {
        Vector2 antes = _ultimaPos.TryGetValue(d.id, out Vector2 p) ? p : d.pos;
        Vector2 delta = d.pos - antes;

        if (d.id == _dedoJoystick)
        {
            float radio = 130f * Escala;
            Vector2 v = d.pos - _centroJoystick;
            if (v.magnitude > radio) v = v.normalized * radio;
            _puntaJoystick = _centroJoystick + v;
            _mover = new Vector2(v.x, -v.y) / radio;   // en pantalla Y crece hacia abajo
            return;
        }

        bool mira = d.id == _dedoMirar;
        foreach (var b in _botones) if (b.dedo == d.id && b.giraCamara) mira = true;

        if (mira)
        {
            // Normalizado por la altura: la misma sensacion en cualquier pantalla
            float k = sensibilidadDedo * (1080f / Mathf.Max(1, Screen.height)) * GameSettings.MouseSensitivity;
            _mirar += new Vector2(delta.x, -delta.y) * k;
        }
    }

    private void Acaba(int id)
    {
        _ultimaPos.Remove(id);
        if (id == _dedoPausa) _dedoPausa = SinDedo;
        if (id == _dedoMirar) _dedoMirar = SinDedo;
        if (id == _dedoJoystick) { _dedoJoystick = SinDedo; _mover = Vector2.zero; }
        foreach (var b in _botones)
            if (b.dedo == id) { b.dedo = SinDedo; _mantenido[(int)b.accion] = false; }
    }

    private void SoltarTodo()
    {
        foreach (var b in _botones) b.dedo = SinDedo;
        System.Array.Clear(_mantenido, 0, N);
        _dedoJoystick = _dedoMirar = _dedoPausa = SinDedo;
        _mover = Vector2.zero;
        _ultimaPos.Clear();
    }

    // ---------- Donde va cada cosa ----------

    private static float Escala => Mathf.Min(Screen.width, Screen.height) / 1080f;

    private void Colocar()
    {
        float s = Escala, w = Screen.width, h = Screen.height;
        float anchoPastilla = 170f * s, altoPastilla = 64f * s, hueco = 12f * s;

        foreach (var b in _botones)
        {
            if (b.redondo)
            {
                Vector2 c = new Vector2(w - b.desdeEsquina.x * s, h - b.desdeEsquina.y * s);
                float r = b.radio * s;
                b.zona = new Rect(c.x - r, c.y - r, r * 2f, r * 2f);
            }
            else if (b.desdeEsquina.y < 0f)
            {
                // Flechas a los lados de la barra de objetos (abajo en el centro)
                float lado = 84f * s;
                b.zona = new Rect(w * 0.5f + b.desdeEsquina.x * 250f * s - lado * 0.5f, h - 140f * s, lado, lado);
            }
            else
            {
                // Fila de arriba a la derecha, debajo del objetivo de la partida
                int i = (int)b.desdeEsquina.x;
                float x = w - 24f * s - (5 - i) * (anchoPastilla + hueco) + hueco;
                b.zona = new Rect(x, 130f * s, anchoPastilla, altoPastilla);
            }
        }
        _zonaPausa = new Rect(w - 24f * s - 110f * s, 30f * s, 110f * s, 80f * s);
    }

    private static bool Dentro(Boton b, Vector2 p)
    {
        if (!b.redondo) return b.zona.Contains(p);
        // Un poco mas grande que el dibujo: con el pulgar se falla por poco
        return (p - b.zona.center).sqrMagnitude <= Mathf.Pow(b.zona.width * 0.5f * 1.15f, 2f);
    }

    // ---------- Dibujo ----------

    private static Texture2D _circulo;

    void OnGUI()
    {
        if (!Activos || Event.current.type != EventType.Repaint) return;
        if (_circulo == null) _circulo = CrearCirculo(128);

        bool jugando = !UIState.BlocksGameplay;
        var estilo = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false,
            fontSize = Mathf.RoundToInt(24f * Escala)
        };
        Color antes = GUI.color;

        foreach (var b in _botones)
        {
            if (!jugando && !b.siempre) continue;
            bool apretado = b.dedo != SinDedo;
            float a = opacidad * (apretado ? 1f : 0.7f);

            GUI.color = new Color(0.05f, 0.06f, 0.08f, a * 0.75f);
            if (b.redondo) GUI.DrawTexture(b.zona, _circulo);
            else GUI.DrawTexture(b.zona, Texture2D.whiteTexture);

            // Borde: rojo si es el disparo, blanco el resto
            GUI.color = b.accion == Accion.Disparar ? new Color(0.9f, 0.3f, 0.25f, a) : new Color(1f, 1f, 1f, a * 0.6f);
            if (b.redondo) GUI.DrawTexture(b.zona, Anillo);
            else DibujarMarco(b.zona, Mathf.Max(2f, 3f * Escala));

            // El texto cabe entero: en los redondos pequenos se encoge en vez de partir la palabra
            int letras = 1;
            foreach (string linea in b.texto.Split('\n')) letras = Mathf.Max(letras, linea.Length);
            estilo.fontSize = Mathf.RoundToInt(Mathf.Min(24f * Escala, b.zona.width * 0.8f / (letras * 0.62f)));
            GUI.color = new Color(1f, 1f, 1f, Mathf.Min(1f, a + 0.25f));
            GUI.Label(b.zona, b.texto, estilo);
        }

        // Pausa (arriba a la derecha): tres rayas
        GUI.color = new Color(0.05f, 0.06f, 0.08f, opacidad * 0.6f);
        GUI.DrawTexture(_zonaPausa, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, opacidad + 0.2f);
        for (int i = 0; i < 3; i++)
            GUI.DrawTexture(new Rect(_zonaPausa.x + _zonaPausa.width * 0.28f, _zonaPausa.y + _zonaPausa.height * (0.3f + i * 0.2f),
                                     _zonaPausa.width * 0.44f, Mathf.Max(2f, 5f * Escala)), Texture2D.whiteTexture);

        // Joystick: solo mientras hay un dedo; si no, una pista de donde esta
        if (jugando)
        {
            float radio = 130f * Escala;
            Vector2 centro = _dedoJoystick != SinDedo ? _centroJoystick : new Vector2(300f * Escala, Screen.height - 330f * Escala);
            Vector2 punta = _dedoJoystick != SinDedo ? _puntaJoystick : centro;
            float a = _dedoJoystick != SinDedo ? opacidad : opacidad * 0.35f;

            GUI.color = new Color(1f, 1f, 1f, a * 0.5f);
            GUI.DrawTexture(new Rect(centro.x - radio, centro.y - radio, radio * 2f, radio * 2f), Anillo);
            GUI.color = new Color(1f, 1f, 1f, a * 0.8f);
            float r = radio * 0.42f;
            GUI.DrawTexture(new Rect(punta.x - r, punta.y - r, r * 2f, r * 2f), _circulo);
        }

        GUI.color = antes;
    }

    private static void DibujarMarco(Rect z, float g)
    {
        GUI.DrawTexture(new Rect(z.x, z.y, z.width, g), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(z.x, z.yMax - g, z.width, g), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(z.x, z.y, g, z.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(z.xMax - g, z.y, g, z.height), Texture2D.whiteTexture);
    }

    private static Texture2D _anillo;
    private static Texture2D Anillo => _anillo != null ? _anillo : (_anillo = CrearAnillo(128));

    private static Texture2D CrearCirculo(int lado) => CrearForma(lado, d => Mathf.Clamp01((1f - d) * lado * 0.5f));

    private static Texture2D CrearAnillo(int lado) =>
        CrearForma(lado, d => Mathf.Clamp01((1f - d) * lado * 0.5f) * Mathf.Clamp01((d - 0.9f) * lado * 0.5f));

    // d = distancia al centro, 0 en el centro y 1 en el borde
    private static Texture2D CrearForma(int lado, System.Func<float, float> alfa)
    {
        var tex = new Texture2D(lado, lado, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[lado * lado];
        for (int y = 0; y < lado; y++)
            for (int x = 0; x < lado; x++)
            {
                float dx = (x + 0.5f) / lado * 2f - 1f, dy = (y + 0.5f) / lado * 2f - 1f;
                px[y * lado + x] = new Color32(255, 255, 255, (byte)(alfa(Mathf.Sqrt(dx * dx + dy * dy)) * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }
}
