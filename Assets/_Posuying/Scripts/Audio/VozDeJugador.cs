using UnityEngine;

// La voz de UN jugador sonando en el mundo. Lo crea ChatVoz sobre el jugador
// que habla la primera vez que llega audio suyo.
//
//   - PROXIMIDAD: es un sonido 3D en su cabeza. Se oye entero de cerca y se va
//     perdiendo con la distancia hasta callar del todo.
//   - ESCENARIO: cada medio segundo se mide el sitio en el que esta el que habla
//     (hay techo? a cuanto estan las paredes?) y se elige el eco: ninguno en la
//     calle, corto en un cuarto, largo en una nave.
//   - PAREDES: si entre el que habla y tu hay un muro, la voz llega apagada.
public class VozDeJugador : MonoBehaviour
{
    [Header("Proximidad")]
    [Tooltip("Hasta esta distancia se oye a volumen completo")]
    public float distanciaCompleta = 3f;
    [Tooltip("A partir de esta distancia ya no se oye")]
    public float distanciaMaxima = 28f;

    [Header("Volumen")]
    [Tooltip("Amplificacion de la voz con el ajuste de Opciones al 100 %. El microfono llega " +
             "bajo comparado con el resto del juego; con 1 se oia poco.")]
    public float gananciaBase = 2.2f;

    [Header("Paredes")]
    [Tooltip("Frecuencia de corte con una pared en medio (mas baja = mas apagada)")]
    public float corteTrasPared = 1400f;
    [Range(0f, 1f)] public float volumenTrasPared = 0.55f;

    [Header("Escenario")]
    [Tooltip("Altura maxima a la que se busca techo")]
    public float alturaTecho = 14f;
    [Tooltip("Paredes a menos de esta distancia media: cuarto pequeno")]
    public float salaPequena = 4.5f;
    [Tooltip("Paredes a menos de esta distancia media: sala. Mas lejos: nave")]
    public float salaMediana = 11f;

    public enum Escenario { Calle, Cuarto, Sala, Nave }
    public Escenario EscenarioActual { get; private set; }
    public bool TrasPared { get; private set; }

    private AudioSource _fuente;
    private AudioReverbFilter _eco;
    private AudioLowPassFilter _filtro;
    private Transform _cabeza;

    // Cola circular de muestras: escribe el hilo del juego, lee el de audio
    private readonly float[] _cola = new float[ChatVoz.Frecuencia * 2];
    private int _escritura, _lectura, _pendientes;
    private bool _llenando = true;
    private readonly object _candado = new object();

    private float _siguienteMedida;
    private float _corteActual = 22000f, _volumenActual = 1f;
    // La lee el hilo de audio: ganancia base por el ajuste de "Voz de otros jugadores"
    private volatile float _ganancia = 2.2f;

    void Awake()
    {
        var go = new GameObject("Voz");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 1.65f;   // la boca, mas o menos
        _cabeza = go.transform;

        _fuente = go.AddComponent<AudioSource>();
        _fuente.spatialBlend = 1f;
        _fuente.rolloffMode = AudioRolloffMode.Linear;
        _fuente.minDistance = distanciaCompleta;
        _fuente.maxDistance = distanciaMaxima;
        _fuente.dopplerLevel = 0f;
        _fuente.loop = true;
        _fuente.playOnAwake = false;
        // Clip "infinito": Unity va pidiendo muestras y se le dan las que hayan llegado
        _fuente.clip = AudioClip.Create("Voz", ChatVoz.Frecuencia, 1, ChatVoz.Frecuencia, true, Leer);

        _filtro = go.AddComponent<AudioLowPassFilter>();
        _filtro.cutoffFrequency = 22000f;

        _eco = go.AddComponent<AudioReverbFilter>();
        _eco.reverbPreset = AudioReverbPreset.Off;

        _fuente.Play();
    }

    // Llega un trozo de voz por la red
    public void Recibir(byte[] datos)
    {
        lock (_candado)
        {
            for (int i = 0; i < datos.Length; i++)
            {
                // Si la cola se llena (nos hemos quedado atras) se pisa lo mas viejo
                if (_pendientes == _cola.Length) { _lectura = (_lectura + 1) % _cola.Length; _pendientes--; }
                _cola[_escritura] = ChatVoz.Descomprimir(datos[i]);
                _escritura = (_escritura + 1) % _cola.Length;
                _pendientes++;
            }
        }
    }

