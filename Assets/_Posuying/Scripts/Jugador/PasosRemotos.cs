using UnityEngine;

// Los pasos de OTRO jugador, tal como se oyen en esta maquina.
//
// En su maquina los lanza su PlayerController al andar; aqui ese script esta
// apagado (no controlamos su personaje), asi que sus pasos no sonaban nunca.
// En vez de mandar un mensaje de red por cada pisada, se deducen de como se
// mueve su copia: cada cierta distancia recorrida, un paso.
//
// Lo anade NetworkPlayer a las copias de los demas jugadores. Usa los mismos
// sonidos y zancadas que tiene configurados el PlayerController del prefab.
public class PasosRemotos : MonoBehaviour
{
    [Tooltip("Por encima de esta velocidad (m/s) suenan pasos de carrera")]
    public float velocidadCarrera = 5.5f;
    [Tooltip("Subiendo o bajando mas rapido que esto se considera que esta en el aire")]
    public float velocidadVerticalMaxima = 2.5f;

    private PlayerController _controller;
    private PlayerDownedState _estado;
    private Vector3 _anterior;
    private float _distancia;
    private float _velocidad;
    private float _vivo;

    void Awake()
    {
        _controller = GetComponent<PlayerController>();
        _estado = GetComponent<PlayerDownedState>();
        _anterior = transform.position;
    }

    void OnEnable()
    {
        _anterior = transform.position;
        _distancia = 0f;
        _vivo = 0f;
    }

    void Update()
    {
        if (_controller == null || Time.deltaTime <= 0f) return;

        Vector3 delta = transform.position - _anterior;
        _anterior = transform.position;

        float vertical = Mathf.Abs(delta.y) / Time.deltaTime;
        delta.y = 0f;
        float paso = delta.magnitude;

        // Un salto de posicion (reaparecer, teletransporte) no son pasos. Tampoco el
        // primer segundo: al aparecer, la copia viaja deslizandose desde el origen
        // hasta su punto de aparicion y eso sonaba como seis pasos de golpe.
        _vivo += Time.deltaTime;
        if (paso > 3f || paso / Time.deltaTime > 14f || _vivo < 1.5f) { _distancia = 0f; _velocidad = 0f; return; }

        _velocidad = Mathf.Lerp(_velocidad, paso / Time.deltaTime, 10f * Time.deltaTime);

        // Parado, en el aire o tirado en el suelo: sin pasos
        bool enPie = _estado == null || _estado.CanAct;
        if (!enPie || _velocidad < 0.5f || vertical > velocidadVerticalMaxima)
        {
            if (_velocidad < 0.5f) _distancia = 0f;
            return;
        }

        _distancia += paso;

        bool corriendo = _velocidad >= velocidadCarrera;
        float zancada = corriendo ? _controller.sprintStride : _controller.jogStride;
        if (_distancia < zancada) return;
        _distancia = 0f;

        AK.Wwise.Event evento = corriendo ? _controller.sprintEvent : _controller.jogEvent;
        if (evento == null || !evento.IsValid()) return;

        // Con la reverb del sitio donde pisa, igual que los pasos propios
        WwiseRoomAcoustics.ApplyReverb(gameObject, WwiseRoomAcoustics.GetRoom(transform.position, transform));
        evento.Post(gameObject);
        SondaPruebas.PasosOidos++;
    }
}
