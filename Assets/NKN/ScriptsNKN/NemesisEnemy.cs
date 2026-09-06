using UnityEngine;

/// <summary>
/// "El Nemesis" (enemigo 3 de la especificación, primer tercio, raro).
///
/// Te detecta por vista O por oído, y a partir de ahí no te suelta. No es que
/// sea más rápido o más fuerte: es que no se cansa. Los otros dos enemigos del
/// tercio se pueden gestionar quedándote quieto o engañándolos; a este hay que
/// perderlo de verdad, esconderse o matarlo.
///
/// LA REGLA QUE LO DEFINE: una vez te detecta, NO vuelve a patrullar. Si le
/// rompes la línea de visión, va a tu última posición conocida y se pone a
/// rastrear la zona. Escaparse es posible, pero es algo que tienes que ganarte
/// poniendo distancia y silencio, no simplemente doblando una esquina.
///
/// EN RED: la IA es del servidor, como el resto.
/// </summary>
public class NemesisEnemy : EnemyBehaviour
{
    private enum Estado { Patrullando, Persiguiendo, Rastreando }

    [Header("Vista")]
    public float visionRange = 25f;
    [Tooltip("Cono de visión total en grados. Amplio: es un cazador.")]
    public float visionAngle = 110f;
    [Tooltip("Altura de sus ojos sobre el suelo")]
    public float eyeHeight = 1.6f;
    public LayerMask sightBlockers = ~0;

    [Header("Oído")]
    [Tooltip("Multiplica el alcance de los ruidos. Alto, según la ficha.")]
    public float hearingAcuity = 1.5f;

    [Header("Persecución")]
    public float chaseSpeed = 7f;
    [Tooltip("Va más tranquilo mientras no te ha visto")]
    public float patrolSpeed = 3.5f;

    [Tooltip("Radio en el que rastrea alrededor de tu última posición conocida")]
    public float searchRadius = 12f;
    [Tooltip("Cada cuánto elige un punto nuevo al rastrear")]
    public float searchPointInterval = 3f;

    [Tooltip("Si te alejas más de esto, se rinde y vuelve a patrullar. En 0 " +
             "no se rinde NUNCA, que es lo que pide la ficha. Se deja por si al " +
             "probarlo resulta insufrible.")]
    public float forgetDistance = 0f;

    [Header("Animación (opcional)")]
    public string moveState = "";
    public string idleState = "";
    public float clipSpeed = 4.5f;

    /// <summary>Te ha detectado y viene a por ti. Para música o avisos.</summary>
    public bool IsHunting { get { return _estado != Estado.Patrullando; } }

    private Estado _estado = Estado.Patrullando;
    private PlayerController _objetivo;
    private Vector3 _ultimaPosicionConocida;
    private float _relojRastreo;

    private Animator _anim;
    private string _estadoAnim = "";
    private Vector3 _posicionAnterior;
    private float _velocidadObservada;

