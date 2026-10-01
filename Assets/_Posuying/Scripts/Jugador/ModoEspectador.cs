using System.Collections.Generic;
using UnityEngine;

// Modo espectador: cuando tu jugador queda ELIMINADO (sin vidas de equipo) y aun
// quedan companeros en pie, la camara pasa a seguirles en vez de quedarse
// mirando tu cadaver. Clic izquierdo o derecho (o las flechas) cambian de companero.
//
// Es solo local: cada maquina mira a quien quiere, no viaja nada por red.
// Va en cualquier objeto de la escena (por ejemplo el Canvas).
//
// La camara lleva SpectatorCameraVisual, que ajusta que capas ve: los cuerpos de
// tercera persona de todos, nunca las manos de primera persona.
public class ModoEspectador : MonoBehaviour
{
    [Header("Camara")]
    public float distancia = 4.5f;
    public float altura = 2.2f;
    public float alturaMirada = 1.5f;
    public float suavizado = 6f;
    [Tooltip("Radio para que la camara no se meta dentro de las paredes")]
    public float radioColision = 0.3f;

    private Camera _camara;
    private Behaviour _oyenteWwise;
    private readonly List<PlayerController> _objetivos = new List<PlayerController>();
    private int _indice;
    private bool _activo;

    // Lo que se apago del jugador local al empezar, para devolverlo al terminar
    private Camera _camaraJugador;
    private readonly List<Behaviour> _oyentesJugador = new List<Behaviour>();

    void LateUpdate()
    {
        bool debe = DebeEspectar();
        if (debe && !_activo) Empezar();
        else if (!debe && _activo) Terminar();
        if (!_activo) return;

        ActualizarObjetivos();
        if (_objetivos.Count == 0) return;

        if (!UIState.BlocksGameplay)
        {
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.RightArrow)) _indice++;
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.LeftArrow)) _indice--;
        }
        _indice = ((_indice % _objetivos.Count) + _objetivos.Count) % _objetivos.Count;

        Seguir(_objetivos[_indice].transform);
    }

    // Eliminado, la partida sigue y hay alguien a quien mirar
    private bool DebeEspectar()
    {
        var local = NetworkPlayer.LocalPlayer;
        if (local == null) return false;
        if (MatchManager.Instance != null && MatchManager.Instance.MatchOver) return false;

        var estado = local.GetComponent<PlayerDownedState>();
        if (estado == null || !estado.IsOut) return false;

        ActualizarObjetivos();
        return _objetivos.Count > 0;
    }

    private void ActualizarObjetivos()
    {
        _objetivos.Clear();
        foreach (var jugador in NetworkPlayer.AllPlayers)
        {
            if (jugador == null || jugador == NetworkPlayer.LocalPlayer) continue;
            var estado = jugador.GetComponent<PlayerDownedState>();
            if (estado != null && estado.IsOut) continue;
            _objetivos.Add(jugador);
        }
    }

    private void Empezar()
    {
        _activo = true;

        if (_camara == null)
        {
            var go = new GameObject("CamaraEspectador");
            go.SetActive(false);   // asi SpectatorCameraVisual se configura al activarla
            _camara = go.AddComponent<Camera>();
            _camara.nearClipPlane = 0.1f;
            _camara.farClipPlane = 1000f;
            go.AddComponent<SpectatorCameraVisual>();

            // Oyente de Wwise en la camara de espectador, para oir lo que pasa
            // alrededor del companero. Se busca por nombre para no atar este
            // script al ensamblado de Wwise.
            var tipoOyente = BuscarTipo("AkAudioListener");
            if (tipoOyente != null) _oyenteWwise = go.AddComponent(tipoOyente) as Behaviour;
        }

        // Apagar la camara y los oyentes del jugador local
        var local = NetworkPlayer.LocalPlayer;
        _camaraJugador = local != null ? local.GetComponentInChildren<Camera>() : null;
        _oyentesJugador.Clear();
        if (_camaraJugador != null)
        {
            _camaraJugador.enabled = false;
            foreach (var b in _camaraJugador.GetComponents<Behaviour>())
                if (b.enabled && (b is AudioListener || b.GetType().Name == "AkAudioListener"))
                {
                    b.enabled = false;
                    _oyentesJugador.Add(b);
                }
        }

        _indice = 0;
        ActualizarObjetivos();
        if (_objetivos.Count > 0)
        {
            // Empezar ya colocada, sin un barrido desde el origen
            var t = _objetivos[0].transform;
            _camara.transform.position = t.position + Vector3.up * altura - t.forward * distancia;
            _camara.transform.LookAt(t.position + Vector3.up * alturaMirada);
        }
        _camara.gameObject.SetActive(true);
        Notifications.Show("Estas fuera de combate: sigues a tus companeros (clic para cambiar)");
    }

    private void Terminar()
    {
        _activo = false;
        if (_camara != null) _camara.gameObject.SetActive(false);

        // Devolver la vista al jugador (reinicio de partida)
        if (_camaraJugador != null) _camaraJugador.enabled = true;
        foreach (var b in _oyentesJugador) if (b != null) b.enabled = true;
        _oyentesJugador.Clear();
        _camaraJugador = null;
    }

    // Detras y un poco por encima del companero, sin atravesar paredes
    private void Seguir(Transform objetivo)
    {
        Vector3 mira = objetivo.position + Vector3.up * alturaMirada;
        Vector3 deseada = objetivo.position + Vector3.up * altura - objetivo.forward * distancia;

        Vector3 dir = deseada - mira;
        if (Physics.SphereCast(mira, radioColision, dir.normalized, out RaycastHit choque, dir.magnitude,
                               Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            deseada = mira + dir.normalized * Mathf.Max(0.5f, choque.distance - 0.1f);

        float k = 1f - Mathf.Exp(-suavizado * Time.deltaTime);
        _camara.transform.position = Vector3.Lerp(_camara.transform.position, deseada, k);
        _camara.transform.rotation = Quaternion.Slerp(_camara.transform.rotation,
            Quaternion.LookRotation(mira - _camara.transform.position), k);
    }

    void OnGUI()
    {
        if (!_activo || _objetivos.Count == 0) return;

        var nombre = _objetivos[Mathf.Clamp(_indice, 0, _objetivos.Count - 1)].GetComponent<PlayerName>();
        string texto = "<b>ESPECTANDO A " + (nombre != null ? nombre.Name.ToUpper() : "UN COMPANERO") + "</b>" +
                       (_objetivos.Count > 1 ? "   ·   clic para cambiar" : "");

        float escala = Screen.height / 1080f;
        var estilo = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true,
                                                     fontSize = Mathf.RoundToInt(22 * escala) };
        GUI.Label(new Rect(0f, Screen.height - 110f * escala, Screen.width, 40f * escala), texto, estilo);
    }

    void OnDisable()
    {
        if (_activo) Terminar();
    }

    private static System.Type BuscarTipo(string nombre)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(nombre);
            if (t != null) return t;
        }
        return null;
    }
}
