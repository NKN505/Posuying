using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Todos los ajustes del juego, guardados en el equipo y aplicados al arrancar.
//
// Los valores viven en variables normales (no se leen de PlayerPrefs cada frame,
// que seria lento): se cargan una vez y se guardan al aplicar.
public static class GameSettings
{
    // ---------- Claves de guardado ----------
    private const string KeyWidth = "opt_res_w";
    private const string KeyHeight = "opt_res_h";
    private const string KeyFullscreen = "opt_fullscreen";
    private const string KeyVSync = "opt_vsync";
    private const string KeyFps = "opt_fps";
    private const string KeyQuality = "opt_quality";
    private const string KeyShadowDistance = "opt_shadow_dist";
    private const string KeyRenderScale = "opt_render_scale";
    private const string KeyShowFps = "opt_show_fps";
    private const string KeySensitivity = "opt_sensitivity";
    private const string KeyInvertY = "opt_invert_y";
    private const string KeyFov = "opt_fov";
    private const string KeyMaster = "opt_vol_master";
    private const string KeyMusic = "opt_vol_music";
    private const string KeySfx = "opt_vol_sfx";
    private const string KeyNpcs = "opt_npcs";
    private const string KeyNpcCount = "opt_npc_count";
    private const string KeyMinimap = "opt_minimap";
    private const string KeyMinimapRange = "opt_minimap_range";
    private const string KeyTips = "opt_tips";
    private const string KeyVoiceOn = "opt_voice_on";
    private const string KeyVoiceVol = "opt_vol_voice";
    private const string KeyMic = "opt_mic";

    // ---------- Valores disponibles ----------
    // Un unico control para los fotogramas: VSync y limite manual se excluyen
    // entre si (con VSync activo Unity ignora el limite), asi que van juntos.
    //   -1 = VSync (lo marca el monitor)    0 = sin limite
    public static readonly int[] FpsOptions = { -1, 30, 60, 75, 90, 120, 144, 0 };

    // ---------- Valores actuales ----------
    public static int TargetFps = -1;   // VSync por defecto
    public static int QualityLevel = 1;
    public static float ShadowDistance = 50f;
    public static float RenderScale = 1f;
    public static bool ShowFps = false;

    public static float MouseSensitivity = 1f;
    public const float MinSensitivity = 0.05f;
    public const float MaxSensitivity = 5f;
    public static bool InvertY = false;
    public static float FieldOfView = 60f;

    public static float MasterVolume = 1f;
    public static float MusicVolume = 1f;
    public static float SfxVolume = 1f;

    // Supervivientes controlados por la maquina. Solo cuenta el ajuste del
    // anfitrion: es el servidor quien los crea.
    public static bool NpcsEnabled = false;
    public static int NpcCount = 3;

    // El minimapa es solo visual y local: cada jugador lo enciende o lo apaga
    // por su cuenta, a diferencia de los NPC, que dependen del anfitrion.
    public static bool MinimapEnabled = true;
    public static float MinimapRange = 40f;

    // Las teclas y los botones del mando estan en Controles (Opciones > Controles).
    // Estos atajos se conservan para el codigo que ya los usaba.
    public static KeyCode ChatKey => Controles.Tecla(Accion.Chat);
    public static KeyCode VoiceKey => Controles.Tecla(Accion.Voz);
    public static KeyCode PingKey => Controles.Tecla(Accion.Marcar);
    public static KeyCode DropKey => Controles.Tecla(Accion.Soltar);
    public static KeyCode UseKey => Controles.Tecla(Accion.Usar);
    public static KeyCode MeleeKey => Controles.Tecla(Accion.Golpe);
    // Usar el objeto elegido. Antes era la F, la misma tecla que la linterna:
    // curarse la encendia o la apagaba.
    // Consejos de controles al empezar cada partida
    public static bool ShowTips = true;

    // Chat de voz: cada jugador decide si lo usa y a que volumen oye a los demas
    public static bool VoiceEnabled = true;
    public static float VoiceVolume = 1f;
    // Nombre del microfono elegido. Vacio = el predeterminado del sistema.
    public static string MicDevice = "";

    // Avisa a quien dependa de un ajuste (camara del jugador, contador de FPS...)
    public static event System.Action Changed;

    private static bool _loaded;

