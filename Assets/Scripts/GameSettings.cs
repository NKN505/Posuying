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

    // ---------- Valores disponibles ----------
    public static readonly int[] FpsOptions = { 30, 60, 75, 90, 120, 144, 0 };   // 0 = sin limite

    // ---------- Valores actuales ----------
    public static bool VSync = true;
    public static int TargetFps = 60;
    public static int QualityLevel = 1;
    public static float ShadowDistance = 50f;
    public static float RenderScale = 1f;
    public static bool ShowFps = false;

    public static float MouseSensitivity = 1f;
    public static bool InvertY = false;
    public static float FieldOfView = 60f;

    public static float MasterVolume = 1f;
    public static float MusicVolume = 1f;
    public static float SfxVolume = 1f;

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

        VSync = PlayerPrefs.GetInt(KeyVSync, 1) == 1;
        TargetFps = PlayerPrefs.GetInt(KeyFps, 60);
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

        _loaded = true;
    }

    public static void Save()
    {
        PlayerPrefs.SetInt(KeyVSync, VSync ? 1 : 0);
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

        PlayerPrefs.Save();
    }

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

        // Sin limite de fotogramas la GPU se queda al 100% sin motivo
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Application.targetFrameRate = (VSync || TargetFps <= 0) ? -1 : TargetFps;

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

    public static string FpsLabel(int fps) => fps <= 0 ? "Sin limite" : fps + " FPS";
    public static string OnOff(bool value) => value ? "Activado" : "Desactivado";
    public static string Percent(float value) => Mathf.RoundToInt(value * 100f) + "%";
}
