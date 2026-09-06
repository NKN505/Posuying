using UnityEngine;

/// <summary>
/// "El Hunter" (enemigo 7 de la especificación, tercer tercio, común).
///
/// Emboscador de media distancia. NO te persigue: se queda en su sitio
/// vigilando, y dispara en cuanto te expones. Si te escondes, no puede tocarte.
///
/// LA REGLA QUE LO DEFINE: es reactivo, no proactivo. Un enemigo que te busca
/// te obliga a huir; uno que espera te obliga a MIRAR ANTES DE MOVERTE, que es
/// lo que pide el tercer tercio. Por eso solo se mueve cuando el Alertador le
/// dice dónde estás: sin ese aviso podrías quedarte escondido indefinidamente,
/// y esa es justamente la tensión que monta la cadena de los tres.
///
/// EN RED: la IA es del servidor, como el resto.
/// </summary>
[RequireComponent(typeof(EnemyRangedAttack))]
public class HunterEnemy : EnemyBehaviour
{
    [Header("Vigilancia")]
    public float watchRange = 22f;
    public float visionAngle = 100f;
    public float eyeHeight = 1.6f;
    public LayerMask sightBlockers = ~0;

    [Header("Reaccion a los avisos del Alertador")]
    [Tooltip("Hasta dónde le llegan los avisos")]
    public float alertRange = 70f;
    [Tooltip("A qué distancia del punto avisado se planta a vigilar. No va " +
             "encima: se coloca a distancia de tiro y espera a que salgas.")]
    public float ambushDistance = 14f;
    public float moveSpeed = 4.5f;

    [Header("Animación (opcional)")]
    public string moveState = "";
    public string idleState = "";
    public float clipSpeed = 4f;

    /// <summary>Tiene a alguien a tiro ahora mismo.</summary>
    public bool HasPrey { get; private set; }

    private EnemyRangedAttack _arma;
    private Vector3 _puestoDeVigilancia;
    private bool _tienePuesto;

    private Animator _anim;
    private string _estadoAnim = "";
    private Vector3 _posicionAnterior;
    private float _velocidadObservada;

    protected override void Awake()
    {
        base.Awake();
        alwaysAggro = true;

        _arma = GetComponent<EnemyRangedAttack>();
        _anim = GetComponentInChildren<Animator>(true);
        _posicionAnterior = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer && agent != null) agent.speed = moveSpeed;
    }

    protected override void Update()
    {
        base.Update();
        ActualizarAnimacion();
    }

    protected override void Chase() { Vigilar(); }
    protected override void Patrol() { Vigilar(); }

    private void Vigilar()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        PlayerController presa = BuscarExpuesto();
        HasPrey = presa != null;

        if (presa != null)
        {
            // A tiro: no se mueve, apunta y dispara. Moverse mientras dispara lo
            // convertiría en un perseguidor, que es justo lo que no es.
            Parar();
            Mirar(presa.transform.position);
            if (_arma != null) _arma.Aim(presa.transform);
            return;
        }

        if (_arma != null) _arma.Cancelar();
        AtenderAvisos();
    }

    // Solo cuentan los jugadores EXPUESTOS: si está escondido, para el Hunter
    // no existe, por muy a la vista que lo tenga el rayo.
    private PlayerController BuscarExpuesto()
    {
        Vector3 ojos = transform.position + Vector3.up * eyeHeight;
        var jugadores = NetworkPlayer.AllPlayers;

        for (int i = 0; i < jugadores.Count; i++)
        {
            var p = jugadores[i];
            if (p == null) continue;

            var abatido = p.GetComponent<PlayerDownedState>();
            if (abatido != null && !abatido.CanAct) continue;

            if (HidingSpot.IsHidden(p)) continue;

            Vector3 pecho, cabeza;
            Perception.BodyPoints(p.transform, out pecho, out cabeza);

            if (Perception.CanSee(ojos, transform.forward, pecho, watchRange, visionAngle,
                                  sightBlockers, transform, p.transform))
                return p;
        }

        return null;
    }

    private void AtenderAvisos()
    {
        Alert aviso;
        if (AlertSystem.TryGetNearest(transform.position, alertRange, out aviso))
        {
            // Se coloca a distancia de tiro del punto avisado, no encima
            Vector3 haciaMi = transform.position - aviso.position;
            haciaMi.y = 0f;
            if (haciaMi.sqrMagnitude < 0.01f) haciaMi = Vector3.forward;

            Vector3 puesto = aviso.position + haciaMi.normalized * ambushDistance;

            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(puesto, out hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                _puestoDeVigilancia = hit.position;
                _tienePuesto = true;
            }
        }

        if (!_tienePuesto) { Parar(); return; }

        if (Vector3.Distance(transform.position, _puestoDeVigilancia) <= 1.5f)
        {
            _tienePuesto = false;   // ya está en su puesto: se queda quieto vigilando
            Parar();
            return;
        }

        agent.isStopped = false;
        agent.speed = moveSpeed;
        agent.SetDestination(_puestoDeVigilancia);
    }

    private void Parar()
    {
        if (agent.isStopped) return;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        agent.ResetPath();
    }

    // Parado, el NavMeshAgent no gira: hay que orientarlo a mano para que no
    // dispare de espaldas.
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
        Gizmos.color = new Color(1f, 0.8f, 0f, 0.7f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * eyeHeight, watchRange);
    }
}
