using UnityEngine;

/// <summary>
/// "El Cobarde" (enemigo 5 de la especificación, segundo tercio).
///
/// Ataca desde lejos y se niega a acercarse. Su función no es matarte: es
/// impedir que te atrincheres. Si te quedas quieto en una esquina segura
/// aguantando a los comunes, el Cobarde te va picando desde fuera y te obliga a
/// moverte, que es justo donde te esperan los demás.
///
/// LA REGLA QUE LO DEFINE: mantiene una FRANJA de distancia, no una distancia
/// exacta. Si te acercas retrocede, si te alejas avanza, y en medio se queda
/// quieto y dispara. Sin la franja estaría corrigiendo la posición cada frame y
/// se vería como un bicho nervioso yendo y viniendo.
///
/// EN RED: la IA es del servidor, como el resto.
/// </summary>
[RequireComponent(typeof(EnemyRangedAttack))]
public class CowardEnemy : EnemyBehaviour
{
    [Header("Distancia que quiere mantener")]
    [Tooltip("Más cerca que esto y retrocede")]
    public float minDistance = 10f;
    [Tooltip("Más lejos que esto y se acerca")]
    public float maxDistance = 18f;

    [Header("Detección")]
    public float visionRange = 30f;
    public float visionAngle = 120f;
    public float eyeHeight = 1.6f;
    public float hearingAcuity = 1f;
    public LayerMask sightBlockers = ~0;

    [Header("Movimiento")]
    public float repositionSpeed = 5f;
    [Tooltip("Si lo tienes a menos de esto, deja de huir y pelea de cerca: " +
             "acorralado no puede seguir retrocediendo eternamente.")]
    public float meleePanicDistance = 3f;

    [Header("Animación (opcional)")]
    public string moveState = "";
    public string idleState = "";
    public float clipSpeed = 4.5f;

    private EnemyRangedAttack _arma;
    private PlayerController _objetivo;

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
        if (IsServer && agent != null) agent.speed = repositionSpeed;
    }

    protected override void Update()
    {
        base.Update();
        ActualizarAnimacion();
    }

    protected override void Chase() { Pensar(); }
    protected override void Patrol() { Pensar(); }

    private void Pensar()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        _objetivo = Detectar();

        if (_objetivo == null)
        {
            if (_arma != null) _arma.Cancelar();
            Rondar();
            return;
        }

        Vector3 haciaMi = transform.position - _objetivo.transform.position;
        haciaMi.y = 0f;
        float distancia = haciaMi.magnitude;

        Colocarse(distancia, haciaMi);

        // Dispara mientras esté en franja o lejos; si lo tienen encima, no.
        if (_arma != null && distancia > meleePanicDistance) _arma.Aim(_objetivo.transform);
        else if (_arma != null) _arma.Cancelar();

        Mirar(_objetivo.transform.position);
    }

    private void Colocarse(float distancia, Vector3 haciaMi)
    {
        agent.speed = repositionSpeed;

        // Acorralado: ya no retrocede más, se queda y pelea de cerca (el daño por
        // contacto de la clase base hace el resto).
        if (distancia <= meleePanicDistance)
        {
            Parar();
            return;
        }

        if (distancia < minDistance)
        {
            // Demasiado cerca: se aleja en línea recta desde el jugador
            Vector3 huida = transform.position + haciaMi.normalized * (maxDistance - distancia);
            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(huida, out hit, 6f, UnityEngine.AI.NavMesh.AllAreas))
            {
                agent.isStopped = false;
                agent.SetDestination(hit.position);
            }
            return;
        }

        if (distancia > maxDistance)
        {
            agent.isStopped = false;
            agent.SetDestination(_objetivo.transform.position);
            return;
        }

        // En franja: quieto y disparando. Aquí es donde pasa la mayor parte del
        // tiempo, y por eso hay franja y no una distancia exacta.
        Parar();
    }

    private void Parar()
    {
        if (agent.isStopped) return;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        agent.ResetPath();
    }

    private void Rondar()
    {
        agent.isStopped = false;
        agent.speed = repositionSpeed * 0.6f;

        if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance < 1f))
            SetNewPatrolTarget();
    }

    // Quieto y disparando tiene que MIRAR a quien dispara: el NavMeshAgent solo
    // gira cuando se mueve, así que parado se quedaría de espaldas.
    private void Mirar(Vector3 objetivo)
    {
        Vector3 dir = objetivo - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, Quaternion.LookRotation(dir, Vector3.up),
            agent.angularSpeed * Time.deltaTime);
    }

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

        // Se comprueba el estado REAL, no una cadena cacheada: si algo saca al
        // Animator de su sitio, al frame siguiente vuelve solo.
        if (!_anim.GetCurrentAnimatorStateInfo(0).IsName(deseado) && !_anim.IsInTransition(0))
        {
            _anim.CrossFade(deseado, 0.15f);
            _estadoAnim = deseado;
        }
        _anim.speed = moviendose && clipSpeed > 0.01f
            ? Mathf.Clamp(_velocidadObservada / clipSpeed, 0.4f, 2f) : 1f;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, minDistance);
        Gizmos.color = new Color(0f, 0.6f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, maxDistance);
    }
}
