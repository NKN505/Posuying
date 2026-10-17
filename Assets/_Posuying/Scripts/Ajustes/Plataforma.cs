using UnityEngine;

// En que esta corriendo el juego y que puede hacer ahi. El resto del codigo
// pregunta aqui en vez de mirar la plataforma directamente:
//
//     if (Plataforma.HayMicrofono) ...
//
// Plataformas: Windows, Mac y Linux (escritorio), Android (movil) y navegador
// (WebGL). Todas juegan juntas en las mismas partidas (ver OnlineSession).
public static class Plataforma
{
    /// <summary>Version del juego. Solo pueden jugar juntos jugadores con la MISMA version.</summary>
    public static string Version => Application.version;

    /// <summary>Navegador (WebGL).</summary>
    public static bool EsWeb =>
#if UNITY_WEBGL && !UNITY_EDITOR
        true;
#else
        false;
#endif

    /// <summary>
    /// Movil o tableta (incluido un navegador de movil y la app web instalada
    /// en el iPhone). El iPad se hace pasar por un Mac en el navegador: alli se
    /// reconoce en cuanto se toca la pantalla.
    /// </summary>
    public static bool EsMovil => Application.isMobilePlatform || ForzarTactil || (EsWeb && _tocado);

    private static bool _tocado;

    /// <summary>Lo llama ControlesTactiles cuando ve un dedo en la pantalla.</summary>
    public static void HanTocado() => _tocado = true;

    /// <summary>Windows, Mac o Linux.</summary>
    public static bool EsEscritorio => !EsWeb && !EsMovil;

    /// <summary>
    /// Para probar los controles tactiles en el editor o en PC: ponlo a true
    /// (Opciones o consola) y el juego se comporta como en un movil.
    /// </summary>
    public static bool ForzarTactil;

    /// <summary>Se juega con los dedos: controles en pantalla, sin raton.</summary>
    public static bool Tactil => EsMovil;

    /// <summary>
    /// El navegador no da acceso al microfono desde Unity: alli el chat de voz no
    /// esta (ni para hablar ni para oir, que tambien usa algo que el navegador no tiene).
    /// </summary>
    public static bool HayChatDeVoz => !EsWeb;

    /// <summary>La bruma volumetrica no funciona en navegador (solo la niebla normal).</summary>
    public static bool HayBrumaVolumetrica => !EsWeb;

    /// <summary>Resolucion y pantalla completa las decide el sistema (movil) o la pagina (web).</summary>
    public static bool EligeResolucion => EsEscritorio;

    /// <summary>En el navegador "Salir del juego" no tiene sentido: se cierra la pestana.</summary>
    public static bool PuedeSalir => !EsWeb;

    /// <summary>Nombre para mostrar (en el menu y al conectarse).</summary>
    public static string Nombre
    {
        get
        {
            if (EsWeb) return "Navegador";
            switch (Application.platform)
            {
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.OSXPlayer: case RuntimePlatform.OSXEditor: return "Mac";
                case RuntimePlatform.LinuxPlayer: case RuntimePlatform.LinuxEditor: return "Linux";
                default: return "Windows";
            }
        }
    }

    /// <summary>
    /// El primer arranque en movil o navegador empieza con graficos bajos: lo de
    /// PC iria a pocos FPS. Despues manda lo que el jugador elija en Opciones.
    /// </summary>
    public static int CalidadPorDefecto()
    {
        if (EsEscritorio) return QualitySettings.GetQualityLevel();
        int movil = System.Array.IndexOf(QualitySettings.names, "Mobile");
        return movil >= 0 ? movil : 0;
    }

    /// <summary>Calidad de la bruma la primera vez: 0 = sin volumen.</summary>
    public static int BrumaPorDefecto => EsWeb ? 0 : EsMovil ? 1 : 2;
}