    protected override void Awake()
    {
        base.Awake();

        // La detección la lleva él: no queremos el radio visual heredado
        alwaysAggro = true;

        _anim = GetComponentInChildren<Animator>(true);
        _posicionAnterior = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer && agent != null) agent.speed = patrolSpeed;
    }

    protected override void Update()
    {
        base.Update();
        ActualizarAnimacion();
    }

    protected override void Chase() { Pensar(); }
    protected override void Patrol() { Pensar(); }

    // ---------- Decisión ----------

    private void Pensar()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        PlayerController percibido = Percibir();

        if (percibido != null)
        {
            _objetivo = percibido;
            _ultimaPosicionConocida = percibido.transform.position;
            _estado = Estado.Persiguiendo;
        }
        else if (_estado == Estado.Persiguiendo)
        {
            // Lo acaba de perder: va a donde estaba y rastrea desde ahí
            _estado = Estado.Rastreando;
            _relojRastreo = 0f;
        }

        if (SeRinde())
        {
            _estado = Estado.Patrullando;
            _objetivo = null;
        }

        switch (_estado)
        {
            case Estado.Persiguiendo: Perseguir(); break;
            case Estado.Rastreando:   Rastrear();  break;
            default:                  Rondar();    break;
        }
    }

    // Vista o ruido, en ese orden. Los abatidos no cuentan: si no, acamparía
    // sobre un caído y no dejaría reanimarlo.
    private PlayerController Percibir()
    {
        Vector3 ojos = transform.position + Vector3.up * eyeHeight;
        var jugadores = NetworkPlayer.AllPlayers;

        for (int i = 0; i < jugadores.Count; i++)
        {
            var p = jugadores[i];
            if (p == null) continue;

            var abatido = p.GetComponent<PlayerDownedState>();
            if (abatido != null && !abatido.CanAct) continue;

            Vector3 pecho, cabeza;
            Perception.BodyPoints(p.transform, out pecho, out cabeza);

            bool visto =
                Perception.CanSee(ojos, transform.forward, pecho, visionRange, visionAngle,
                                  sightBlockers, transform, p.transform) ||
                Perception.CanSee(ojos, transform.forward, cabeza, visionRange, visionAngle,
                                  sightBlockers, transform, p.transform);

            if (visto) return p;
        }

        // Por oído no sabe QUIÉN hizo el ruido, solo dónde sonó. Se queda con el
        // jugador más cercano a ese punto, que es la apuesta razonable.
        Noise ruido;
        if (NoiseSystem.TryHear(transform.position, hearingAcuity, out ruido))
        {
            _ultimaPosicionConocida = ruido.position;

            PlayerController mejor = null;
            float mejorDistancia = float.MaxValue;

            for (int i = 0; i < jugadores.Count; i++)
            {
                var p = jugadores[i];
                if (p == null) continue;

                var abatido = p.GetComponent<PlayerDownedState>();
                if (abatido != null && !abatido.CanAct) continue;

                float d = Vector3.Distance(p.transform.position, ruido.position);
                if (d >= mejorDistancia) continue;

                mejorDistancia = d;
                mejor = p;
            }

            return mejor;
        }

        return null;
    }

    private bool SeRinde()
    {
        if (forgetDistance <= 0f) return false;      // no se rinde nunca
        if (_estado == Estado.Patrullando) return false;

        return Vector3.Distance(transform.position, _ultimaPosicionConocida) > forgetDistance;
    }

    // ---------- Los tres modos ----------

    private void Perseguir()
    {
        agent.isStopped = false;
        agent.speed = chaseSpeed;
        if (_objetivo != null) agent.SetDestination(_objetivo.transform.position);
    }

    private void Rastrear()
    {
        agent.isStopped = false;
        agent.speed = chaseSpeed;

        _relojRastreo -= Time.deltaTime;

        bool llegado = !agent.pathPending && agent.remainingDistance < 1.5f;
        if (!llegado && _relojRastreo > 0f) return;

        // Sigue buscando indefinidamente alrededor del último rastro: no vuelve
        // a patrullar por su cuenta. Perderlo es cosa tuya, no suya.
        _relojRastreo = searchPointInterval;

        Vector3 candidato = _ultimaPosicionConocida + Random.insideUnitSphere * searchRadius;
        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(candidato, out hit, searchRadius, UnityEngine.AI.NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    private void Rondar()
    {
        agent.isStopped = false;
        agent.speed = patrolSpeed;

        if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance < 1f))
            SetNewPatrolTarget();
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

        if (deseado != _estadoAnim)
        {
            _anim.CrossFade(deseado, 0.15f);
            _estadoAnim = deseado;
        }

        _anim.speed = moviendose && clipSpeed > 0.01f
            ? Mathf.Clamp(_velocidadObservada / clipSpeed, 0.4f, 2f)
            : 1f;
    }

    void OnDrawGizmosSelected()
    {
        Vector3 ojos = transform.position + Vector3.up * eyeHeight;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(ojos, visionRange);

        Vector3 izq = Quaternion.Euler(0f, -visionAngle * 0.5f, 0f) * transform.forward;
        Vector3 der = Quaternion.Euler(0f, visionAngle * 0.5f, 0f) * transform.forward;
        Gizmos.DrawLine(ojos, ojos + izq * visionRange);
        Gizmos.DrawLine(ojos, ojos + der * visionRange);

        if (_estado == Estado.Rastreando)
        {
            Gizmos.color = new Color(1f, 0.4f, 0f);
            Gizmos.DrawWireSphere(_ultimaPosicionConocida, searchRadius);
        }
    }
}
