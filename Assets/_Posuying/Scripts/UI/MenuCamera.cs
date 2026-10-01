using UnityEngine;

// Camara de la escena que se usa solo antes de conectarse (menu de Host/Cliente).
// En cuanto aparece nuestro jugador en red, se apaga para no estorbar a su camara.
[RequireComponent(typeof(Camera))]
public class MenuCamera : MonoBehaviour
{
    private Camera _cam;
    private AudioListener _listener;
    private AkAudioListener _akListener;     // el oido de Wwise en el menu

    void Awake()
    {
        _cam = GetComponent<Camera>();
        _listener = GetComponent<AudioListener>();
    }

    // En Start y no en Awake: el motor de Wwise se inicia en el Awake de
    // AkInitializer y el oyente tiene que registrarse despues.
    void Start()
    {
        _akListener = GetComponent<AkAudioListener>();
        if (_akListener == null) _akListener = gameObject.AddComponent<AkAudioListener>();
        _akListener.enabled = _cam.enabled;
    }

    void Update()
    {
        bool localPlayerExists = NetworkPlayer.LocalPlayer != null;

        if (_cam.enabled == localPlayerExists)
        {
            _cam.enabled = !localPlayerExists;
            if (_listener != null) _listener.enabled = !localPlayerExists;
            if (_akListener != null) _akListener.enabled = !localPlayerExists;
        }
    }
}
