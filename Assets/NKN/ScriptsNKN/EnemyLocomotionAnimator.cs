using UnityEngine;

/// <summary>
/// Pone la animación de andar/correr a cualquier enemigo, sin que su script de
/// IA tenga que saber nada de animación.
///
/// MIDE LA VELOCIDAD DEL TRANSFORM, no la del NavMeshAgent. Es a propósito: en
/// los clientes el agente está apagado y al enemigo lo mueve NetworkTransform,
/// así que el agente marcaría cero. Midiendo el transform el mismo componente
/// vale en todas las máquinas y no hace falta un NetworkAnimator ni tráfico de
/// red extra.
///
/// El controlador que use no debe tener transiciones automáticas: aquí se
/// decide el estado y nadie más debería cambiarlo.
/// </summary>
[DisallowMultipleComponent]
public class EnemyLocomotionAnimator : MonoBehaviour
{
    [Header("Estados del Animator")]
    public string idleState = "idle";
    public string walkState = "walk";
    [Tooltip("Dejar vacío si el modelo no tiene animación de correr")]
    public string runState = "run";

    [Header("Umbrales de velocidad (m/s)")]
    [Tooltip("Empieza a andar por encima de esta velocidad")]
    public float startMovingSpeed = 0.35f;
    [Tooltip("Vuelve a quieto por debajo de esta. Mas baja que la anterior a " +
             "proposito: sin ese margen, una velocidad que oscile alrededor del " +
             "umbral hace que la animacion salte entre quieto y andar cada pocos " +
             "frames y parezca que se corta.")]
    public float stopMovingSpeed = 0.15f;
    [Tooltip("Calcula runThreshold solo a partir de la velocidad del NavMeshAgent. " +
             "Asi no hay que acordarse de subirlo cada vez que se sube la velocidad " +
             "del enemigo: olvidarlo deja el umbral pegado a la velocidad de crucero " +
             "y la animacion parpadea entre andar y correr.")]
    public bool umbralCorrerAutomatico = true;
    [Range(0.3f, 0.9f)]
    [Tooltip("A que fraccion de la velocidad del agente empieza a correr")]
    public float fraccionCorrer = 0.65f;
    [Tooltip("Por encima de esto usa la animación de correr. Con umbralCorrerAutomatico " +
             "activo este valor se reescribe solo y aqui solo sirve para verlo.")]
    public float runThreshold = 2.5f;
    [Range(0.3f, 0.95f)]
    [Tooltip("Fraccion de runThreshold a la que DEJA de correr. El margen entre " +
             "los dos valores es lo que impide el parpadeo.")]
    public float runHysteresis = 0.75f;
    [Tooltip("Tiempo minimo en un estado antes de poder cambiar A OTRO")]
    public float minStateTime = 0.25f;

    [Header("Ritmo de la animación")]
    [Tooltip("A qué velocidad real se ve natural el clip de andar")]
    public float walkClipSpeed = 1.4f;
    [Tooltip("A qué velocidad real se ve natural el clip de correr")]
    public float runClipSpeed = 4.5f;
    public float minAnimSpeed = 0.5f;
    public float maxAnimSpeed = 2f;

    [Header("Mezcla")]
    public float crossFade = 0.15f;

    /// <summary>
    /// Congela la animación en el fotograma actual. Lo usa el Acechador: al
    /// pararse tiene que quedarse clavado a media zancada, no saltar a una pose
    /// de reposo, que delataría que hay una máquina de estados detrás.
    /// </summary>
    public bool Frozen { get; set; }

    /// <summary>
    /// Reproduce un clip de un solo uso (atacar, gritar, recibir un golpe) y
    /// suspende la locomoción mientras dure. Al acabar vuelve solo a idle/walk/run
    /// según la velocidad que lleve, sin que el que lo llamó tenga que hacer nada.
    ///
    /// La duración se pasa a mano en vez de leer la del clip a propósito: casi
    /// siempre interesa cortarlo antes. El Attack del Slender dura 2,73 s y
    /// dejarlo entero clava al bicho una eternidad.
    /// </summary>
    public void PlayOneShot(string estado, float duracion, float mezcla)
    {
        if (_anim == null || _anim.runtimeAnimatorController == null) return;
        if (string.IsNullOrEmpty(estado) || duracion <= 0f) return;

        _anim.speed = 1f;
        _anim.CrossFade(estado, mezcla);
        _estadoPuesto = estado;
        _tiempoEnEstado = 0f;
        _finDelOneShot = Time.time + duracion;
    }

    public void PlayOneShot(string estado, float duracion) { PlayOneShot(estado, duracion, 0.1f); }

    /// <summary>True mientras haya un clip de un solo uso en marcha.</summary>
    public bool EnOneShot { get { return Time.time < _finDelOneShot; } }

