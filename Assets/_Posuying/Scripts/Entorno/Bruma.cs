using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Bruma espesa y volumetrica, al estilo del primer Silent Hill: a unas decenas de
// metros todo se pierde en un gris azulado y los enemigos salen de la nada.
//
//   - NIEBLA: la de Unity (RenderSettings), pero mucho mas densa que la de la
//     escena, y que se abre y se cierra despacio. Va con el reloj de la partida,
//     asi que todos los jugadores la ven igual de cerrada en cada momento.
//   - VOLUMEN: encima, el paquete "URP Volumetric Fog" recorre el aire de cada
//     pixel: la luz de linternas, farolas y la luna se ve EN la niebla (el haz de
//     la linterna), con sus sombras.
//   - CIELO: se tapa con el color de la bruma. La niebla de Unity no afecta al
//     cielo, y sin esto los edificios lejanos se recortarian contra la luna.
//   - Lo que queda mas alla de la bruma no se dibuja (no se ve, y ahorra trabajo).
//
// El aspecto se ajusta en el asset Resources/Bruma/AjustesBruma; la calidad y la
// densidad, cada jugador en Opciones > Graficos. Se crea sola al arrancar el
// juego: no hay que ponerla en ninguna escena, y la
// niebla guardada en la escena no se cambia (solo mientras se juega).
public class Bruma : MonoBehaviour
{
    private static Bruma _instancia;

    /// <summary>Hay bruma ahora mismo.</summary>
    public static bool Activa => _instancia != null && _instancia._puesta;

    /// <summary>Metros a partir de los cuales no se distingue nada.</summary>
    public static float Visibilidad => Activa ? _instancia._visibilidad : float.MaxValue;

    /// <summary>La bruma no deja ver un punto desde el otro.</summary>
    public static bool Tapa(Vector3 a, Vector3 b)
    {
        if (!Activa) return false;
        float v = _instancia._visibilidad;
        return (a - b).sqrMagnitude > v * v;
    }

    // Con niebla "exponencial al cuadrado" de densidad d, a 2.4 / d metros solo
    // llega el 0.3 % del color de las cosas: no se distinguen del fondo.
    private const float Corte = 2.4f;

    private AjustesBruma _ajustes;
    private bool _puesta;
    private float _visibilidad = 40f;

    // Lo que tenia la escena, para devolverlo si se apaga la bruma
    private bool _guardado;
    private bool _nieblaAntes;
    private FogMode _modoAntes;
    private Color _colorAntes;
    private float _densidadAntes;

    private struct CamaraAntes { public CameraClearFlags fondo; public Color color; public float lejos; }
    private readonly Dictionary<Camera, CamaraAntes> _camaras = new Dictionary<Camera, CamaraAntes>();
    private readonly List<Camera> _sobran = new List<Camera>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CrearAlArrancar()
    {
        if (_instancia != null) return;
        var go = new GameObject("Bruma");
        DontDestroyOnLoad(go);
        _instancia = go.AddComponent<Bruma>();
    }

    void Awake()
    {
        _ajustes = Resources.Load<AjustesBruma>("Bruma/AjustesBruma");
        // Cada escena trae su propia niebla: al cambiar se empieza de cero
        SceneManager.activeSceneChanged += AlCambiarDeEscena;
    }

    void OnDestroy()
    {
        SceneManager.activeSceneChanged -= AlCambiarDeEscena;
        if (_volumen != null && _volumen.profile != null) Destroy(_volumen.profile);
    }

    private void AlCambiarDeEscena(Scene antes, Scene ahora)
    {
        _guardado = false; _puesta = false; _camaras.Clear();
    }

    // LateUpdate: despues de que cada script haya encendido o apagado su camara
    void LateUpdate()
    {
        bool toca = _ajustes != null && _ajustes.activa && EnEscenaConBruma();
        if (!toca)
        {
            if (_puesta) Quitar();
            return;
        }

        if (!_guardado)
        {
            _nieblaAntes = RenderSettings.fog;
            _modoAntes = RenderSettings.fogMode;
            _colorAntes = RenderSettings.fogColor;
            _densidadAntes = RenderSettings.fogDensity;
            _guardado = true;
        }
        _puesta = true;

        // Se abre y se cierra despacio y sin patron reconocible
        float ritmo = 1f / Mathf.Max(5f, _ajustes.segundosDeCambio);
        float cerrada = Mathf.SmoothStep(0f, 1f, Mathf.PerlinNoise(Reloj() * ritmo, 7.3f));
        _visibilidad = Mathf.Lerp(_ajustes.visibilidadMaxima, _ajustes.visibilidadMinima, cerrada)
                     * GameSettings.FogVisibilityFactor;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = _ajustes.color;
        RenderSettings.fogDensity = Corte / Mathf.Max(1f, _visibilidad);

        PonerCamaras();
        PrepararVolumen();
    }

    private bool EnEscenaConBruma()
    {
        string escena = SceneManager.GetActiveScene().name;
        foreach (string nombre in _ajustes.escenas)
            if (nombre == escena) return true;
        return false;
    }

    // El reloj de la partida es el mismo en todas las maquinas
    private static float Reloj()
    {
        var red = NetworkManager.Singleton;
        return red != null && red.IsListening ? (float)red.ServerTime.Time : Time.time;
    }

    // ---------- Camaras: cielo tapado y distancia recortada ----------

