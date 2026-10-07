using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// Todo lo que el jugador puede hacer con una tecla o un boton, en un solo sitio.
public enum Accion
{
    Disparar, Apuntar, Recargar, Golpe, Correr, Agacharse, Saltar, Linterna, Interactuar,
    Inventario, Usar, Soltar, HuecoAnterior, HuecoSiguiente, Marcar, Chat, Voz
}

// Botones de un mando, con nombres neutros: valen igual para Xbox y PlayStation
// (Sur es A en uno y Cruz en el otro). El nombre que ve el jugador sale de NombreBoton.
public enum BotonMando
{
    Ninguno, Sur, Este, Oeste, Norte, L1, R1, L2, R2, L3, R3, Arriba, Abajo, Izquierda, Derecha, Select, Start
}

// Controles configurables, para teclado y raton Y para mando.
//
// Cada accion tiene una tecla y un boton de mando, las dos cosas a la vez: se
// puede jugar con cualquiera sin cambiar ningun ajuste. Se eligen en Opciones >
// Controles y se guardan en el equipo.
//
// El resto del juego no pregunta por teclas concretas, sino por acciones:
//     if (Controles.Pulsado(Accion.Saltar)) ...
//
// El mando se lee con el Input System nuevo, que da la misma distribucion para
// Xbox y PlayStation. El teclado y el raton siguen por el sistema clasico.
public static class Controles
{
    private static readonly int N = System.Enum.GetValues(typeof(Accion)).Length;

    private static readonly KeyCode[] TeclasPorDefecto =
    {
        KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.R, KeyCode.C, KeyCode.LeftShift, KeyCode.LeftControl,
        KeyCode.Space, KeyCode.F, KeyCode.E, KeyCode.Tab, KeyCode.X, KeyCode.G,
        KeyCode.None, KeyCode.None, KeyCode.Q, KeyCode.T, KeyCode.V
    };

    private static readonly BotonMando[] BotonesPorDefecto =
    {
        BotonMando.R2, BotonMando.L2, BotonMando.Oeste, BotonMando.R3, BotonMando.L3, BotonMando.Este,
        BotonMando.Sur, BotonMando.Arriba, BotonMando.Norte, BotonMando.Select, BotonMando.Abajo, BotonMando.Ninguno,
        BotonMando.Izquierda, BotonMando.Derecha, BotonMando.R1, BotonMando.Ninguno, BotonMando.L1
    };

    private static readonly string[] Etiquetas =
    {
        "Disparar", "Apuntar", "Recargar / gastar vida", "Golpe cuerpo a cuerpo", "Correr", "Agacharse",
        "Saltar", "Linterna", "Interactuar / levantar", "Inventario", "Usar objeto elegido", "Soltar objeto elegido",
        "Hueco anterior", "Hueco siguiente", "Marcar para el equipo", "Chat de texto", "Chat de voz (mantener)"
    };

    private static readonly KeyCode[] _teclas = new KeyCode[N];
    private static readonly BotonMando[] _botones = new BotonMando[N];
    private static bool _cargado;

    /// <summary>Sensibilidad de la camara con el stick derecho (1 = unos 150 grados por segundo).</summary>
    public static float SensibilidadMando = 1f;
    public const float MinSensibilidadMando = 0.2f, MaxSensibilidadMando = 3f;

    /// <summary>True si lo ultimo que ha tocado el jugador es el mando. Decide que nombres ensena la ayuda.</summary>
    public static bool UsandoMando { get; private set; }

    public static bool MandoConectado => Gamepad.current != null;

    public static int Cuantas => N;
    public static string Etiqueta(Accion a) => Etiquetas[(int)a];

    // ---------- Guardado ----------

    // Las seis teclas que ya existian conservan su clave de guardado: quien las
    // habia cambiado no pierde su eleccion.
    private static string ClaveTecla(Accion a)
    {
        switch (a)
        {
            case Accion.Chat: return "opt_key_chat";
            case Accion.Voz: return "opt_key_voice";
            case Accion.Marcar: return "opt_key_ping";
            case Accion.Soltar: return "opt_key_drop";
            case Accion.Usar: return "opt_key_use";
            case Accion.Golpe: return "opt_key_melee";
            default: return "ctl_tecla_" + a;
        }
    }

    private static void Cargar()
    {
        if (_cargado) return;
        _cargado = true;

        for (int i = 0; i < N; i++)
        {
            _teclas[i] = (KeyCode)PlayerPrefs.GetInt(ClaveTecla((Accion)i), (int)TeclasPorDefecto[i]);
            _botones[i] = (BotonMando)PlayerPrefs.GetInt("ctl_boton_" + (Accion)i, (int)BotonesPorDefecto[i]);
        }
        SensibilidadMando = PlayerPrefs.GetFloat("ctl_sens_mando", 1f);
    }

