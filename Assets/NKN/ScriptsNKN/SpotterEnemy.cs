using UnityEngine;

/// <summary>
/// "El Alertador" (enemigo 9 de la especificación, tercer tercio, raro).
///
/// No ataca, no corre, no aguanta. Solo mira, y cuenta lo que ve.
///
/// ES EL ENEMIGO MÁS PELIGROSO DEL TERCIO, y no hace daño. Es el único que TE
/// ENCUENTRA ESCONDIDO: el Hunter y el Psycho-Killer no pueden tocarte mientras
/// estés agazapado, pero el Alertador sí te ve, y avisa. Entonces los Hunters
/// van a montar guardia sobre tu escondite, y en cuanto sales, el
/// Psycho-Killer te tiene a tiro desde el otro extremo del mapa.
///
///     Te escondes -> el Alertador te encuentra -> avisa a los Hunters ->
///     te presionan -> sales -> el Psycho-Killer te dispara
///
/// Por eso mantiene las distancias y huye si te acercas: su vida depende de que
/// no llegues a él, y matarlo tiene que ser un objetivo por el que merezca la
/// pena exponerse.
///
/// EN RED: la IA es del servidor, como el resto.
/// </summary>
public class SpotterEnemy : EnemyBehaviour
{
    [Header("Observación")]
    public float visionRange = 35f;
    public float visionAngle = 130f;
    public float eyeHeight = 1.7f;
    public float hearingAcuity = 1.2f;
    public LayerMask sightBlockers = ~0;

    [Header("Aviso")]
    [Tooltip("Cada cuánto repite el aviso mientras te tenga a la vista")]
    public float alertInterval = 2f;
    [Tooltip("Cuánto dura el aviso para los Hunters")]
    public float alertLifetime = 15f;

    [Header("Cobardía")]
    [Tooltip("Si te acercas más de esto, retrocede")]
    public float keepDistance = 18f;
    public float fleeSpeed = 5.5f;
    public float patrolSpeed = 3f;

    [Header("Animación (opcional)")]
    public string moveState = "";
    public string idleState = "";
    public float clipSpeed = 4f;

    /// <summary>Te ha visto y está cantando tu posición.</summary>
    public bool IsAlerting { get; private set; }

    private float _reloj;

    private Animator _anim;
    private string _estadoAnim = "";
    private Vector3 _posicionAnterior;
    private float _velocidadObservada;

    protected override void Awake()
    {
        base.Awake();
        alwaysAggro = true;

        // No hace daño: su amenaza es lo que provoca, no lo que pega
        damageAmount = 0f;

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

    protected override void Chase() { Observar(); }
    protected override void Patrol() { Observar(); }

    private void Observar()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        PlayerController visto = Detectar();
        IsAlerting = visto != null;

        if (visto == null)
        {
            _reloj = 0f;
            Rondar();
            return;
        }

        // Canta la posición cada pocos segundos mientras lo tenga localizado
        _reloj -= Time.deltaTime;
        if (_reloj <= 0f)
        {
            _reloj = alertInterval;
            AlertSystem.Broadcast(visto.transform.position, visto.transform, alertLifetime);
        }

        Mantenerse(visto);
    }

    /// <summary>
    /// A DIFERENCIA DEL RESTO, este no respeta los escondites: te ve igual.
    /// Es su razón de ser — sin él, esconderse sería una victoria permanente.
    /// </summary>
    private PlayerController Detectar()
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

            if (Perception.CanSee(ojos, transform.forward, pecho, visionRange, visionAngle,
                                  sightBlockers, transform, p.transform))
                return p;
        }

        // También delata por oído: hacer ruido escondido es igual de malo
        Noise ruido;
        if (!NoiseSystem.TryHear(transform.position, hearingAcuity, out ruido)) return null;

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
            mejorDistancia = d; mejor = p;
        }
        return mejor;
    }

    // Ni se acerca ni se va: te vigila desde lejos. Si te acercas, retrocede.
    private void Mantenerse(PlayerController objetivo)
    {
        Vector3 haciaMi = transform.position - objetivo.transform.position;
        haciaMi.y = 0f;
        float distancia = haciaMi.magnitude;

        Mirar(objetivo.transform.position);

        if (distancia >= keepDistance)
        {
            Parar();
            return;
        }

        Vector3 huida = transform.position + haciaMi.normalized * (keepDistance - distancia + 4f);
        UnityEngine.AI.NavMeshHit hit;
        if (!UnityEngine.AI.NavMesh.SamplePosition(huida, out hit, 8f, UnityEngine.AI.NavMesh.AllAreas)) return;

        agent.isStopped = false;
        agent.speed = fleeSpeed;
        agent.SetDestination(hit.position);
    }

    private void Rondar()
    {
        agent.isStopped = false;
        agent.speed = patrolSpeed;

        if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance < 1f))
            SetNewPatrolTarget();
    }

    private void Parar()
    {
        if (agent.isStopped) return;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        agent.ResetPath();
    }

    private void Mirar(Vector3 objetivo)
    {
        Vector3 dir = objetivo - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, Quaternion.LookRotation(dir, Vector3.up),
            agent.angularSpeed * Time.deltaTime);
    }

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

        if (deseado != _estadoAnim) { _anim.CrossFade(deseado, 0.15f); _estadoAnim = deseado; }
        _anim.speed = moviendose && clipSpeed > 0.01f
            ? Mathf.Clamp(_velocidadObservada / clipSpeed, 0.4f, 2f) : 1f;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * eyeHeight, visionRange);
        Gizmos.color = new Color(1f, 1f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, keepDistance);
    }
}