    private void PonerCamaras()
    {
        // Margen sobre la visibilidad mas larga posible, fijo, para que el corte no vaya y venga
        float lejos = _ajustes.visibilidadMaxima * GameSettings.FogVisibilityFactor * 1.15f;

        foreach (var camara in Camera.allCameras)
        {
            if (camara.targetTexture != null) continue;   // las que pintan en una textura

            if (!_camaras.TryGetValue(camara, out CamaraAntes antes))
            {
                // Las que no pintan el cielo (la de las manos) se dejan como estan
                if (camara.clearFlags != CameraClearFlags.Skybox) continue;
                antes = new CamaraAntes { fondo = camara.clearFlags, color = camara.backgroundColor, lejos = camara.farClipPlane };
                _camaras[camara] = antes;
            }

            if (_ajustes.taparCielo)
            {
                camara.clearFlags = CameraClearFlags.SolidColor;
                camara.backgroundColor = _ajustes.color;
            }
            else camara.clearFlags = antes.fondo;

            camara.farClipPlane = _ajustes.recortarDistancia ? Mathf.Min(antes.lejos, lejos) : antes.lejos;
        }

        // Camaras destruidas: fuera de la lista
        _sobran.Clear();
        foreach (var par in _camaras) if (par.Key == null) _sobran.Add(par.Key);
        foreach (var camara in _sobran) _camaras.Remove(camara);
    }

    private void Quitar()
    {
        _puesta = false;
        if (_guardado)
        {
            RenderSettings.fog = _nieblaAntes;
            RenderSettings.fogMode = _modoAntes;
            RenderSettings.fogColor = _colorAntes;
            RenderSettings.fogDensity = _densidadAntes;
        }
        foreach (var par in _camaras)
        {
            if (par.Key == null) continue;
            par.Key.clearFlags = par.Value.fondo;
            par.Key.backgroundColor = par.Value.color;
            par.Key.farClipPlane = par.Value.lejos;
        }
        _camaras.Clear();
        if (_volumen != null) _volumen.enabled = false;
    }

    // ---------- Volumen ----------

    // El volumen lo pinta el paquete "URP Volumetric Fog" (com.cqf.urpvolumetricfog,
    // de Cristian Qiu). Tiene su "Renderer Feature" en PC_Renderer y Mobile_Renderer
    // y se maneja como cualquier efecto de postprocesado: con un Volume. Aqui se
    // crea uno global por codigo y se le van pasando los valores.

    private Volume _volumen;
    private VolumetricFogVolumeComponent _niebla;
    private float _siguienteRepaso;

    private void PrepararVolumen()
    {
        int calidad = Mathf.Clamp(GameSettings.FogQuality, 0, 3);

        if (_niebla == null)
        {
            var go = new GameObject("Volumen de bruma");
            go.transform.SetParent(transform, false);
            _volumen = go.AddComponent<Volume>();
            _volumen.isGlobal = true;
            _volumen.priority = 50f;
            _volumen.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _niebla = _volumen.profile.Add<VolumetricFogVolumeComponent>(true);
        }
        _volumen.enabled = true;

        _niebla.enabled.value = calidad > 0;
        if (calidad == 0) return;

        // Hasta donde se calcula: mas alla la niebla de fondo ya lo ha tapado todo
        _niebla.distance.value = Mathf.Min(_visibilidad, 512f);
        _niebla.density.value = _ajustes.densidadVolumen;
        // Alto: el oscurecer con la distancia ya lo hace la niebla de fondo
        _niebla.attenuationDistance.value = _visibilidad * 4f;

        // La altura va con la camara: el mapa tiene desniveles y no hay un "suelo" unico
        Camera camara = Camera.main;
        float y = camara != null ? camara.transform.position.y : 0f;
        _niebla.baseHeight.value = y + 2f;
        _niebla.maximumHeight.value = y + _ajustes.alturaVolumen;
        _niebla.enableGround.value = false;

        // La luna
        _niebla.enableMainLightContribution.value = _ajustes.luzDeLuna > 0f;
        _niebla.scattering.value = Mathf.Clamp01(_ajustes.luzDeLuna);
        _niebla.anisotropy.value = 0.5f;

        // Linternas, farolas, fogonazos
        _niebla.enableAdditionalLightsContribution.value = true;

        _niebla.maxSteps.value = calidad == 1 ? _ajustes.pasos.x : calidad == 2 ? _ajustes.pasos.y : _ajustes.pasos.z;
        _niebla.blurIterations.value = calidad;

        MarcarLuces();
    }

    // El paquete solo tiene en cuenta las luces que llevan su componente
    // VolumetricAdditionalLight. Se les pone por codigo (asi no hay que tocar
    // prefabs ni la escena) y se repasa cada poco: aparecen linternas, fogonazos,
    // luces de objetos soltados...
    private void MarcarLuces()
    {
        if (Time.unscaledTime < _siguienteRepaso) return;
        _siguienteRepaso = Time.unscaledTime + 1f;

        foreach (var luz in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (luz.type != LightType.Spot && luz.type != LightType.Point) continue;

            var volumetrica = luz.GetComponent<VolumetricAdditionalLight>();
            if (volumetrica == null) volumetrica = luz.gameObject.AddComponent<VolumetricAdditionalLight>();

            bool foco = luz.type == LightType.Spot;
            volumetrica.Scattering = foco ? _ajustes.brilloFocos : _ajustes.brilloPuntuales;
            volumetrica.Anisotropy = _ajustes.concentracion;
            // En un foco, radio alto: si no, mirando a lo largo del haz (tu propia
            // linterna) el arranque de la luz quema la pantalla en blanco
            volumetrica.Radius = foco ? 1f : 0.3f;
        }
    }
}
