using UnityEngine;

// Camara de la escena que se usa solo antes de conectarse (menu de Host/Cliente).
// En cuanto aparece nuestro jugador en red, se apaga para no estorbar a su camara.
[RequireComponent(typeof(Camera))]
public class MenuCamera : MonoBehaviour
{
    private Camera _cam;
    private AudioListener _listener;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        _listener = GetComponent<AudioListener>();
    }

    void Update()
    {
        bool localPlayerExists = NetworkPlayer.LocalPlayer != null;

        if (_cam.enabled == localPlayerExists)
        {
            _cam.enabled = !localPlayerExists;
            if (_listener != null) _listener.enabled = !localPlayerExists;
        }
    }
}