    // Lo llama Unity desde el hilo de audio
    private void Leer(float[] salida)
    {
        lock (_candado)
        {
            // Colchon de 120 ms antes de empezar a sonar: absorbe que los trozos no
            // lleguen a ritmo exacto. Sin el, la voz saldria entrecortada.
            if (_llenando && _pendientes >= ChatVoz.Frecuencia * 120 / 1000) _llenando = false;

            for (int i = 0; i < salida.Length; i++)
            {
                if (_llenando || _pendientes == 0)
                {
                    salida[i] = 0f;
                    if (_pendientes == 0) _llenando = true;
                    continue;
                }
                // Se amplifica la muestra (el volumen del AudioSource no pasa de 1) y se
                // redondea el pico en vez de cortarlo, para que al subirla no chasquee
                float m = _cola[_lectura] * _ganancia;
                salida[i] = m / (1f + Mathf.Abs(m) * 0.35f) * 1.35f;
                _lectura = (_lectura + 1) % _cola.Length;
                _pendientes--;
            }
        }
    }

    void Update()
    {
        _fuente.minDistance = distanciaCompleta;
        _fuente.maxDistance = distanciaMaxima;

        if (Time.unscaledTime >= _siguienteMedida)
        {
            _siguienteMedida = Time.unscaledTime + 0.5f;
            MedirEscenario();
            MedirPared();
        }

        // La pared entra y sale suave, no de golpe
        float corte = TrasPared ? corteTrasPared : 22000f;
        float volumen = TrasPared ? volumenTrasPared : 1f;
        _ganancia = gananciaBase * GameSettings.VoiceVolume;
        _corteActual = Mathf.Lerp(_corteActual, corte, 6f * Time.unscaledDeltaTime);
        _volumenActual = Mathf.Lerp(_volumenActual, volumen, 6f * Time.unscaledDeltaTime);
        _filtro.cutoffFrequency = _corteActual;
        _fuente.volume = _volumenActual;
    }

    private static readonly Vector3[] Direcciones =
    {
        Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
        (Vector3.forward + Vector3.right).normalized, (Vector3.forward + Vector3.left).normalized,
        (Vector3.back + Vector3.right).normalized, (Vector3.back + Vector3.left).normalized,
    };

    // Como es el sitio en el que esta el que habla: con techo o sin el, y de que tamano
    private void MedirEscenario()
    {
        Vector3 origen = _cabeza.position;
        Escenario nuevo;

        if (!Golpea(origen, Vector3.up, alturaTecho, out _))
        {
            nuevo = Escenario.Calle;
        }
        else
        {
            float suma = 0f;
            for (int i = 0; i < Direcciones.Length; i++)
                suma += Golpea(origen, Direcciones[i], 30f, out float d) ? d : 30f;
            float media = suma / Direcciones.Length;

            nuevo = media < salaPequena ? Escenario.Cuarto
                  : media < salaMediana ? Escenario.Sala
                  : Escenario.Nave;
        }

        if (nuevo == EscenarioActual && _eco.reverbPreset != AudioReverbPreset.Off == (nuevo != Escenario.Calle)) return;
        EscenarioActual = nuevo;

        switch (nuevo)
        {
            case Escenario.Calle: _eco.reverbPreset = AudioReverbPreset.Off; break;
            case Escenario.Cuarto: _eco.reverbPreset = AudioReverbPreset.Room; break;
            case Escenario.Sala: _eco.reverbPreset = AudioReverbPreset.Stoneroom; break;
            case Escenario.Nave: _eco.reverbPreset = AudioReverbPreset.Hangar; break;
        }
    }

    private void MedirPared()
    {
        var oyente = Camera.main;
        if (oyente == null) { TrasPared = false; return; }

        Vector3 a = _cabeza.position, b = oyente.transform.position;
        Vector3 dir = b - a;
        TrasPared = dir.sqrMagnitude > 1f && Golpea(a, dir.normalized, dir.magnitude - 0.5f, out _);
    }

    // Rayo contra el escenario: no cuentan los personajes ni los triggers
    private bool Golpea(Vector3 origen, Vector3 direccion, float distancia, out float donde)
    {
        donde = distancia;
        var golpes = Physics.RaycastAll(origen, direccion, distancia, Physics.DefaultRaycastLayers,
                                        QueryTriggerInteraction.Ignore);
        bool alguno = false;
        for (int i = 0; i < golpes.Length; i++)
        {
            if (golpes[i].collider.GetComponentInParent<Character>() != null) continue;
            if (golpes[i].distance < donde) donde = golpes[i].distance;
            alguno = true;
        }
        return alguno;
    }
}
