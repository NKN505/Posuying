using UnityEngine;

/// <summary>
/// "El Psycho-Killer" (enemigo 8 de la especificación, tercer tercio, semicomún).
///
/// Francotirador. Dispara desde muy lejos, se reposiciona cada poco y NUNCA se
/// acerca a buscarte. Controla áreas enteras del mapa: mientras esté vivo, los
/// espacios abiertos dejan de ser transitables.
///
/// LA REGLA QUE LO DEFINE: si estás escondido, no puede hacer nada. No entra a
/// buscarte, no se acerca, no te saca de ahí. Se limita a esperar a que salgas.
/// Eso es lo que convierte al Alertador en el enemigo peligroso del tercio: el
/// Psycho-Killer solo es letal si alguien te obliga a moverte.
///
/// El reposicionamiento no es decorativo: si se quedara fijo, aprenderías dónde
/// está y lo evitarías para siempre. Cambiando de sitio obliga a volver a leer
/// el terreno cada pocos minutos.
///
/// EN RED: la IA es del servidor, como el resto.
/// </summary>
[RequireComponent(typeof(EnemyRangedAttack))]
public class PsychoKillerEnemy : EnemyBehaviour
{
    [Header("Alcance")]
    [Tooltip("No dispara a nadie más cerca de esto: si lo tienes encima, su " +
             "trabajo ya ha fallado")]
    public float minRange = 25f;
    public float maxRange = 70f;
    public float visionAngle = 140f;
    public float eyeHeight = 1.7f;
    public LayerMask sightBlockers = ~0;

    [Header("Reposicionamiento")]
    [Tooltip("Cada cuánto busca un nido nuevo")]
    public float repositionInterval = 20f;
    [Tooltip("Cuántos puntos prueba al buscar nido. Más = mejores posiciones y " +
             "más coste; se hace muy de vez en cuando, así que sale barato.")]
    public int candidates = 14;
    public float moveSpeed = 4f;

    [Header("Animación (opcional)")]
    public string moveState = "";
    public string idleState = "";
    public float clipSpeed = 4f;

    public bool IsAiming { get { return _arma != null && _arma.IsAiming; } }

    private EnemyRangedAttack _arma;
    private float _relojReposicion;
    private Vector3 _nido;
    private bool _yendoAlNido;

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
        _relojReposicion = repositionInterval;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && _arma != null)
        {
            _arma.range = maxRange;
            if (agent != null) agent.speed = moveSpeed;
        }
    }

    protected override void Update()
    {
        base.Update();
        ActualizarAnimacion();
    }

    protected override void Chase() { Acechar(); }
    protected override void Patrol() { Acechar(); }

    private void Acechar()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        _relojReposicion -= Time.deltaTime;

        PlayerController presa = BuscarExpuesto();

        if (presa != null && !_yendoAlNido)
        {
            Parar();
            Mirar(presa.transform.position);
            if (_arma != null) _arma.Aim(presa.transform);

            // Disparar delata la posición, así que después toca mudarse
            if (_relojReposicion <= 0f) BuscarNido(presa.transform.position);
            return;
        }

        if (_arma != null) _arma.Cancelar();

        // Sin nadie a la vista: se muda de vez en cuando para cubrir otra zona.
        // No va a buscar a nadie; solo cambia el área que vigila.
        if (_relojReposicion <= 0f && !_yendoAlNido)
        {
            var referencia = JugadorMasCercano();
            BuscarNido(referencia != null ? referencia.transform.position : transform.position);
        }

        IrAlNido();
    }

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

            // Escondido = intocable para él. No entra a sacarte.
            if (HidingSpot.IsHidden(p)) continue;

            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < minRange || d > maxRange) continue;

            Vector3 pecho, cabeza;
            Perception.BodyPoints(p.transform, out pecho, out cabeza);

            if (Perception.CanSee(ojos, transform.forward, pecho, maxRange, visionAngle,
                                  sightBlockers, transform, p.transform))
                return p;
        }

        return null;
    }

    // Un nido bueno: lejos del jugador, sobre NavMesh y con vista despejada.
    // Se prueban varios candidatos al azar y se elige el mejor; buscar la
    // posición óptima de verdad costaría mucho más y no se notaría.
    private void BuscarNido(Vector3 objetivo)
    {
        _relojReposicion = repositionInterval;

        Vector3 mejor = Vector3.zero;
        float mejorNota = float.MinValue;
        bool encontrado = false;

        for (int i = 0; i < candidates; i++)
        {
            float angulo = Random.Range(0f, Mathf.PI * 2f);
            float radio = Random.Range(minRange + 5f, maxRange * 0.85f);
            Vector3 candidato = objetivo + new Vector3(Mathf.Cos(angulo), 0f, Mathf.Sin(angulo)) * radio;

            UnityEngine.AI.NavMeshHit hit;
            if (!UnityEngine.AI.NavMesh.SamplePosition(candidato, out hit, 8f, UnityEngine.AI.NavMesh.AllAreas))
                continue;

            Vector3 ojos = hit.position + Vector3.up * eyeHeight;
            if (!Perception.HasLineOfSight(ojos, objetivo + Vector3.up * 1.2f, sightBlockers, transform))
                continue;

            // Cuanto más lejos, mejor: es un francotirador
            float nota = Vector3.Distance(hit.position, objetivo);
            if (nota <= mejorNota) continue;

            mejorNota = nota;
            mejor = hit.position;
            encontrado = true;
        }

        if (!encontrado) return;

        _nido = mejor;
        _yendoAlNido = true;
    }

    private void IrAlNido()
    {
        if (!_yendoAlNido) { Parar(); return; }

        agent.isStopped = false;
        agent.speed = moveSpeed;
        PedirDestino(_nido);

        if (Vector3.Distance(transform.position, _nido) <= 2f)
            _yendoAlNido = false;
    }

    private PlayerController JugadorMasCercano()
    {
        PlayerController mejor = null;
        float mejorDistancia = float.MaxValue;
        var jugadores = NetworkPlayer.AllPlayers;

        for (int i = 0; i < jugadores.Count; i++)
        {
            var p = jugadores[i];
            if (p == null) continue;

            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d >= mejorDistancia) continue;
            mejorDistancia = d; mejor = p;
        }

        return mejor;
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
}
