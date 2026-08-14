using System.Collections.Generic;
using UnityEngine;

// Ajustes de pantalla guardados entre partidas (PlayerPrefs).
// No hace falta ponerlo en ningun objeto: se aplica solo al arrancar el juego.
public static class GameSettings
{
    private const string KeyWidth = "opt_res_w";
    private const string KeyHeight = "opt_res_h";
    private const string KeyFullscreen = "opt_fullscreen";
    private const string KeyVSync = "opt_vsync";
    private const string KeyFps = "opt_fps";

    // 0 = sin limite
    public static readonly int[] FpsOptions = { 30, 60, 90, 120, 144, 0 };

    public static bool VSyncEnabled => PlayerPrefs.GetInt(KeyVSync, 1) == 1;   // activado por defecto
    public static int TargetFps => PlayerPrefs.GetInt(KeyFps, 60);

    private static List<Vector2Int> _resolutions;

    // Resoluciones que soporta el monitor, sin repetir (ignoramos la tasa de refresco)
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

        // En el editor la lista puede venir vacia: usamos la ventana actual
        if (_resolutions.Count == 0)
            _resolutions.Add(new Vector2Int(Screen.width, Screen.height));
    }

    // Unity llama a esto solo al arrancar el juego, antes de cargar la escena
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedSettings()
    {
        // Esto SIEMPRE, incluso la primera vez: sin limite de fotogramas la GPU
        // dibuja todo lo que puede y se queda al 100% sin motivo.
        ApplyPerformanceNow(VSyncEnabled, TargetFps);

        if (!PlayerPrefs.HasKey(KeyWidth)) return;   // primera vez: se deja lo del build

        int width = PlayerPrefs.GetInt(KeyWidth);
        int height = PlayerPrefs.GetInt(KeyHeight);
        bool fullscreen = PlayerPrefs.GetInt(KeyFullscreen, 1) == 1;

        Screen.SetResolution(width, height, ToMode(fullscreen));
    }

    public static void ApplyPerformance(bool vsync, int fps)
    {
        PlayerPrefs.SetInt(KeyVSync, vsync ? 1 : 0);
        PlayerPrefs.SetInt(KeyFps, fps);
        PlayerPrefs.Save();

        ApplyPerformanceNow(vsync, fps);
    }

    private static void ApplyPerformanceNow(bool vsync, int fps)
    {
        // Con VSync activo, Unity ignora targetFrameRate: manda el monitor
        QualitySettings.vSyncCount = vsync ? 1 : 0;
        Application.targetFrameRate = (vsync || fps <= 0) ? -1 : fps;
    }

    public static string FpsLabel(int fps) => fps <= 0 ? "Sin limite" : fps + " FPS";

    public static void Apply(Vector2Int resolution, bool fullscreen)
    {
        PlayerPrefs.SetInt(KeyWidth, resolution.x);
        PlayerPrefs.SetInt(KeyHeight, resolution.y);
        PlayerPrefs.SetInt(KeyFullscreen, fullscreen ? 1 : 0);
        PlayerPrefs.Save();

        Screen.SetResolution(resolution.x, resolution.y, ToMode(fullscreen));
    }

    private static FullScreenMode ToMode(bool fullscreen)
    {
        return fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
    }

    // Indice de la resolucion actual dentro de la lista (para colocar el selector)
    public static int IndexOfCurrent()
    {
        Vector2Int current = new Vector2Int(
            PlayerPrefs.GetInt(KeyWidth, Screen.width),
            PlayerPrefs.GetInt(KeyHeight, Screen.height));

        int index = Resolutions.IndexOf(current);
        return index >= 0 ? index : Resolutions.Count - 1;
    }

    public static bool SavedFullscreen =>
        PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;
}