    /// <summary>Velocidad medida, por si algún script la quiere.</summary>
    public float ObservedSpeed { get { return _velocidad; } }

    private Animator _anim;
    private UnityEngine.AI.NavMeshAgent _agente;
    private Vector3 _posicionAnterior;
    private float _velocidad;
    private bool _moviendose;
    private bool _corriendo;
    private string _estadoPuesto = "";
    private float _tiempoEnEstado;
    private float _finDelOneShot;

    void Awake()
    {
        _anim = GetComponentInChildren<Animator>(true);
        _agente = GetComponent<UnityEngine.AI.NavMeshAgent>();
        _posicionAnterior = transform.position;
    }

    void Update()
    {
        if (_anim == null || _anim.runtimeAnimatorController == null) return;

        Medir();
        ActualizarUmbralCorrer();

        if (Frozen) { _anim.speed = 0f; return; }

        // Un clip de un solo uso manda sobre la locomocion mientras dure. Sin
        // esto, la maquina de estados lo pisaria al frame siguiente y el ataque
        // no llegaria a verse.
        if (Time.time < _finDelOneShot) { _anim.speed = 1f; return; }

        _tiempoEnEstado += Time.deltaTime;

        // Histeresis: hace falta superar un umbral para empezar a andar y bajar
        // de OTRO mas bajo para volver a pararse. Con un solo umbral, una
        // velocidad que oscile a su alrededor reinicia el clip sin parar.
        if (_moviendose) { if (_velocidad < stopMovingSpeed) _moviendose = false; }
        else             { if (_velocidad > startMovingSpeed) _moviendose = true; }

        // Andar <-> correr necesita la MISMA histeresis que quieto <-> andar.
        // Con un solo umbral, un enemigo cuya velocidad de crucero se parezca al
        // umbral lo cruza varias veces por segundo y cada cruce reinicia el clip:
        // se ve como si la animacion se cortara y volviera a empezar.
        if (string.IsNullOrEmpty(runState)) _corriendo = false;
        else if (_corriendo) { if (_velocidad < runThreshold * runHysteresis) _corriendo = false; }
        else                 { if (_velocidad > runThreshold) _corriendo = true; }

        string deseado = !_moviendose ? idleState : (_corriendo ? runState : walkState);
        if (string.IsNullOrEmpty(deseado)) return;

        // Se comprueba el estado REAL y no una cadena cacheada: si algo saca al
        // Animator de su sitio, vuelve solo.
        bool enSitio = _anim.GetCurrentAnimatorStateInfo(0).IsName(deseado);
        if (!enSitio && !_anim.IsInTransition(0))
        {
            // Si el estado que se quiere es el que ya se habia pedido, es que algo
            // ha sacado al Animator de su sitio: se vuelve de inmediato. Si es uno
            // DISTINTO, se respeta el tiempo minimo de permanencia; eso es lo que
            // corta el parpadeo. Antes esta condicion estaba justo al reves y solo
            // frenaba el caso que no habia que frenar.
            bool recuperando = deseado == _estadoPuesto;
            if (recuperando || _tiempoEnEstado >= minStateTime)
            {
                _anim.CrossFade(deseado, crossFade);
                _estadoPuesto = deseado;
                _tiempoEnEstado = 0f;
            }
        }

        if (!_moviendose) { _anim.speed = 1f; return; }

        // El ritmo sigue a la velocidad real para que los pies no patinen
        float referencia = _corriendo ? runClipSpeed : walkClipSpeed;
        _anim.speed = referencia > 0.01f
            ? Mathf.Clamp(_velocidad / referencia, minAnimSpeed, maxAnimSpeed)
            : 1f;
    }

    // El umbral de correr se ata a la velocidad del agente en vez de dejarlo a
    // mano. Puesto a mano acaba coincidiendo con la velocidad de crucero en cuanto
    // alguien toca una y no la otra, y entonces la velocidad medida cruza el umbral
    // varias veces por segundo y cada cruce reinicia el clip.
    //
    // Se recalcula cada frame a proposito: hay enemigos que cambian de velocidad en
    // marcha (el Tanque al aparecer, el Cobarde al huir) y el umbral debe seguirla.
    private void ActualizarUmbralCorrer()
    {
        if (!umbralCorrerAutomatico || _agente == null) return;
        if (_agente.speed <= 0.01f) return;   // parado a proposito: se deja el ultimo

        runThreshold = _agente.speed * fraccionCorrer;
    }

    private void Medir()
    {
        Vector3 delta = transform.position - _posicionAnterior;
        delta.y = 0f;
        _posicionAnterior = transform.position;

        if (Time.deltaTime <= 0f) return;

        float instantanea = delta.magnitude / Time.deltaTime;

        // Suavizado: sin esto, un salto de posición de la red da un tirón
        _velocidad = Mathf.Lerp(_velocidad, instantanea, 12f * Time.deltaTime);
    }
}