    public static void Guardar()
    {
        Cargar();
        for (int i = 0; i < N; i++)
        {
            PlayerPrefs.SetInt(ClaveTecla((Accion)i), (int)_teclas[i]);
            PlayerPrefs.SetInt("ctl_boton_" + (Accion)i, (int)_botones[i]);
        }
        PlayerPrefs.SetFloat("ctl_sens_mando", SensibilidadMando);
        PlayerPrefs.Save();
    }

    /// <summary>Vuelve a los controles de fabrica (los del teclado o los del mando).</summary>
    public static void Restablecer(bool mando)
    {
        Cargar();
        for (int i = 0; i < N; i++)
        {
            if (mando) _botones[i] = BotonesPorDefecto[i];
            else _teclas[i] = TeclasPorDefecto[i];
        }
        if (mando) SensibilidadMando = 1f;
        Guardar();
    }

    public static KeyCode Tecla(Accion a) { Cargar(); return _teclas[(int)a]; }
    public static BotonMando Boton(Accion a) { Cargar(); return _botones[(int)a]; }

    // Una tecla o un boton solo puede hacer una cosa: si ya la tenia otra accion, esa se queda sin el
    public static void PonerTecla(Accion a, KeyCode tecla)
    {
        Cargar();
        if (tecla != KeyCode.None)
            for (int i = 0; i < N; i++) if (_teclas[i] == tecla) _teclas[i] = KeyCode.None;
        _teclas[(int)a] = tecla;
        Guardar();
    }

    public static void PonerBoton(Accion a, BotonMando boton)
    {
        Cargar();
        if (boton != BotonMando.Ninguno)
            for (int i = 0; i < N; i++) if (_botones[i] == boton) _botones[i] = BotonMando.Ninguno;
        _botones[(int)a] = boton;
        Guardar();
    }

    // ---------- Lectura ----------

    private static ButtonControl Control(BotonMando b)
    {
        Gamepad m = Gamepad.current;
        if (m == null) return null;
        switch (b)
        {
            case BotonMando.Sur: return m.buttonSouth;
            case BotonMando.Este: return m.buttonEast;
            case BotonMando.Oeste: return m.buttonWest;
            case BotonMando.Norte: return m.buttonNorth;
            case BotonMando.L1: return m.leftShoulder;
            case BotonMando.R1: return m.rightShoulder;
            case BotonMando.L2: return m.leftTrigger;
            case BotonMando.R2: return m.rightTrigger;
            case BotonMando.L3: return m.leftStickButton;
            case BotonMando.R3: return m.rightStickButton;
            case BotonMando.Arriba: return m.dpad.up;
            case BotonMando.Abajo: return m.dpad.down;
            case BotonMando.Izquierda: return m.dpad.left;
            case BotonMando.Derecha: return m.dpad.right;
            case BotonMando.Select: return m.selectButton;
            case BotonMando.Start: return m.startButton;
            default: return null;
        }
    }

    public static bool BotonPulsado(BotonMando b) { var c = Control(b); return c != null && c.wasPressedThisFrame; }
    public static bool BotonMantenido(BotonMando b) { var c = Control(b); return c != null && c.isPressed; }

    /// <summary>La accion acaba de pulsarse este frame (con la tecla o con el boton del mando).</summary>
    public static bool Pulsado(Accion a)
    {
        Cargar();
        KeyCode t = _teclas[(int)a];
        return (t != KeyCode.None && Input.GetKeyDown(t)) || BotonPulsado(_botones[(int)a]);
    }

    /// <summary>La accion se mantiene pulsada.</summary>
    public static bool Mantenido(Accion a)
    {
        Cargar();
        KeyCode t = _teclas[(int)a];
        return (t != KeyCode.None && Input.GetKey(t)) || BotonMantenido(_botones[(int)a]);
    }

    private static Vector2 Stick(StickControl stick)
    {
        Vector2 v = stick.ReadValue();
        float m = v.magnitude;
        if (m < 0.18f) return Vector2.zero;                       // zona muerta: los sticks nunca vuelven a cero exacto
        return v / m * Mathf.InverseLerp(0.18f, 1f, m);
    }

    /// <summary>Movimiento: X = lateral, Y = adelante. Teclado (WASD) o stick izquierdo.</summary>
    public static Vector2 Mover()
    {
        Vector2 v = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        if (Gamepad.current != null)
        {
            Vector2 s = Stick(Gamepad.current.leftStick);
            if (s.sqrMagnitude > v.sqrMagnitude) v = s;
        }
        return Vector2.ClampMagnitude(v, 1f);
    }

    /// <summary>
    /// Giro de camara de ESTE frame, ya con la sensibilidad aplicada: raton mas
    /// stick derecho. X = girar, Y = subir/bajar (sin invertir).
    /// </summary>
    public static Vector2 Mirar()
    {
        Cargar();
        Vector2 v = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * GameSettings.MouseSensitivity;
        if (Gamepad.current != null)
        {
            Vector2 s = Stick(Gamepad.current.rightStick);
            // Curva suave: a medio stick se gira menos de la mitad, para apuntar fino
            s *= s.magnitude;
            v += new Vector2(s.x * 150f, s.y * 95f) * SensibilidadMando * Time.deltaTime;
        }
        return v;
    }

