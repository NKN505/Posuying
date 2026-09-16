using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Musica del menu de inicio: suena en bucle mientras estas en el menu y se
/// apaga con un fundido en cuanto entras en una partida.
///
/// Se crea sola al arrancar el juego, no hay que colocarla en ninguna escena.
/// La cancion se carga desde Resources/Musica, asi que para cambiarla basta con
/// sustituir el fichero o cambiar <see cref="RutaCancion"/>.
///
/// "Estar en el menu de inicio" se decide con la MISMA regla que usa MainMenuUI
/// para mostrarse: no estar conectado a una partida ni migrando de anfitrion. Lo
/// que NO cuenta como menu de inicio es abrir las Opciones con Escape en mitad de
/// la partida: usa el mismo panel, pero ahi la musica del menu no pinta nada.
///
/// El volumen obedece al ajuste "Musica" de Opciones. El "Maestro" ya lo aplica
/// GameSettings sobre el AudioListener, asi que aqui no se multiplica otra vez.
/// </summary>
public class MenuMusic : MonoBehaviour
{
    /// <summary>Ruta dentro de Resources, sin extension.</summary>
    public const string RutaCancion = "Musica/MenuTheme_IfImGone";

    [Tooltip("Segundos del fundido al entrar en partida o volver al menu")]
    public float fundido = 1.5f;

    private AudioSource _fuente;
    private float _nivel;          // 0..1: por donde va el fundido

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CrearAlArrancar()
    {
        if (FindFirstObjectByType<MenuMusic>() != null) return;

        var clip = Resources.Load<AudioClip>(RutaCancion);
        if (clip == null)
        {
            Debug.LogWarning("MenuMusic: no encuentro Resources/" + RutaCancion + ". El menu sonara sin musica.");
            return;
        }

        var go = new GameObject("MusicaMenu");
        DontDestroyOnLoad(go);

        var musica = go.AddComponent<MenuMusic>();
        musica.Montar(clip);
    }

    private void Montar(AudioClip clip)
    {
        _fuente = gameObject.AddComponent<AudioSource>();
        _fuente.clip = clip;
        _fuente.loop = true;
        _fuente.playOnAwake = false;
        _fuente.spatialBlend = 0f;     // 2D: suena igual este donde este la camara
        _fuente.volume = 0f;
        _fuente.ignoreListenerPause = true;
    }

    void Update()
    {
        if (_fuente == null) return;

        bool enMenu = EnMenuDeInicio();

        // Tiempo sin escalar: si alguien pausa el juego con timeScale = 0, el
        // fundido tiene que seguir avanzando igual
        float paso = fundido > 0.01f ? Time.unscaledDeltaTime / fundido : 1f;
        _nivel = Mathf.MoveTowards(_nivel, enMenu ? 1f : 0f, paso);

        if (enMenu && !_fuente.isPlaying)
        {
            // Al volver al menu tras una partida, la cancion empieza desde el
            // principio en vez de retomarse a medias
            _fuente.time = 0f;
            _fuente.Play();
        }

        _fuente.volume = _nivel * GameSettings.MusicVolume;

        // Solo se para del todo cuando el fundido ha terminado, o se oiria el corte
        if (!enMenu && _nivel <= 0f && _fuente.isPlaying)
            _fuente.Stop();
    }

    private static bool EnMenuDeInicio()
    {
        var net = NetworkManager.Singleton;
        if (net != null && (net.IsClient || net.IsServer)) return false;

        var menu = MainMenuUI.Instance;
        if (menu != null && menu.onlineSession != null && menu.onlineSession.IsMigrating) return false;

        return true;
    }
}
