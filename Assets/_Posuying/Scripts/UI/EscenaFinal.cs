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
        // Audio directo a la tarjeta: no depende de AudioListener ni de Wwise
        _reproductor.audioOutputMode = VideoAudioOutputMode.Direct;
        _reproductor.loopPointReached += _ => VolverAlMenu();
        _reproductor.Play();
    }

    void Update()
    {
        if (_saliendo) return;

        float t = Time.unscaledTime - _inicio;
        if (video == null && t >= segundosSinVideo) { VolverAlMenu(); return; }

        if (t >= segundosAntesDeSaltar &&
            (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)))
            VolverAlMenu();
    }

    void OnGUI()
    {
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

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        SceneManager.LoadScene(escenaMenu);
    }
}
