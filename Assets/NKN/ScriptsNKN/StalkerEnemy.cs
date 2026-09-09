using UnityEngine;

/// <summary>
/// El Acechador: avanza hacia ti mientras nadie lo mira y se queda petrificado
/// en el instante en que entra en el campo de visión de alguien.
///
/// La gracia del bicho no es la velocidad, es que TÚ decides cuándo se mueve.
/// Mirarlo lo detiene, pero mientras lo miras no miras a nada más; y en cuanto
/// giras la cabeza, avanza. Con dos jugadores se vuelve un problema de
/// coordinación: alguien tiene que quedarse vigilándolo.
///
/// EN RED: toda la decisión es del SERVIDOR, como el resto de enemigos. Los
/// clientes solo ven el resultado por NetworkTransform.
/// </summary>
public class StalkerEnemy : EnemyBehaviour
{
    [Header("Acecho")]
    [Tooltip("Velocidad a la que avanza cuando nadie lo mira")]
    public float stalkSpeed = 6.5f;

    [Tooltip("Ángulo total del cono de visión del jugador, en grados. " +
             "Algo más ancho que el FOV de la cámara: si lo ves de reojo, cuenta.")]
    public float visionAngle = 120f;

    [Tooltip("Más lejos de esto no se le considera visto, aunque esté de frente")]
    public float maxSightDistance = 60f;

    [Tooltip("Si llega a esta distancia del jugador, mirarlo YA NO lo detiene: " +
             "sigue avanzando y atacando. Es el punto de no retorno — si lo has " +
             "dejado acercarse tanto, la mirada ya no te salva.")]
    public float attackRadius = 4f;

    [Tooltip("Qué capas cortan la línea de visión. Si hay un muro por medio, " +
             "mirar hacia él no lo congela.")]
    public LayerMask sightBlockers = ~0;

    // Congelado o no, visible para todos. Ahora mismo solo lo usa el propio
    // enemigo, pero lo necesitará la animación cuando el modelo tenga clips.
    private readonly Unity.Netcode.NetworkVariable<bool> netFrozen =
        new Unity.Netcode.NetworkVariable<bool>(
            false,
            Unity.Netcode.NetworkVariableReadPermission.Everyone,
            Unity.Netcode.NetworkVariableWritePermission.Server);

    public bool IsFrozen => netFrozen.Value;

    private bool _congelado;

    // La animacion la lleva entera EnemyLocomotionAnimator: mide la velocidad del
    // transform (el agente esta apagado en los clientes), decide idle/walk/run con
    // histeresis y ajusta el ritmo del clip. Aqui solo hay que decirle cuando
    // congelarse.
    private EnemyLocomotionAnimator _loco;

