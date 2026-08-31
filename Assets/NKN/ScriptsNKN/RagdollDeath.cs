using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Muerte con ragdoll al estilo Punisher (PS2, 2004): se lanza un clip de muerte
/// muy corto y, antes de que termine, el control pasa a la fisica. El cuerpo no
/// "reproduce una caida", la caida ocurre de verdad.
///
/// Va en la raiz del personaje, junto al Animator. Sirve igual para el jugador,
/// los NPC y los enemigos: solo pide un rig humanoide con el ragdoll montado.
///
/// -------------------------------------------------------------------------
/// POR QUE EL CLIP SE CORTA
/// -------------------------------------------------------------------------
/// Un clip de muerte entero siempre cae igual, atraviesa las paredes y termina
/// en una pose fija. Un ragdoll desde el frame 0 se desploma como un saco, sin
/// intencion. Los primeros 0.6 s del clip dan la reaccion (el cuerpo acusa el
/// golpe) y a partir de ahi la fisica da la caida, que ya responde al terreno,
/// a las escaleras y a lo que haya alrededor.
///
/// -------------------------------------------------------------------------
/// POR QUE SE COPIA LA VELOCIDAD DE LOS HUESOS
/// -------------------------------------------------------------------------
/// Si al activar el ragdoll los rigidbodies arrancan quietos, el movimiento del
/// clip se corta en seco y el cuerpo parece congelarse un instante antes de
/// caer. Por eso mientras suena el clip se mide cuanto se mueve cada hueso, y
/// en el traspaso esa velocidad se le entrega a su rigidbody: el cuerpo sigue
/// yendo hacia donde ya iba.
/// </summary>
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public class RagdollDeath : MonoBehaviour
{
    // Los mismos numeros que las condiciones DeathType del Animator.
    public const int MuerteFrontal = 0;
    public const int MuerteEspalda = 1;
    public const int MuerteAgachado = 2;

    [Header("Referencias")]
    [SerializeField] private Animator animator;
    [Tooltip("Componentes que hay que apagar al morir: el controlador, los " +
             "scripts procedurales, el CharacterController... Si se deja vacio " +
             "se buscan solos los mas habituales.")]
    [SerializeField] private Behaviour[] componentesADesactivar;

    [Header("Capa del ragdoll")]
    [Tooltip("Capa que se asigna a los huesos. Debe estar configurada en Physics " +
             "para no chocar consigo misma.")]
    [SerializeField] private string capaRagdoll = "Ragdoll";
    [Tooltip("Mientras el personaje esta vivo los colliders del ragdoll estorban: " +
             "chocan con el suelo y los recogen los raycasts. Se apagan y solo se " +
             "encienden al morir.")]
    [SerializeField] private bool apagarCollidersEnVida = true;

    [Header("Traspaso a la fisica")]
    [Tooltip("Segundos de clip antes de soltar el ragdoll. Por encima de 1 s la " +
             "caida se vuelve animada; por debajo de 0.3 s no da tiempo a leer el " +
             "golpe.")]
    [Range(0f, 1.5f)] [SerializeField] private float duracionClip = 0.6f;
    [Tooltip("Cuanto se conserva de la velocidad medida en los huesos. A 1 el " +
             "cuerpo sale disparado con toda la inercia del clip.")]
    [Range(0f, 1f)] [SerializeField] private float herenciaVelocidad = 1f;
    [Tooltip("Tope de la velocidad heredada, para que un frame perdido no mande " +
             "el cadaver al otro lado del mapa.")]
    [SerializeField] private float velocidadMaximaHeredada = 8f;

    [Header("Sobredano: cuanto te manda el golpe")]
    [Tooltip("Impulso por cada punto de dano que sobra despues de matarte. Un " +
             "disparo justo no te mueve; una escopeta a bocajarro si.")]
    [SerializeField] private float impulsoPorSobredano = 0.6f;
    [Tooltip("Tope del impulso.")]
    [SerializeField] private float impulsoMaximo = 900f;
    [Tooltip("Sobredano a partir del cual NO se reproduce clip: explosion, " +
             "escopetazo. El cuerpo sale volando directamente.")]
    [SerializeField] private float sobredanoQueSaltaElClip = 150f;
    [Tooltip("Componente hacia arriba del impulso. Sin esto el cuerpo se arrastra " +
             "por el suelo en vez de levantarse del golpe.")]
    [Range(0f, 1f)] [SerializeField] private float impulsoHaciaArriba = 0.35f;

    [Header("Depuracion")]
    [SerializeField] private bool depurar = false;

    [Header("Prueba (quitar antes de publicar)")]
    [Tooltip("Permite matar al personaje con una tecla para ver el ragdoll sin " +
             "necesidad de que nadie te dispare.")]
    [SerializeField] private bool pruebaActiva = false;
    [SerializeField] private KeyCode teclaDePrueba = KeyCode.M;
    [Tooltip("Sobredano simulado. Con 0 el cuerpo se desploma; subelo para ver " +
             "como el golpe lo manda mas lejos. Por encima de " +
             "'Sobredano Que Salta El Clip' sale volando sin animacion.")]
    [SerializeField] private float sobredanoDePrueba = 0f;
    [Tooltip("Si esta marcado, el tipo de muerte se sortea como en el juego. " +
             "Desmarcalo para forzar siempre el de abajo y comparar los tres.")]
    [SerializeField] private bool tipoAleatorioEnPrueba = true;
    [Range(0, 2)] [SerializeField] private int tipoForzadoEnPrueba = 0;

    private static readonly int HashDie       = Animator.StringToHash("Die");
    private static readonly int HashDeathType = Animator.StringToHash("DeathType");
    private static readonly int HashCrouch    = Animator.StringToHash("Crouch");
    private static readonly int HashGrounded  = Animator.StringToHash("IsGrounded");

    private readonly List<Rigidbody> _huesos = new List<Rigidbody>();
    private readonly List<Collider> _colliders = new List<Collider>();
    private Vector3[] _posAnterior;
    private Quaternion[] _rotAnterior;
    private Vector3[] _velLineal;
    private Vector3[] _velAngular;

    private CharacterController _controller;
    private bool _muerto;
    private bool _ragdollActivo;
    private bool _midiendo;
    private float _tiempoRestante;

    // Golpe pendiente de aplicar en el traspaso.
    private Vector3 _puntoImpacto, _dirImpacto;
    private float _sobredano;

    /// <summary>True desde que empieza la muerte, aunque aun suene el clip.</summary>
    public bool EstaMuerto { get { return _muerto; } }

    /// <summary>True solo cuando ya manda la fisica.</summary>
    public bool EnRagdoll { get { return _ragdollActivo; } }

    // ---------------------------------------------------------------------
    // PREPARACION
    // ---------------------------------------------------------------------

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        _controller = GetComponent<CharacterController>();

        RecogerHuesos();
        PrepararRagdoll();
    }

    private void RecogerHuesos()
    {
        var encontrados = GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < encontrados.Length; i++)
        {
            // El rigidbody de la propia raiz, si lo hubiera, no es del ragdoll.
            if (encontrados[i].transform == transform) continue;
            _huesos.Add(encontrados[i]);

            var suyos = encontrados[i].GetComponents<Collider>();
            for (int j = 0; j < suyos.Length; j++) _colliders.Add(suyos[j]);
        }

        int n = _huesos.Count;
        _posAnterior = new Vector3[n];
        _rotAnterior = new Quaternion[n];
        _velLineal   = new Vector3[n];
        _velAngular  = new Vector3[n];

        if (n == 0)
            Debug.LogWarning("[RagdollDeath] No encuentro rigidbodies de ragdoll bajo " +
                             name + ". Montalo con GameObject > 3D Object > Ragdoll.", this);
        else if (depurar)
            Debug.Log("[RagdollDeath] " + n + " huesos, " + _colliders.Count + " colliders.", this);
    }

    /// <summary>
    /// Deja el ragdoll dormido: kinematico, sin colliders y en su capa. Mientras
    /// el personaje esta vivo manda el Animator y nada mas.
    /// </summary>
    private void PrepararRagdoll()
    {
        int capa = LayerMask.NameToLayer(capaRagdoll);
        if (capa < 0)
        {
            capa = -1;
            Debug.LogWarning("[RagdollDeath] No existe la capa '" + capaRagdoll +
                             "'. Creala en Tags and Layers o el cadaver chocara con " +
                             "los jugadores vivos.", this);
        }

        for (int i = 0; i < _huesos.Count; i++)
        {
            var rb = _huesos[i];
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

            if (capa >= 0) rb.gameObject.layer = capa;
        }

        if (apagarCollidersEnVida)
            for (int i = 0; i < _colliders.Count; i++)
                _colliders[i].enabled = false;
    }

    // ---------------------------------------------------------------------
    // ENTRADA PUBLICA
    // ---------------------------------------------------------------------

    /// <summary>
    /// Decide que muerte toca. En red esto lo llama SOLO el servidor y manda el
    /// resultado a todos: si cada cliente tirase su propio dado, cada uno veria
    /// morir al mismo personaje de una forma distinta.
    /// </summary>
    public int ElegirTipoMuerte()
    {
        if (animator != null && animator.GetBool(HashCrouch))
            return MuerteAgachado;

        return Random.Range(0, 2);   // 0 frontal, 1 de espaldas
    }

    /// <summary>Muerte sin golpe concreto: caida, desangrado, guion.</summary>
    public void Matar()
    {
        Matar(ElegirTipoMuerte(), transform.position, Vector3.zero, 0f);
    }

    /// <summary>
    /// Muerte con golpe.
    /// </summary>
    /// <param name="tipoMuerte">0 frontal, 1 de espaldas, 2 agachado.</param>
    /// <param name="puntoImpacto">Donde entro el disparo, en coordenadas de mundo.</param>
    /// <param name="direccionImpacto">Hacia donde iba el disparo. Sin normalizar da igual.</param>
    /// <param name="sobredano">Dano que sobro despues de bajar la vida a cero.
    /// Es lo que decide si el cuerpo se desploma o sale volando.</param>
    public void Matar(int tipoMuerte, Vector3 puntoImpacto, Vector3 direccionImpacto, float sobredano)
    {
        if (_muerto) return;
        _muerto = true;

        _puntoImpacto = puntoImpacto;
        _dirImpacto   = direccionImpacto.sqrMagnitude > 0.0001f
                      ? direccionImpacto.normalized
                      : Vector3.zero;
        _sobredano = Mathf.Max(0f, sobredano);

        ApagarControl();

        // Dos casos en los que el clip no pinta nada:
        //  - te ha reventado algo: el cuerpo tiene que salir despedido ya
        //  - mueres en el aire: ya vas cayendo, un clip de pie seria absurdo
        bool enElAire = animator != null && !animator.GetBool(HashGrounded);
        bool reventado = _sobredano >= sobredanoQueSaltaElClip;

        if (reventado || enElAire || duracionClip <= 0f || animator == null)
        {
            ActivarRagdoll();
            return;
        }

        animator.SetInteger(HashDeathType, Mathf.Clamp(tipoMuerte, 0, 2));
        animator.SetTrigger(HashDie);

        _tiempoRestante = duracionClip;
        _midiendo = true;
        GuardarPoseActual();
    }

    // ---------------------------------------------------------------------
    // PRUEBA
    // ---------------------------------------------------------------------

    /// <summary>
    /// Atajo de desarrollo. El golpe se simula desde la camara hacia delante,
    /// que es de donde vendria un disparo de otro jugador plantado enfrente.
    /// </summary>
    private void Update()
    {
        if (!pruebaActiva || _muerto) return;
        if (!Input.GetKeyDown(teclaDePrueba)) return;

        Camera cam = Camera.main;
        Vector3 punto = animator != null && animator.isHuman
                      ? PosicionDeHueso(HumanBodyBones.Chest)
                      : transform.position + Vector3.up * 1.2f;

        Vector3 direccion = cam != null ? cam.transform.forward : transform.forward;

        int tipo = tipoAleatorioEnPrueba ? ElegirTipoMuerte() : tipoForzadoEnPrueba;
        Matar(tipo, punto, direccion, sobredanoDePrueba);

        Debug.Log("[RagdollDeath] PRUEBA: muerte tipo " + tipo +
                  ", sobredano " + sobredanoDePrueba.ToString("0"), this);
    }

    private Vector3 PosicionDeHueso(HumanBodyBones hueso)
    {
        Transform t = animator.GetBoneTransform(hueso);
        return t != null ? t.position : transform.position + Vector3.up * 1.2f;
    }

    // ---------------------------------------------------------------------
    // CLIP -> FISICA
    // ---------------------------------------------------------------------

    /// <summary>
    /// Se mide en LateUpdate porque es despues de que el Animator haya escrito
    /// los huesos: aqui ya estan donde el clip los deja este frame.
    /// </summary>
    private void LateUpdate()
    {
        if (!_midiendo) return;

        MedirVelocidades(Time.deltaTime);

        _tiempoRestante -= Time.deltaTime;
        if (_tiempoRestante <= 0f)
            ActivarRagdoll();
    }

    private void GuardarPoseActual()
    {
        for (int i = 0; i < _huesos.Count; i++)
        {
            _posAnterior[i] = _huesos[i].transform.position;
            _rotAnterior[i] = _huesos[i].transform.rotation;
            _velLineal[i] = Vector3.zero;
            _velAngular[i] = Vector3.zero;
        }
    }

    private void MedirVelocidades(float dt)
    {
        if (dt <= 0f) return;

        for (int i = 0; i < _huesos.Count; i++)
        {
            Transform t = _huesos[i].transform;

            _velLineal[i] = (t.position - _posAnterior[i]) / dt;

            // Velocidad angular a partir del giro de este frame: se pasa el
            // cuaternion delta a eje-angulo y se divide por el tiempo.
            Quaternion delta = t.rotation * Quaternion.Inverse(_rotAnterior[i]);
            float angulo;
            Vector3 eje;
            delta.ToAngleAxis(out angulo, out eje);
            if (angulo > 180f) angulo -= 360f;           // el giro corto, no el largo
            if (!float.IsInfinity(eje.x) && angulo != 0f)
                _velAngular[i] = eje.normalized * (angulo * Mathf.Deg2Rad / dt);
            else
                _velAngular[i] = Vector3.zero;

            _posAnterior[i] = t.position;
            _rotAnterior[i] = t.rotation;
        }
    }

    private void ActivarRagdoll()
    {
        if (_ragdollActivo) return;
        _ragdollActivo = true;
        _midiendo = false;

        // El Animator se apaga DESPUES de leer la pose: los huesos se quedan
        // exactamente donde el clip los dejo, y la fisica arranca desde ahi.
        if (animator != null) animator.enabled = false;

        for (int i = 0; i < _colliders.Count; i++)
            _colliders[i].enabled = true;

        for (int i = 0; i < _huesos.Count; i++)
        {
            var rb = _huesos[i];
            rb.isKinematic = false;
            rb.useGravity = true;

            Vector3 v = _velLineal[i] * herenciaVelocidad;
            if (v.sqrMagnitude > velocidadMaximaHeredada * velocidadMaximaHeredada)
                v = v.normalized * velocidadMaximaHeredada;

            rb.linearVelocity = v;
            rb.angularVelocity = _velAngular[i] * herenciaVelocidad;
        }

        AplicarGolpe();

        if (depurar)
            Debug.Log("[RagdollDeath] Ragdoll activo en " + name +
                      ". Sobredano " + _sobredano.ToString("0"), this);
    }

    /// <summary>
    /// El impulso va al hueso mas cercano al impacto, no a la cadera: un tiro en
    /// el hombro tuerce el torso, uno en la pierna barre las piernas.
    /// </summary>
    private void AplicarGolpe()
    {
        if (_sobredano <= 0f || _dirImpacto == Vector3.zero || _huesos.Count == 0)
            return;

        Rigidbody objetivo = _huesos[0];
        float mejor = float.MaxValue;
        for (int i = 0; i < _huesos.Count; i++)
        {
            float d = (_huesos[i].transform.position - _puntoImpacto).sqrMagnitude;
            if (d < mejor) { mejor = d; objetivo = _huesos[i]; }
        }

        float fuerza = Mathf.Min(_sobredano * impulsoPorSobredano, impulsoMaximo);
        Vector3 dir = (_dirImpacto + Vector3.up * impulsoHaciaArriba).normalized;

        objetivo.AddForceAtPosition(dir * fuerza, _puntoImpacto, ForceMode.Impulse);
    }

    /// <summary>
    /// Apaga todo lo que seguiria moviendo al personaje: el controlador, el
    /// CharacterController y los scripts procedurales. Si no, la columna seguiria
    /// apuntando a la camara mientras el cuerpo esta en el suelo.
    /// </summary>
    private void ApagarControl()
    {
        if (_controller != null) _controller.enabled = false;

        if (componentesADesactivar != null)
            for (int i = 0; i < componentesADesactivar.Length; i++)
                if (componentesADesactivar[i] != null)
                    componentesADesactivar[i].enabled = false;

        // Por si el array del inspector se queda vacio.
        Apagar<PlayerProceduralAim>();
        Apagar<PlayerProceduralFeet>();
        Apagar<PlayerProceduralHands>();
    }

    private void Apagar<T>() where T : Behaviour
    {
        var c = GetComponent<T>();
        if (c != null) c.enabled = false;
    }

    // ---------------------------------------------------------------------
    // VUELTA ATRAS (reanimacion)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Devuelve el control al Animator. Para el sistema de reanimacion: mueve la
    /// raiz a donde ha quedado la cadera, porque durante el ragdoll los huesos se
    /// han ido andando pero el transform raiz sigue donde murio.
    /// </summary>
    public void Levantar()
    {
        if (!_muerto) return;

        Transform cadera = animator != null
                         ? animator.GetBoneTransform(HumanBodyBones.Hips)
                         : null;

        if (cadera != null)
        {
            Vector3 destino = cadera.position;
            RaycastHit hit;
            if (Physics.Raycast(destino + Vector3.up, Vector3.down, out hit, 4f,
                                ~0, QueryTriggerInteraction.Ignore))
                destino.y = hit.point.y;

            transform.position = destino;
        }

        PrepararRagdoll();

        if (animator != null)
        {
            animator.enabled = true;
            animator.ResetTrigger(HashDie);
            animator.Play("Locomotion", 0, 0f);
        }

        if (_controller != null) _controller.enabled = true;

        if (componentesADesactivar != null)
            for (int i = 0; i < componentesADesactivar.Length; i++)
                if (componentesADesactivar[i] != null)
                    componentesADesactivar[i].enabled = true;

        Encender<PlayerProceduralAim>();
        Encender<PlayerProceduralFeet>();
        Encender<PlayerProceduralHands>();

        _muerto = false;
        _ragdollActivo = false;
        _midiendo = false;
    }

    private void Encender<T>() where T : Behaviour
    {
        var c = GetComponent<T>();
        if (c != null) c.enabled = true;
    }
}
