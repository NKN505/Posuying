using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

// Escena del final del juego: reproduce el video y vuelve al menu.
//
// PARA INSERTAR EL VIDEO: arrastralo al campo 'video' de este componente (en la
// escena Final). Si el campo esta vacio se muestra un aviso provisional durante
// unos segundos, para poder probar todo el flujo antes de tener el video.
//
// Se puede saltar con Escape, Espacio o Intro pasado un momento.
[RequireComponent(typeof(Camera))]
public class EscenaFinal : MonoBehaviour
{
    [Tooltip("El video del final. Vacio = aviso provisional")]
    public VideoClip video;

    [Tooltip("Segundos del aviso provisional cuando no hay video")]
    public float segundosSinVideo = 6f;

    [Tooltip("Segundos antes de poder saltarlo (evita saltarlo sin querer)")]
    public float segundosAntesDeSaltar = 1.5f;

    [Tooltip("Escena a la que se vuelve (la del juego, que arranca en el menu)")]
    public string escenaMenu = "Mapa";

    private VideoPlayer _reproductor;
    private float _inicio;
    private bool _saliendo;

    void Start()
    {
        _inicio = Time.unscaledTime;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        if (video == null) return;

        _reproductor = gameObject.AddComponent<VideoPlayer>();
        _reproductor.playOnAwake = false;
        _reproductor.clip = video;
        _reproductor.renderMode = VideoRenderMode.CameraNearPlane;
        _reproductor.targetCamera = GetComponent<Camera>();
        _reproductor.aspectRatio = VideoAspectRatio.FitInside;

        // El audio sale por un AudioSource: el modo "Direct" no esta soportado en
        // Windows (el video se veia pero no sonaba). Asi ademas respeta el volumen
        // general de las opciones, que GameSettings aplica al AudioListener.
        var fuente = gameObject.AddComponent<AudioSource>();
        fuente.playOnAwake = false;
        fuente.spatialBlend = 0f;
        // Siempre su propio oyente. No sirve "si no hay otro": al llegar aqui aun
        // existe el de la camara del menu de Mapa, que se destruye justo despues.
        if (GetComponent<AudioListener>() == null)
            gameObject.AddComponent<AudioListener>();
        _reproductor.audioOutputMode = VideoAudioOutputMode.AudioSource;
        _reproductor.controlledAudioTrackCount = 1;
        _reproductor.EnableAudioTrack(0, true);
        _reproductor.SetTargetAudioSource(0, fuente);
        _reproductor.loopPointReached += _ => AlAcabarElVideo();
        _reproductor.Play();
    }

    void Update()
    {
        if (_saliendo) return;

        bool tecla = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                     Controles.BotonPulsado(BotonMando.Sur) || Controles.BotonPulsado(BotonMando.Start);

        // Segunda parte: la tabla de resultados, hasta que pulsen o pase un rato
        if (_enResultados)
        {
            if ((Time.unscaledTime - _resultadosDesde > 1f && tecla) ||
                Time.unscaledTime - _resultadosDesde > segundosDeResultados)
                VolverAlMenu();
            return;
        }

        float t = Time.unscaledTime - _inicio;
        if (video == null && t >= segundosSinVideo) { AlAcabarElVideo(); return; }

        if (t >= segundosAntesDeSaltar && tecla) AlAcabarElVideo();
    }

    [Tooltip("Segundos que se queda la tabla de resultados si nadie pulsa nada")]
    public float segundosDeResultados = 25f;

    private bool _enResultados;
    private float _resultadosDesde;

    // Del video se pasa a los resultados de la partida; si no hay, directo al menu
    private void AlAcabarElVideo()
    {
        if (_saliendo || _enResultados) return;
        if (!ResumenPartida.Hay) { VolverAlMenu(); return; }

        _enResultados = true;
        _resultadosDesde = Time.unscaledTime;
        if (_reproductor != null) { _reproductor.Stop(); _reproductor.enabled = false; }
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void DibujarResultados()
    {
        float escala = Screen.height / 1080f;
        Matrix4x4 antes = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(escala, escala, 1f));
        float ancho = 1080f * ((float)Screen.width / Screen.height);

        Color color = GUI.color;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, ancho, 1080f), Texture2D.whiteTexture);
        GUI.color = color;

        var titulo = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, fontSize = 54 };
        GUI.Label(new Rect(0, 150f, ancho, 80f),
            ResumenPartida.Victoria ? "<color=#7CFF8A><b>HABEIS ESCAPADO</b></color>"
                                    : "<color=#ff6666><b>FIN DE LA PARTIDA</b></color>", titulo);

        float w = 820f;
        ResumenPartida.Dibujar(new Rect((ancho - w) / 2f, 270f, w, ResumenPartida.Alto));

        titulo.fontSize = 20;
        GUI.Label(new Rect(0, 290f + ResumenPartida.Alto + 30f, ancho, 30f),
            "<color=#9da3ad>Pulsa Espacio para volver al menu</color>", titulo);

        GUI.matrix = antes;
    }

    void OnGUI()
    {
        if (_enResultados) { DibujarResultados(); return; }
        if (video != null) return;

        var estilo = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true,
                                                     fontSize = Mathf.RoundToInt(Screen.height / 14f) };
        GUI.Label(new Rect(0, 0, Screen.width, Screen.height * 0.9f), "<b>FIN</b>", estilo);
        estilo.fontSize = Mathf.RoundToInt(Screen.height / 45f);
        GUI.Label(new Rect(0, Screen.height * 0.55f, Screen.width, 60f),
                  "<color=#9da3ad>(aqui ira el video final)</color>", estilo);
    }

    private void VolverAlMenu()
    {
        if (_saliendo) return;
        _saliendo = true;

        // Mismo remedio que NetworkReset: el NetworkManager de la partida sigue
        // vivo (sobrevive a los cambios de escena). Si no se quita, al cargar el
        // menu habria dos y el nuevo se autodestruiria.
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsListening) nm.Shutdown();
            Destroy(nm.gameObject);
        }

        ResumenPartida.Borrar();   // ya se ha visto: que no salga en la siguiente partida
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        SceneManager.LoadScene(escenaMenu);
    }
}