    protected override void Awake()
    {
        base.Awake();

        // Va SIEMPRE a por ti: no patrulla ni espera a que entres en su radio.
        // Lo único que lo detiene es que lo miren.
        alwaysAggro = true;

        _loco = GetComponent<EnemyLocomotionAnimator>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && agent != null)
        {
            agent.speed = stalkSpeed;
            SetSpeed(stalkSpeed);
        }
    }

    // OJO: base.Update() lleva dentro su propio "si no soy el servidor, salgo",
    // pero eso solo corta EL MÉTODO DE LA CLASE PADRE. Lo que va después sigue
    // ejecutándose aquí, que es justo lo que hace falta: la IA es del servidor,
    // la animación tiene que verse en todas las máquinas.
    protected override void Update()
    {
        base.Update();
        ActualizarAnimacion();
    }

    // Persiguiendo y patrullando hace lo mismo: el acecho no tiene dos modos.
    protected override void Chase() { Acechar(); }
    protected override void Patrol() { Acechar(); }

    // ---------- Animación ----------

    // Congelar el Animator lo deja EXACTAMENTE en el fotograma en que estaba: a
    // media zancada, con el peso en un pie y un brazo levantado. Eso es lo que
    // hace el efecto. Saltar a una pose de reposo sería mucho peor, porque se
    // vería el cambio y delataría que hay una máquina de estados detrás.
    // Congelado de verdad significa congelado donde estuviera.
    private void ActualizarAnimacion()
    {
        if (_loco != null) _loco.Frozen = IsFrozen;
    }

    private void Acechar()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        // Dentro del radio de ataque la regla de la mirada deja de aplicarse.
        // Sin esto el enemigo es inofensivo: bastaría con mirarlo fijamente para
        // dejarlo clavado a un metro de ti para siempre.
        if (!YaMeTienesEncima() && AlguienMeVe())
        {
            Congelar();
            return;
        }

        Avanzar();
    }

    private bool YaMeTienesEncima()
    {
        if (player == null) return false;
        return Vector3.Distance(transform.position, player.position) <= attackRadius;
    }

    // ---------- Los dos estados ----------

    private void Congelar()
    {
        if (_congelado) return;
        _congelado = true;
        netFrozen.Value = true;

        // En seco, sin frenada. Si solo se pusiera isStopped, el agente seguiría
        // deslizándose un poco por su velocidad acumulada y se notaría el truco.
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        agent.ResetPath();
    }

    private void Avanzar()
    {
        if (_congelado)
        {
            _congelado = false;
            netFrozen.Value = false;
            agent.isStopped = false;
        }

        if (player != null) agent.SetDestination(player.position);
    }

    // ---------- ¿Me está viendo alguien? ----------

    private bool AlguienMeVe()
    {
        var jugadores = NetworkPlayer.AllPlayers;

        for (int i = 0; i < jugadores.Count; i++)
        {
            var p = jugadores[i];
            if (p == null) continue;

            // Un compañero en el suelo no vigila a nadie. Además así no se puede
            // usar a un abatido como torreta para dejarlo clavado.
            var abatido = p.GetComponent<PlayerDownedState>();
            if (abatido != null && !abatido.CanAct) continue;

            if (MeVe(p)) return true;
        }

        // OJO: los NPC NO lo congelan. No tienen cámara ni "mirada" de verdad, y
        // si contaran, unos cuantos deambulando lo dejarían paralizado sin que el
        // jugador entienda por qué. Además así la mecánica se puede probar en
        // solitario con NPC por el mapa.
        return false;
    }

    private bool MeVe(PlayerController jugador)
    {
        Vector3 ojos = PosicionOjos(jugador);

        // Se prueban dos puntos: el pecho y la coronilla. Con uno solo, asomar la
        // cabeza por encima de una caja no contaría como verlo.
        Vector3 pecho, cabeza;
        PuntosVisibles(out pecho, out cabeza);

        return PuntoVisible(jugador, ojos, pecho) || PuntoVisible(jugador, ojos, cabeza);
    }

    private bool PuntoVisible(PlayerController jugador, Vector3 ojos, Vector3 punto)
    {
        Vector3 haciaMi = punto - ojos;
        float distancia = haciaMi.magnitude;

        if (distancia > maxSightDistance || distancia < 0.01f) return false;

        // Solo cuenta el giro horizontal: mirar al suelo o al cielo NO lo libera.
        // Es a propósito, y además es lo único fiable: el cuerpo del jugador gira
        // con el ratón y eso se replica, pero el cabeceo vive en la cámara, que en
        // las copias remotas no se actualiza.
        Vector3 mirada = jugador.transform.forward;
        mirada.y = 0f;

        Vector3 plano = haciaMi;
        plano.y = 0f;

        if (mirada.sqrMagnitude < 0.0001f || plano.sqrMagnitude < 0.0001f) return false;
        if (Vector3.Angle(mirada, plano) > visionAngle * 0.5f) return false;

        return HayLineaDeVision(ojos, punto, jugador.transform);
    }

    // Verdadero si entre los ojos y el punto no se cruza nada. Se descartan los
    // impactos contra el propio jugador (la cámara va dentro de su cápsula) y
    // contra este mismo enemigo (que es justo el destino).
    private bool HayLineaDeVision(Vector3 ojos, Vector3 destino, Transform jugador)
    {
        Vector3 direccion = destino - ojos;
        float distancia = direccion.magnitude;

        var impactos = Physics.RaycastAll(ojos, direccion.normalized, distancia,
                                          sightBlockers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < impactos.Length; i++)
        {
            Transform t = impactos[i].collider.transform;
            if (t.IsChildOf(jugador)) continue;
            if (t.IsChildOf(transform)) continue;

            return false;   // hay un muro (o cualquier cosa) por medio
        }

        return true;
    }

    // ---------- Geometría del propio bicho ----------

    private Vector3 PosicionOjos(PlayerController jugador)
    {
        // La cámara del jugador: su POSICIÓN sí es fiable en todas las máquinas
        // (cuelga del cuerpo). Lo que no vale es su rotación en las copias remotas.
        var cam = jugador.GetComponentInChildren<Camera>(true);
        if (cam != null) return cam.transform.position;

        var cc = jugador.GetComponent<CharacterController>();
        float alto = cc != null ? cc.height : 2f;
        return jugador.transform.position + Vector3.up * (alto * 0.9f);
    }

    private void PuntosVisibles(out Vector3 pecho, out Vector3 cabeza)
    {
        var cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            Vector3 baseCuerpo = transform.TransformPoint(cc.center) - Vector3.up * (cc.height * 0.5f);
            pecho = baseCuerpo + Vector3.up * (cc.height * 0.55f);
            cabeza = baseCuerpo + Vector3.up * (cc.height * 0.95f);
            return;
        }

        pecho = transform.position + Vector3.up * 1.0f;
        cabeza = transform.position + Vector3.up * 1.8f;
    }

    void OnDrawGizmosSelected()
    {
        Vector3 pecho, cabeza;
        PuntosVisibles(out pecho, out cabeza);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(pecho, 0.15f);
        Gizmos.DrawWireSphere(cabeza, 0.15f);
    }
}
