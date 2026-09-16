using UnityEngine;

/// <summary>
/// "El Tanque" (enemigo 6 de la especificación, segundo tercio, raro).
///
/// Lento, enorme y letal: si te alcanza, se acabó. No se combate, se esquiva.
///
/// LA REGLA QUE LO DEFINE: no cabe en los interiores. Meterse en un edificio
/// deja de ser una posición cómoda y pasa a ser LA respuesta al Tanque, y eso
/// cambia cómo lees el mapa entero. Por eso su daño puede ser absurdo sin que
/// sea injusto: siempre tienes una salida, pero tienes que conocerla.
///
/// Gira despacio a propósito. Un tanque que te sigue con la agilidad de un
/// zombi normal no da sensación de mole, y además rodearlo tiene que ser una
/// táctica válida.
///
/// EN RED: la IA es del servidor, como el resto.
/// </summary>
public class TankEnemy : EnemyBehaviour
{
    [Header("Mole")]
    public float chaseSpeed = 3.2f;
    [Tooltip("Grados por segundo. Bajo a propósito: rodearlo debe funcionar.")]
    public float turnSpeed = 90f;

    [Tooltip("A qué distancia del jugador se planta cuando este se ha metido " +
             "en un sitio donde el Tanque no cabe")]
    public float waitDistance = 2f;

    [Header("Animación (opcional)")]
    public string moveState = "";
    public string idleState = "";
    public float clipSpeed = 3f;

    /// <summary>Te tiene fichado pero no puede llegar: estás a cubierto.</summary>
    public bool IsBlocked { get; private set; }

    private Animator _anim;
    private string _estadoAnim = "";
    private Vector3 _posicionAnterior;
    private float _velocidadObservada;

    protected override void Awake()
    {
        base.Awake();
        alwaysAggro = true;   // no patrulla: va a por ti desde el principio

        _anim = GetComponentInChildren<Animator>(true);
        _posicionAnterior = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && agent != null)
        {
            agent.speed = chaseSpeed;
            agent.angularSpeed = turnSpeed;
            SetSpeed(chaseSpeed);
        }
    }

    protected override void Update()
    {
        base.Update();
        ActualizarAnimacion();
    }

    protected override void Chase() { Avanzar(); }
    protected override void Patrol() { Avanzar(); }

    private void Avanzar()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        if (player == null) return;

        // Si el Tanque mismo ha acabado dentro de una zona prohibida (lo han
        // colocado mal, o el mapa ha cambiado), lo primero es salir de ahí.
        if (TankForbiddenZone.AnyContains(transform.position))
        {
            IrA(TankForbiddenZone.PushOut(transform.position, waitDistance));
            return;
        }

        Vector3 destino = player.position;
        IsBlocked = TankForbiddenZone.AnyContains(destino);

        if (IsBlocked)
        {
            // Te has metido donde no cabe: se planta lo más cerca que puede.
            // No se va, no se rinde: te espera fuera.
            destino = TankForbiddenZone.PushOut(destino, waitDistance);
        }

        IrA(destino);
    }

    private void IrA(Vector3 destino)
    {
        UnityEngine.AI.NavMeshHit hit;
        if (!UnityEngine.AI.NavMesh.SamplePosition(destino, out hit, 8f, UnityEngine.AI.NavMesh.AllAreas))
            return;

        agent.isStopped = false;
        agent.speed = chaseSpeed;
        agent.angularSpeed = turnSpeed;
        PedirDestino(hit.position);
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

        bool moviendose = _velocidadObservada > 0.3f;
        string deseado = moviendose ? moveState : idleState;
        if (string.IsNullOrEmpty(deseado)) return;

        // Se comprueba el estado REAL, no una cadena cacheada: si algo saca al
        // Animator de su sitio, al frame siguiente vuelve solo.
        if (!_anim.GetCurrentAnimatorStateInfo(0).IsName(deseado) && !_anim.IsInTransition(0))
        {
            _anim.CrossFade(deseado, 0.2f);
            _estadoAnim = deseado;
        }
        _anim.speed = moviendose && clipSpeed > 0.01f
            ? Mathf.Clamp(_velocidadObservada / clipSpeed, 0.4f, 1.6f) : 1f;
    }
}