    // ---------- Que esta usando el jugador ----------

    /// <summary>Lo llama una vez por frame el vigilante de abajo.</summary>
    internal static void Actualizar()
    {
        Gamepad m = Gamepad.current;
        if (m != null)
        {
            bool actividad = m.leftStick.ReadValue().sqrMagnitude > 0.2f || m.rightStick.ReadValue().sqrMagnitude > 0.2f;
            if (!actividad)
                for (int i = 1; i <= (int)BotonMando.Start; i++)
                    if (BotonMantenido((BotonMando)i)) { actividad = true; break; }
            if (actividad) { UsandoMando = true; return; }
        }

        if (Input.anyKeyDown || Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.5f || Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.5f)
            UsandoMando = false;
    }

    /// <summary>El primer boton del mando pulsado este frame (para elegir un control nuevo).</summary>
    public static bool AlgunBotonPulsado(out BotonMando cual)
    {
        for (int i = 1; i <= (int)BotonMando.Start; i++)
            if (BotonPulsado((BotonMando)i)) { cual = (BotonMando)i; return true; }
        cual = BotonMando.Ninguno;
        return false;
    }

    // ---------- Nombres para la interfaz ----------

    public static bool EsPlayStation
    {
        get
        {
            Gamepad m = Gamepad.current;
            if (m == null) return false;
            string n = ((m.layout ?? "") + " " + (m.description.manufacturer ?? "") + " " + (m.displayName ?? "")).ToLowerInvariant();
            return n.Contains("dualshock") || n.Contains("dualsense") || n.Contains("sony") || n.Contains("playstation");
        }
    }

    public static string NombreBoton(BotonMando b)
    {
        bool ps = EsPlayStation;
        switch (b)
        {
            case BotonMando.Sur: return ps ? "Cruz" : "A";
            case BotonMando.Este: return ps ? "Circulo" : "B";
            case BotonMando.Oeste: return ps ? "Cuadrado" : "X";
            case BotonMando.Norte: return ps ? "Triangulo" : "Y";
            case BotonMando.L1: return ps ? "L1" : "LB";
            case BotonMando.R1: return ps ? "R1" : "RB";
            case BotonMando.L2: return ps ? "L2" : "LT";
            case BotonMando.R2: return ps ? "R2" : "RT";
            case BotonMando.L3: return ps ? "L3" : "Pulsar stick izq.";
            case BotonMando.R3: return ps ? "R3" : "Pulsar stick der.";
            case BotonMando.Arriba: return "Cruceta arriba";
            case BotonMando.Abajo: return "Cruceta abajo";
            case BotonMando.Izquierda: return "Cruceta izquierda";
            case BotonMando.Derecha: return "Cruceta derecha";
            case BotonMando.Select: return ps ? "Share" : "Vista";
            case BotonMando.Start: return ps ? "Options" : "Menu";
            default: return "Sin asignar";
        }
    }

    public static string NombreTecla(KeyCode k)
    {
        if (k == KeyCode.None) return "Sin asignar";
        if (k == KeyCode.Mouse0) return "Clic izquierdo";
        if (k == KeyCode.Mouse1) return "Clic derecho";
        if (k == KeyCode.Mouse2) return "Clic central";
        if (k == KeyCode.LeftShift) return "Mayus izq.";
        if (k == KeyCode.RightShift) return "Mayus der.";
        if (k == KeyCode.LeftControl) return "Ctrl izq.";
        if (k == KeyCode.RightControl) return "Ctrl der.";
        if (k == KeyCode.Space) return "Espacio";
        if (k == KeyCode.Return) return "Intro";

        string s = k.ToString();
        if (s.StartsWith("Alpha")) return s.Substring(5);
        if (s.StartsWith("Keypad")) return "Num " + s.Substring(6);
        if (s.StartsWith("Mouse")) return "Raton " + (int.Parse(s.Substring(5)) + 1);
        return s;
    }

    /// <summary>Como se llama el control de esa accion en lo que el jugador esta usando ahora.</summary>
    public static string Nombre(Accion a)
    {
        Cargar();
        return UsandoMando ? NombreBoton(_botones[(int)a]) : NombreTecla(_teclas[(int)a]);
    }
}

// Mira cada frame si el jugador esta con el mando o con el teclado. Se crea solo.
public class VigilanteControles : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Crear()
    {
        if (FindFirstObjectByType<VigilanteControles>() != null) return;
        var go = new GameObject("VigilanteControles");
        DontDestroyOnLoad(go);
        go.AddComponent<VigilanteControles>();
    }

    void Update()
    {
        Controles.Actualizar();
    }
}
