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

    [Header("Ataque")]
    [Tooltip("A qué distancia lanza el zarpazo. Tiene que ser MENOR que " +
             "attackRadius, o intentaría atacar desde fuera del punto de no retorno.")]
    public float attackRange = 1.8f;
    [Tooltip("Segundos entre zarpazo y zarpazo")]
    public float attackCooldown = 2.5f;
    [Tooltip("Daño de cada zarpazo. OJO: el jugador tiene 1000 de vida.")]
    public float attackDamage = 40f;
    [Tooltip("Segundos desde que arranca la animación hasta que entra el golpe. " +
             "Es la ventana para apartarse: sin ella el zarpazo es inesquivable.")]
    public float attackWindup = 0.55f;
    [Tooltip("Cuánto se queda clavado atacando. El clip dura 2,73 s pero dejarlo " +
             "entero lo inmoviliza demasiado.")]
    public float attackDuration = 1.5f;
    [Tooltip("Estado del Animator con el zarpazo")]
    public string attackState = "attack";

    [Tooltip("El clip Attack del Slender es un PLACAJE: salta 2,13 m de alto y " +
             "avanza 1,37 m. Con esto activo el bicho se desplaza hacia la victima " +
             "mientras dura el salto, para que el brinco lleve a alguna parte. " +
             "Desactivalo si cambias el modelo por uno cuyo ataque sea plantado.")]
    public bool ataqueConPlacaje = true;
    [Tooltip("A que velocidad cubre la distancia durante el placaje (m/s)")]
    public float lungeSpeed = 4f;

    [Header("Grito")]
    [Tooltip("Grita al entrar en el radio de no retorno. Es el aviso de que " +
             "mirarlo ya no te salva: sin esto el jugador no tiene forma de saber " +
             "que ha cruzado esa linea.")]
    public bool gritaAlAcercarse = true;
    [Tooltip("Estado del Animator con el grito")]
    public string screamState = "scream";
    [Tooltip("Cuanto se queda clavado gritando. Es lo que te da margen para huir; " +
             "el clip entero dura 2,80 s.")]
    public float screamDuration = 1.6f;
    [Tooltip("Sonido del grito (opcional)")]
    public AudioClip screamSound;
    public float screamVolume = 1f;

    [Header("Sonido al ser mirado")]
    [Tooltip("Suena en bucle mientras alguien lo esta mirando y se apaga al dejar " +
             "de mirarlo. Ademas de ambientar, es la unica pista SONORA de que lo " +
             "tienes congelado: sin esto solo lo sabes mirandolo, que es justo lo " +
             "que no puedes hacer todo el rato.")]
    public AudioClip sonidoAlSerMirado;
    public float volumenMirada = 1f;
    [Tooltip("Segundos que tarda en subir o bajar el volumen. Cortar en seco suena " +
             "a chasquido y delata el interruptor.")]
    public float fundidoMirada = 0.35f;

    // Congelado o no, visible para todos. Ahora mismo solo lo usa el propio
    // enemigo, pero lo necesitará la animación cuando el modelo tenga clips.
    private readonly Unity.Netcode.NetworkVariable<bool> netFrozen =
        new Unity.Netcode.NetworkVariable<bool>(
            false,
            Unity.Netcode.NetworkVariableReadPermission.Everyone,
            Unity.Netcode.NetworkVariableWritePermission.Server);

    public bool IsFrozen => netFrozen.Value;

    // Lo calcula el servidor, lo leen todos: el sonido de "te estoy mirando" tiene
    // que sonar en la maquina de cada jugador, no solo en la del anfitrion.
    private readonly Unity.Netcode.NetworkVariable<bool> netObservado =
        new Unity.Netcode.NetworkVariable<bool>(
            false,
            Unity.Netcode.NetworkVariableReadPermission.Everyone,
            Unity.Netcode.NetworkVariableWritePermission.Server);

    /// <summary>Alguien lo tiene en su campo de vision ahora mismo.</summary>
    public bool EstaSiendoMirado { get { return netObservado.Value; } }

    private bool _congelado;
    private float _tiempoHastaOtroAtaque;
    private bool _atacando;
    private bool _yaHaGritado;
    private bool _gritando;
    private AudioSource _audio;

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
        _audio = GetComponentInChildren<AudioSource>(true);
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
        // ANTES de base.Update(): ahi dentro corre Acechar(), que necesita el dato
        // ya puesto. Y se calcula una sola vez por frame porque lleva raycasts:
        // llamarlo dos veces duplicaria el coste con varios acechadores en escena.
        if (IsServer) netObservado.Value = AlguienMeVe();

        base.Update();

        ActualizarAnimacion();
        ActualizarSonidoDeMirada();

        if (IsServer && _tiempoHastaOtroAtaque > 0f)
            _tiempoHastaOtroAtaque -= Time.deltaTime;
    }

    // Corre en TODAS las maquinas: cada jugador tiene que oirlo desde donde esta.
    private void ActualizarSonidoDeMirada()
    {
        if (_audio == null || sonidoAlSerMirado == null) return;

        bool visto = netObservado.Value;

        if (visto && !_audio.isPlaying)
        {
            _audio.clip = sonidoAlSerMirado;
            _audio.loop = true;
            _audio.volume = 0f;
            _audio.Play();
        }

        float objetivo = visto ? volumenMirada : 0f;
        _audio.volume = fundidoMirada > 0.01f
            ? Mathf.MoveTowards(_audio.volume, objetivo, (volumenMirada / fundidoMirada) * Time.deltaTime)
            : objetivo;

        // Solo se para cuando el fundido ha terminado, o se oiria el corte
        if (!visto && _audio.isPlaying && _audio.volume <= 0.001f) _audio.Stop();
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

        // Mientras dura el zarpazo o el grito no se mueve ni se congela.
        if (_atacando || _gritando) return;

        bool meTieneEncima = YaMeTienesEncima();

        // Se rearma con holgura, para que ir y venir por el borde del radio no
        // lo deje gritando sin parar
        if (!meTieneEncima && _yaHaGritado && player != null &&
            Vector3.Distance(transform.position, player.position) > attackRadius * 1.3f)
            _yaHaGritado = false;

        // El grito marca el cruce del punto de no retorno
        if (meTieneEncima && !_yaHaGritado && gritaAlAcercarse && !string.IsNullOrEmpty(screamState))
        {
            Gritar();
            return;
        }

        // Dentro del radio de ataque la regla de la mirada deja de aplicarse.
        // Sin esto el enemigo es inofensivo: bastaría con mirarlo fijamente para
        // dejarlo clavado a un metro de ti para siempre.
        if (!meTieneEncima && netObservado.Value)
        {
            Congelar();
            return;
        }

        if (PuedeAtacar())
        {
            LanzarZarpazo();
            return;
        }

        Avanzar();
    }

    // ---------- Grito ----------

    private void Gritar()
    {
        _yaHaGritado = true;
        _gritando = true;

        if (_congelado) { _congelado = false; netFrozen.Value = false; }
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        if (player != null)
        {
            Vector3 hacia = player.position - transform.position;
            hacia.y = 0f;
            if (hacia.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(hacia);
        }

        GritoRpc();
        StartCoroutine(RutinaDelGrito());
    }

    [Unity.Netcode.Rpc(Unity.Netcode.SendTo.Everyone)]
    private void GritoRpc()
    {
        if (_loco != null) _loco.PlayOneShot(screamState, screamDuration, 0.05f);
        if (_audio != null && screamSound != null)
            _audio.PlayOneShot(screamSound, screamVolume);
    }

    private System.Collections.IEnumerator RutinaDelGrito()
    {
        yield return new WaitForSeconds(screamDuration);

        _gritando = false;
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = false;
    }

    // ---------- Zarpazo ----------

    private bool PuedeAtacar()
    {
        if (player == null || _tiempoHastaOtroAtaque > 0f) return false;
        if (string.IsNullOrEmpty(attackState)) return false;

        // Al abatido no se le remata: si no, no habría forma de levantarlo
        var abatido = player.GetComponent<PlayerDownedState>();
        if (abatido != null && !abatido.CanAct) return false;

        return Vector3.Distance(transform.position, player.position) <= attackRange;
    }

    private void LanzarZarpazo()
    {
        _atacando = true;
        _tiempoHastaOtroAtaque = attackCooldown;

        // Se planta para golpear. Atacar caminando haría que el golpe pareciera
        // un atropello en vez de un zarpazo.
        if (_congelado) { _congelado = false; netFrozen.Value = false; }
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        // Se encara a la víctima: el clip golpea de frente
        Vector3 haciaLaVictima = player.position - transform.position;
        haciaLaVictima.y = 0f;
        if (haciaLaVictima.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(haciaLaVictima);

        ZarpazoRpc();                       // la animación tiene que verse en TODAS las máquinas
        StartCoroutine(RutinaDelZarpazo());
    }

    // La decisión es del servidor, pero la animación la ve todo el mundo
    [Unity.Netcode.Rpc(Unity.Netcode.SendTo.Everyone)]
    private void ZarpazoRpc()
    {
        if (_loco != null) _loco.PlayOneShot(attackState, attackDuration);
    }

    private System.Collections.IEnumerator RutinaDelZarpazo()
    {
        // Ventana para apartarse: el golpe no entra al final del salto.
        //
        // Durante ese tramo se le acompana el desplazamiento. El clip lleva el
        // salto dentro, pero el movimiento horizontal se descarta (applyRootMotion
        // esta apagado para no pelear contra el NavMeshAgent), asi que sin esto
        // brinca en el sitio y parece un bote sin sentido.
        float t = 0f;
        while (t < attackWindup)
        {
            t += Time.deltaTime;

            if (ataqueConPlacaje && player != null &&
                agent != null && agent.enabled && agent.isOnNavMesh)
            {
                Vector3 hacia = player.position - transform.position;
                hacia.y = 0f;
                float dist = hacia.magnitude;

                // Se para a 0.8 m: lo justo para tocarte sin empotrarse dentro
                if (dist > 0.8f)
                {
                    float paso = Mathf.Min(dist - 0.8f, lungeSpeed * Time.deltaTime);
                    agent.Move(hacia.normalized * paso);   // Move respeta el NavMesh
                    transform.rotation = Quaternion.LookRotation(hacia);
                }
            }

            yield return null;
        }

        if (player != null &&
            Vector3.Distance(transform.position, player.position) <= attackRange + 0.4f)
        {
            var victima = player.GetComponent<Character>();
            var abatido = player.GetComponent<PlayerDownedState>();
            bool enPie = abatido == null || abatido.CanAct;

            if (victima != null && enPie)
            {
                // El golpe entra por donde está el bicho y empuja hacia la víctima,
                // para que el ragdoll caiga en la dirección del zarpazo
                Vector3 punto = player.position + Vector3.up * 1.2f;
                Vector3 direccion = player.position - transform.position;
                direccion.y = 0f;
                victima.TakeDamage(attackDamage, punto, direccion);
            }
        }

        yield return new WaitForSeconds(Mathf.Max(0f, attackDuration - attackWindup));

        _atacando = false;
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = false;
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

        if (player != null) PedirDestino(player.position);
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