    // ---------- Arranque ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        Load();
        ApplyAll();
        ApplySavedResolution();
    }

    public static void Load()
    {
        if (_loaded) return;

        TargetFps = PlayerPrefs.GetInt(KeyFps, -1);   // -1 = VSync
        QualityLevel = PlayerPrefs.GetInt(KeyQuality, QualitySettings.GetQualityLevel());
        ShadowDistance = PlayerPrefs.GetFloat(KeyShadowDistance, 50f);
        RenderScale = PlayerPrefs.GetFloat(KeyRenderScale, 1f);
        ShowFps = PlayerPrefs.GetInt(KeyShowFps, 0) == 1;

        MouseSensitivity = PlayerPrefs.GetFloat(KeySensitivity, 1f);
        InvertY = PlayerPrefs.GetInt(KeyInvertY, 0) == 1;
        FieldOfView = PlayerPrefs.GetFloat(KeyFov, 60f);

        MasterVolume = PlayerPrefs.GetFloat(KeyMaster, 1f);
        MusicVolume = PlayerPrefs.GetFloat(KeyMusic, 1f);
        SfxVolume = PlayerPrefs.GetFloat(KeySfx, 1f);

        NpcsEnabled = PlayerPrefs.GetInt(KeyNpcs, 0) == 1;
        NpcCount = PlayerPrefs.GetInt(KeyNpcCount, 3);

        MinimapEnabled = PlayerPrefs.GetInt(KeyMinimap, 1) == 1;
        MinimapRange = PlayerPrefs.GetFloat(KeyMinimapRange, 40f);

        ShowTips = PlayerPrefs.GetInt(KeyTips, 1) == 1;
        VoiceEnabled = PlayerPrefs.GetInt(KeyVoiceOn, 1) == 1;
        VoiceVolume = PlayerPrefs.GetFloat(KeyVoiceVol, 1f);
        MicDevice = PlayerPrefs.GetString(KeyMic, "");

        _loaded = true;
    }

    public static void Save()
    {
        PlayerPrefs.SetInt(KeyFps, TargetFps);
        PlayerPrefs.SetInt(KeyQuality, QualityLevel);
        PlayerPrefs.SetFloat(KeyShadowDistance, ShadowDistance);
        PlayerPrefs.SetFloat(KeyRenderScale, RenderScale);
        PlayerPrefs.SetInt(KeyShowFps, ShowFps ? 1 : 0);

        PlayerPrefs.SetFloat(KeySensitivity, MouseSensitivity);
        PlayerPrefs.SetInt(KeyInvertY, InvertY ? 1 : 0);
        PlayerPrefs.SetFloat(KeyFov, FieldOfView);

        PlayerPrefs.SetFloat(KeyMaster, MasterVolume);
        PlayerPrefs.SetFloat(KeyMusic, MusicVolume);
        PlayerPrefs.SetFloat(KeySfx, SfxVolume);

        PlayerPrefs.SetInt(KeyNpcs, NpcsEnabled ? 1 : 0);
        PlayerPrefs.SetInt(KeyNpcCount, NpcCount);

        PlayerPrefs.SetInt(KeyMinimap, MinimapEnabled ? 1 : 0);
        PlayerPrefs.SetFloat(KeyMinimapRange, MinimapRange);

        PlayerPrefs.SetInt(KeyTips, ShowTips ? 1 : 0);
        PlayerPrefs.SetInt(KeyVoiceOn, VoiceEnabled ? 1 : 0);
        PlayerPrefs.SetFloat(KeyVoiceVol, VoiceVolume);
        PlayerPrefs.SetString(KeyMic, MicDevice);

        PlayerPrefs.Save();
    }

    // Volumen en vivo, para los sliders: se aplica al instante sin pasar por
    // APLICAR. Queda guardado en PlayerPrefs solo en memoria (SetFloat es barato
    // y se puede llamar en cada frame del arrastre); a disco se escribe al soltar
    // el slider con PlayerPrefs.Save(), y Unity lo hace tambien al cerrar el juego.
    // Tiene que quedar en PlayerPrefs y no solo en la variable: el menu llama a
    // Load() al cambiar de sub-pestana y, si no, el valor volveria al anterior.
    public static void ApplyVolumes()
    {
        PlayerPrefs.SetFloat(KeyMaster, MasterVolume);
        PlayerPrefs.SetFloat(KeyMusic, MusicVolume);
        PlayerPrefs.SetFloat(KeySfx, SfxVolume);
        PlayerPrefs.SetFloat(KeyVoiceVol, VoiceVolume);
        PlayerPrefs.SetInt(KeyVoiceOn, VoiceEnabled ? 1 : 0);

        // La musica y los efectos leen su variable cada frame; el general va por
        // el AudioListener
        AudioListener.volume = MasterVolume;
    }

    // Sensibilidad en vivo, igual que el volumen: el jugador la lee cada frame,
    // aqui solo se deja guardada (a disco, al soltar el slider).
    public static void ApplySensitivity()
    {
        MouseSensitivity = Mathf.Clamp(MouseSensitivity, MinSensitivity, MaxSensitivity);
        PlayerPrefs.SetFloat(KeySensitivity, MouseSensitivity);
    }

    // Cambia al microfono anterior (-1) o siguiente (+1) de los enchufados.
    // La lista da la vuelta y empieza por "predeterminado del sistema".
    public static void StepMic(int direction)
    {
        string[] devices = Microphone.devices;
        int index = System.Array.IndexOf(devices, MicDevice);   // -1 = predeterminado
        int count = devices.Length + 1;
        int next = ((index + 1 + direction) % count + count) % count - 1;

        MicDevice = next < 0 ? "" : devices[next];
        PlayerPrefs.SetString(KeyMic, MicDevice);
        PlayerPrefs.Save();
    }

    // Lo que se ensena en Opciones. Si el elegido ya no esta enchufado se avisa:
    // mientras tanto se usa el predeterminado.
    public static string MicLabel()
    {
        if (Microphone.devices.Length == 0) return "No hay microfonos";
        if (string.IsNullOrEmpty(MicDevice)) return "Predeterminado del sistema";
        if (System.Array.IndexOf(Microphone.devices, MicDevice) < 0) return MicDevice + " (desconectado)";
        return MicDevice;
    }

    // Las teclas se guardan en cuanto se eligen, sin pasar por APLICAR
    public static void SaveKeys()
    {
        PlayerPrefs.SetInt(KeyTips, ShowTips ? 1 : 0);
        PlayerPrefs.Save();
    }

    // Nombre legible de una tecla para la interfaz
    public static string KeyLabel(KeyCode key) => Controles.NombreTecla(key);

    // Guarda y aplica de golpe (lo llama el boton APLICAR del menu)
    public static void SaveAndApply()
    {
        Save();
        ApplyAll();
    }

    public static void ApplyAll()
    {
        // Calidad primero: puede cambiar el perfil de render que tocamos despues
        if (QualityLevel >= 0 && QualityLevel < QualitySettings.names.Length)
            QualitySettings.SetQualityLevel(QualityLevel, true);

        // Sin limite de fotogramas la GPU se queda al 100% sin motivo.
        // Con vSyncCount > 0 Unity ignora targetFrameRate, por eso son excluyentes.
        bool useVSync = TargetFps == -1;
        QualitySettings.vSyncCount = useVSync ? 1 : 0;
        Application.targetFrameRate = (useVSync || TargetFps <= 0) ? -1 : TargetFps;

        // En URP la distancia de sombras y la escala viven en el perfil, no en QualitySettings
        UniversalRenderPipelineAsset urp =
            (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline)
            as UniversalRenderPipelineAsset;

        if (urp != null)
        {
            urp.shadowDistance = ShadowDistance;
            urp.renderScale = RenderScale;
        }

        AudioListener.volume = MasterVolume;

        Changed?.Invoke();
    }

    // ---------- Resolucion (aparte: no se toca al aplicar el resto) ----------

    private static List<Vector2Int> _resolutions;

    public static List<Vector2Int> Resolutions
    {
        get
        {
            if (_resolutions == null) BuildResolutionList();
            return _resolutions;
        }
    }

    private static void BuildResolutionList()
    {
        _resolutions = new List<Vector2Int>();

        foreach (Resolution r in Screen.resolutions)
        {
            Vector2Int size = new Vector2Int(r.width, r.height);
            if (!_resolutions.Contains(size))
                _resolutions.Add(size);
        }

        if (_resolutions.Count == 0)
            _resolutions.Add(new Vector2Int(Screen.width, Screen.height));
    }

    private static void ApplySavedResolution()
    {
        if (!PlayerPrefs.HasKey(KeyWidth)) return;   // primera vez: se deja lo del build

        Screen.SetResolution(
            PlayerPrefs.GetInt(KeyWidth),
            PlayerPrefs.GetInt(KeyHeight),
            ToMode(PlayerPrefs.GetInt(KeyFullscreen, 1) == 1));
    }

    public static void ApplyResolution(Vector2Int resolution, bool fullscreen)
    {
        PlayerPrefs.SetInt(KeyWidth, resolution.x);
        PlayerPrefs.SetInt(KeyHeight, resolution.y);
        PlayerPrefs.SetInt(KeyFullscreen, fullscreen ? 1 : 0);
        PlayerPrefs.Save();

        Screen.SetResolution(resolution.x, resolution.y, ToMode(fullscreen));
    }

    private static FullScreenMode ToMode(bool fullscreen) =>
        fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;

    public static int IndexOfCurrentResolution()
    {
        Vector2Int current = new Vector2Int(
            PlayerPrefs.GetInt(KeyWidth, Screen.width),
            PlayerPrefs.GetInt(KeyHeight, Screen.height));

        int index = Resolutions.IndexOf(current);
        return index >= 0 ? index : Resolutions.Count - 1;
    }

    public static bool SavedFullscreen =>
        PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;

    // ---------- Textos para la interfaz ----------

    public static string FpsLabel(int fps)
    {
        if (fps == -1) return "VSync (monitor)";
        if (fps <= 0) return "Sin limite";
        return fps + " FPS";
    }
    public static string OnOff(bool value) => value ? "Activado" : "Desactivado";
    public static string Percent(float value) => Mathf.RoundToInt(value * 100f) + "%";
}
