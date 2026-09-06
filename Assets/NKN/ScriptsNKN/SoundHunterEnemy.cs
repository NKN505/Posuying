using UnityEngine;

/// <summary>
/// "El Garrador" (enemigo 2 de la especificación, primer tercio).
///
/// Caza por oído. Prácticamente ciego: mientras no hagas ruido no sabe que
/// existes, aunque lo tengas a veinte metros de frente.
///
/// LO IMPORTANTE DE SU DISEÑO: va al SITIO DONDE SONÓ, no a por ti. No te
/// persigue, persigue un punto del mapa. Por eso quedarte quieto funciona, y
/// por eso disparar y salir corriendo también: llega al sitio del disparo y allí
/// no hay nadie. Si fuera directo al jugador, el sigilo no significaría nada.
///
/// EN RED: la IA es del servidor, como el resto.
/// </summary>
public class SoundHunterEnemy : EnemyBehaviour
{
    [Header("Oído")]
    [Tooltip("Multiplica el alcance de los ruidos. Por encima de 1 tiene mejor " +
             "oído que la media, que es su rasgo.")]
    public float hearingAcuity = 1.3f;

    [Tooltip("A esta distancia te detecta aunque no hagas ruido: lo tienes " +
             "encima. Pequeño a propósito; si fuera grande, quedarse quieto " +
             "dejaría de servir.")]
    public float closeSenseRadius = 2.5f;

    [Header("Persecución")]
    [Tooltip("Va rápido: el castigo por hacer ruido tiene que llegar pronto")]
    public float chaseSpeed = 8f;

    [Tooltip("A qué distancia del punto de destino se da por llegado")]
    public float arriveDistance = 1.5f;

    [Tooltip("Cuánto se queda husmeando en el sitio antes de rendirse")]
    public float investigateTime = 4f;

    [Header("Animación (opcional)")]
    [Tooltip("Estado del Animator al moverse. Vacío = no se toca la animación.")]
    public string moveState = "";
    [Tooltip("Estado del Animator al quedarse quieto")]
    public string idleState = "";
    public float clipSpeed = 4.5f;

    /// <summary>Para avisos en pantalla o sonido: está yendo a por algo.</summary>
    public bool IsInvestigating { get; private set; }

    private Vector3 _destino;
    private bool _tengoPista;
    private float _husmeando;

    private Animator _anim;
    private string _estadoActual = "";
    private Vector3 _posicionAnterior;
    private float _velocidadObservada;

    protected override void Awake()
    {
        base.Awake();

        // No usa el radio de detección visual heredado: su sentido es el oído.
        // Con esto Chase() y Patrol() acaban en el mismo sitio y da igual cuál
        // elija la clase padre.
        alwaysAggro = true;

        _anim = GetComponentInChildren<Animator>(true);
        _posicionAnterior = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && agent != null)
        {
            agent.speed = chaseSpeed;
            SetSpeed(chaseSpeed);
        }
    }

    protected override void Update()
    {
        base.Update();          // la IA de dentro es solo del servidor
        ActualizarAnimacion();  // esto sí, en todas las máquinas
    }

    protected override void Chase() { Cazar(); }
    protected override void Patrol() { Cazar(); }

    // ---------- La caza ----------

    private void Cazar()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        BuscarPista();

        if (!_tengoPista)
        {
            Esperar();
            return;
        }

        Ir();
    }

    // Una pista nueva reemplaza a la anterior: siempre va a lo último que oyó.
    private void BuscarPista()
    {
        Noise ruido;
        if (NoiseSystem.TryHear(transform.position, hearingAcuity, out ruido))
        {
            _destino = ruido.position;
            _tengoPista = true;
            _husmeando = 0f;
            return;
        }

        // Sin ruido solo queda el "lo tengo encima"
        var cerca = JugadorMuyCerca();
        if (cerca != null)
        {
            _destino = cerca.position;
            _tengoPista = true;
            _husmeando = 0f;
        }
    }

    private Transform JugadorMuyCerca()
    {
        var jugadores = NetworkPlayer.AllPlayers;

        for (int i = 0; i < jugadores.Count; i++)
        {
            var p = jugadores[i];
            if (p == null) continue;

            var abatido = p.GetComponent<PlayerDownedState>();
            if (abatido != null && !abatido.CanAct) continue;

            if (Vector3.Distance(transform.position, p.transform.position) <= closeSenseRadius)
                return p.transform;
        }

        return null;
    }

    private void Ir()
    {
        IsInvestigating = true;
        agent.isStopped = false;
        agent.speed = chaseSpeed;
        agent.SetDestination(_destino);

        if (Vector3.Distance(transform.position, _destino) > arriveDistance) return;

        // Ha llegado y no hay nadie: husmea un rato y se rinde. Ese margen es lo
        // que te da tiempo a alejarte en silencio antes de que vuelva a su sitio.
        _husmeando += Time.deltaTime;
        if (_husmeando >= investigateTime)
        {
            _tengoPista = false;
            _husmeando = 0f;
        }
    }

    private void Esperar()
    {
        IsInvestigating = false;

        if (agent.isStopped) return;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        agent.ResetPath();
    }

    // ---------- Animación ----------

    private void ActualizarAnimacion()
    {
        if (_anim == null || _anim.runtimeAnimatorController == null) return;
        if (string.IsNullOrEmpty(moveState) && string.IsNullOrEmpty(idleState)) return;

        Vector3 delta = transform.position - _posicionAnterior;
        delta.y = 0f;
        _posicionAnterior = transform.position;

        if (Time.deltaTime > 0f)
        {
            float v = delta.magnitude / Time.deltaTime;
            _velocidadObservada = Mathf.Lerp(_velocidadObservada, v, 12f * Time.deltaTime);
        }

        bool moviendose = _velocidadObservada > 0.35f;
        string deseado = moviendose ? moveState : idleState;

        if (string.IsNullOrEmpty(deseado)) return;

        if (deseado != _estadoActual)
        {
            _anim.CrossFade(deseado, 0.15f);
            _estadoActual = deseado;
        }

        _anim.speed = moviendose && clipSpeed > 0.01f
            ? Mathf.Clamp(_velocidadObservada / clipSpeed, 0.4f, 2f)
            : 1f;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, closeSenseRadius);

        if (_tengoPista)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position + Vector3.up, _destino + Vector3.up);
            Gizmos.DrawWireSphere(_destino, arriveDistance);
        }
    }
}
