using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

// Posuying > Multiplataforma
//
// Herramientas para sacar el juego en todas las plataformas desde este PC:
//
//   - COMPROBAR: compila los scripts como si fueran para esa plataforma, SIN
//     cambiar la plataforma activa del editor (cambiarla tarda varios minutos).
//     Sirve para pillar codigo que solo existe en Windows antes de ponerse a
//     hacer una build.
//   - COMPILAR: hace la build en Builds/<Plataforma>/.
//
// Para compilar para una plataforma hace falta su modulo en Unity Hub
// (Installs > tu version > Add modules). Si falta, el menu lo dice.
public static class Multiplataforma
{
    public struct Destino
    {
        public string nombre;
        public BuildTarget objetivo;
        public BuildTargetGroup grupo;
        public string archivo;   // dentro de Builds/<nombre>/
    }

    public static readonly Destino[] Destinos =
    {
        new Destino { nombre = "Windows", objetivo = BuildTarget.StandaloneWindows64, grupo = BuildTargetGroup.Standalone, archivo = "Posuying.exe" },
        new Destino { nombre = "Linux",   objetivo = BuildTarget.StandaloneLinux64,   grupo = BuildTargetGroup.Standalone, archivo = "Posuying.x86_64" },
        new Destino { nombre = "Mac",     objetivo = BuildTarget.StandaloneOSX,       grupo = BuildTargetGroup.Standalone, archivo = "Posuying.app" },
        new Destino { nombre = "Android", objetivo = BuildTarget.Android,             grupo = BuildTargetGroup.Android,    archivo = "Posuying.apk" },
        new Destino { nombre = "Web",     objetivo = BuildTarget.WebGL,               grupo = BuildTargetGroup.WebGL,      archivo = "" },
    };

    public static Destino Buscar(string nombre) => Destinos.First(d => d.nombre == nombre);

    /// <summary>El modulo de esa plataforma esta instalado en este Unity.</summary>
    public static bool Instalado(Destino d) => BuildPipeline.IsBuildTargetSupported(d.grupo, d.objetivo);

    // ---------- Comprobar ----------

    [MenuItem("Posuying/Multiplataforma/Comprobar que compila en todas")]
    private static void ComprobarTodas()
    {
        var informe = new List<string>();
        foreach (var d in Destinos) informe.Add(Comprobar(d.nombre));
        string texto = string.Join("\n\n", informe);
        Debug.Log("[Multiplataforma]\n" + texto);
        EditorUtility.DisplayDialog("Multiplataforma", Resumen(texto), "Vale");
    }

    /// <summary>
    /// Compila los scripts del juego para esa plataforma y devuelve un informe
    /// (errores incluidos). No cambia la plataforma activa.
    /// </summary>
    public static string Comprobar(string nombre)
    {
        var d = Buscar(nombre);
        if (!Instalado(d)) return nombre + ": FALTA EL MODULO (instalalo en Unity Hub)";

        var errores = new List<string>();
        Application.LogCallback recoger = (mensaje, pila, tipo) =>
        {
            // "TypeDB" son avisos internos de Unity al compilar para la plataforma activa, no errores del juego
            if ((tipo == LogType.Error || tipo == LogType.Exception) && !mensaje.StartsWith("TypeDB"))
                errores.Add(mensaje.Split('\n')[0]);
        };

        string carpeta = Path.Combine(Path.GetTempPath(), "PosuyingComprobar_" + nombre);
        Application.logMessageReceived += recoger;
        ScriptCompilationResult resultado;
        try
        {
            resultado = PlayerBuildInterface.CompilePlayerScripts(
                new ScriptCompilationSettings { target = d.objetivo, group = d.grupo }, carpeta);
        }
        finally
        {
            Application.logMessageReceived -= recoger;
        }

        int ensamblados = resultado.assemblies != null ? resultado.assemblies.Count : 0;
        var distintos = errores.Distinct().ToList();
        if (distintos.Count == 0) return nombre + ": OK (" + ensamblados + " ensamblados)";

        return nombre + ": " + distintos.Count + " ERRORES\n  " + string.Join("\n  ", distintos.Take(30)) +
               (distintos.Count > 30 ? "\n  ..." : "");
    }

    private static string Resumen(string texto) =>
        string.Join("\n", texto.Split('\n').Where(l => !l.StartsWith("  ")));

    // ---------- Ajustes de cada plataforma ----------

    /// <summary>Identificador del juego en Android (unico en Google Play; se puede cambiar antes de publicar).</summary>
    public const string IdentificadorAndroid = "com.nkn505.posuying";

    // Lo que cada plataforma necesita para que el juego funcione alli. Se aplica
    // solo antes de cada build; desde el menu, para verlo en Project Settings.
    [MenuItem("Posuying/Multiplataforma/Preparar ajustes de plataformas")]
    public static void PrepararAjustes()
    {
        // Android: 64 bits (lo que pide Google Play), en horizontal y con Internet
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, IdentificadorAndroid);
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;

        // Navegador: comprimido, y que se descomprima solo aunque el servidor web
        // no este configurado para ello (asi vale cualquier hosting, itch.io incluido)
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        // Instalable como app (PWA) en el iPhone y en Android: Assets/WebGLTemplates/PosuyingPWA
        PlayerSettings.WebGL.template = "PROJECT:PosuyingPWA";
        PlayerSettings.WebGL.dataCaching = false;   // ya lo guarda el ServiceWorker de la plantilla

        // Wwise todavia no esta instalado para Android ni navegador: alli se compila
        // con el Wwise "mudo" de Scripts/Audio/SinWwise.cs (ver ese archivo)
        PonerSimbolo(UnityEditor.Build.NamedBuildTarget.Android, SinWwise, true);
        PonerSimbolo(UnityEditor.Build.NamedBuildTarget.WebGL, SinWwise, true);

        // Que el juego siga corriendo (y la red viva) aunque la ventana o pestana pierda el foco
        PlayerSettings.runInBackground = true;

        AssetDatabase.SaveAssets();
        Debug.Log("[Multiplataforma] Ajustes de Android, navegador y escritorio preparados.");
    }

    public const string SinWwise = "POSUYING_SIN_WWISE";

    /// <summary>Tamano maximo de textura en las builds de movil y navegador.</summary>
    public const int TexturaMaximaMovil = 1024;

    private static void PonerSimbolo(UnityEditor.Build.NamedBuildTarget objetivo, string simbolo, bool poner)
    {
        var lista = PlayerSettings.GetScriptingDefineSymbols(objetivo)
            .Split(';').Where(s => s.Length > 0 && s != simbolo).ToList();
        if (poner) lista.Add(simbolo);
        PlayerSettings.SetScriptingDefineSymbols(objetivo, string.Join(";", lista));
    }

    // ---------- Compilar ----------

    [MenuItem("Posuying/Multiplataforma/Compilar/Windows")] private static void CWindows() => CompilarConAviso("Windows");
    [MenuItem("Posuying/Multiplataforma/Compilar/Linux")] private static void CLinux() => CompilarConAviso("Linux");
    [MenuItem("Posuying/Multiplataforma/Compilar/Mac")] private static void CMac() => CompilarConAviso("Mac");
    [MenuItem("Posuying/Multiplataforma/Compilar/Android")] private static void CAndroid() => CompilarConAviso("Android");
    [MenuItem("Posuying/Multiplataforma/Compilar/Web (navegador)")] private static void CWeb() => CompilarConAviso("Web");

    private static void CompilarConAviso(string nombre)
    {
        string resultado = Compilar(nombre);
        Debug.Log("[Multiplataforma] " + resultado);
        EditorUtility.DisplayDialog("Multiplataforma", resultado, "Vale");
    }

    /// <summary>Hace la build de esa plataforma en Builds/&lt;nombre&gt;/. Devuelve como fue.</summary>
    public static string Compilar(string nombre, bool desarrollo = false)
    {
        var d = Buscar(nombre);
        if (!Instalado(d)) return nombre + ": falta el modulo. Instalalo en Unity Hub > Installs > Add modules.";

        PrepararAjustes();
        // <carpeta del proyecto>/Builds/<plataforma>, p. ej. D:\Windows\ProyectosUnity\Posuying\Builds\Android
        string proyecto = Directory.GetParent(Application.dataPath).FullName;
        string carpeta = Path.Combine(proyecto, "Builds", nombre);
        string ruta = d.archivo == "" ? carpeta : Path.Combine(carpeta, d.archivo);

        var escenas = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var opciones = new BuildPlayerOptions
        {
            scenes = escenas,
            locationPathName = ruta,
            target = d.objetivo,
            targetGroup = d.grupo,
            options = desarrollo ? BuildOptions.Development : BuildOptions.None,
        };

        // Movil y navegador: texturas como mucho de 1024 px y en ASTC (lo que usan
        // iPhone y Android). Muchas texturas de los modelos comprados son de 4096 px
        // (21 MB cada una): con ellas la web pesaba 423 MB y un iPhone no la abre.
        // Solo afecta a esta build: los archivos de las texturas y las builds de PC no cambian.
        bool ligera = d.objetivo == BuildTarget.WebGL || d.objetivo == BuildTarget.Android;
        int tamanoAntes = EditorUserBuildSettings.overrideMaxTextureSize;
        if (ligera)
        {
            EditorUserBuildSettings.overrideMaxTextureSize = TexturaMaximaMovil;
            if (d.objetivo == BuildTarget.WebGL) EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.ASTC;
            else EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        }

        UnityEditor.Build.Reporting.BuildReport informe;
        try { informe = BuildPipeline.BuildPlayer(opciones); }
        finally { if (ligera) EditorUserBuildSettings.overrideMaxTextureSize = tamanoAntes; }
        var r = informe.summary;
        if (r.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            return nombre + ": la build ha FALLADO (" + r.totalErrors + " errores). Mira la consola.";

        return nombre + ": build lista en " + Path.GetFullPath(ruta) + " (" + (TamanoEnDisco(ruta) / (1024 * 1024)) +
               " MB, " + Mathf.RoundToInt((float)r.totalTime.TotalSeconds) + " s)";
    }

    // Lo que ocupa de verdad (ya comprimido), sea un archivo (.apk) o una carpeta (web, .app)
    private static long TamanoEnDisco(string ruta)
    {
        if (File.Exists(ruta)) return new FileInfo(ruta).Length;
        if (!Directory.Exists(ruta)) return 0;
        return Directory.GetFiles(ruta, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("DoNotShip")).Sum(f => new FileInfo(f).Length);
    }
}
