using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using Debug = UnityEngine.Debug;

// Posuying > Multiplataforma > Publicar web en Vercel
//
// Hace la build web y la sube directamente a Vercel con su herramienta de linea
// de comandos (sin pasar por git). Al terminar, la web nueva ya esta en linea.
//
// Requisitos, una sola vez en el PC:
//   1. Node.js        winget install OpenJS.NodeJS.LTS
//   2. Vercel CLI     npm install -g vercel
//   3. Iniciar sesion vercel login
//
// La build se copia a Builds/WebVercel, que es lo que se sube. Ahi se guarda
// tambien la carpeta .vercel, que dice a que proyecto de Vercel va: no la borres
// o la siguiente subida creara un proyecto nuevo.
public static class PublicarWeb
{
    /// <summary>Nombre del proyecto en Vercel (la web queda en https://posuying.vercel.app si esta libre).</summary>
    public const string ProyectoVercel = "posuying";

    private static string Proyecto => Directory.GetParent(UnityEngine.Application.dataPath).FullName;
    public static string CarpetaSubida => Path.Combine(Proyecto, "Builds", "WebVercel");

    [MenuItem("Posuying/Multiplataforma/Publicar web en Vercel")]
    private static void Menu()
    {
        if (!EditorUtility.DisplayDialog("Publicar web en Vercel",
                "Se hara la build web (unos minutos) y se subira a Vercel, version " +
                UnityEngine.Application.version + ".\n\nAntes de publicar una version nueva, sube la version en " +
                "Project Settings > Player > Version: asi los moviles que la tienen instalada se actualizan.",
                "Publicar", "Cancelar"))
            return;

        string resultado = Publicar(true);
        Debug.Log("[PublicarWeb] " + resultado);
        EditorUtility.DisplayDialog("Publicar web en Vercel", resultado, "Vale");
    }

    /// <summary>Hace la build (si se pide), la sube a Vercel y devuelve como fue.</summary>
    public static string Publicar(bool compilar)
    {
        if (Ejecutar("vercel --version", Proyecto, out _) != 0)
            return "No encuentro la herramienta de Vercel. Una sola vez, en una terminal:\n" +
                   "  winget install OpenJS.NodeJS.LTS\n  npm install -g vercel\n  vercel login\n" +
                   "y despues reinicia Unity.";

        if (compilar)
        {
            string build = Multiplataforma.Compilar("Web");
            if (!build.Contains("build lista")) return build;
        }

        string origen = Path.Combine(Proyecto, "Builds", "Web");
        if (!File.Exists(Path.Combine(origen, "index.html"))) return "No hay build web en " + origen + ". Compilala primero.";

        try
        {
            EditorUtility.DisplayProgressBar("Publicar web", "Preparando los archivos...", 0.1f);
            Preparar(origen);

            // La primera vez se enlaza la carpeta con el proyecto de Vercel (se crea si no existe)
            if (!Directory.Exists(Path.Combine(CarpetaSubida, ".vercel")))
            {
                EditorUtility.DisplayProgressBar("Publicar web", "Creando el proyecto en Vercel...", 0.2f);
                if (Ejecutar("vercel link --yes --project " + ProyectoVercel, CarpetaSubida, out string enlace) != 0)
                    return "No se pudo enlazar con Vercel (¿has hecho \"vercel login\"?):\n" + Ultimas(enlace);
            }

            EditorUtility.DisplayProgressBar("Publicar web", "Subiendo a Vercel (~70 MB)...", 0.4f);
            if (Ejecutar("vercel deploy --prod --yes", CarpetaSubida, out string salida) != 0)
                return "La subida a Vercel ha fallado:\n" + Ultimas(salida);

            var url = Regex.Matches(salida, @"https://[^\s]+\.vercel\.app").Cast<Match>().Select(m => m.Value).LastOrDefault();
            return "Web publicada (version " + UnityEngine.Application.version + ")" +
                   (url != null ? ":\n" + url : ".\n" + Ultimas(salida)) +
                   "\n\nEn el iPhone: abrela en Safari > Compartir > \"Anadir a pantalla de inicio\".";
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // Copia la build a la carpeta de subida (conservando el enlace .vercel) y anade la configuracion
    private static void Preparar(string origen)
    {
        Directory.CreateDirectory(CarpetaSubida);
        foreach (var f in Directory.GetFiles(CarpetaSubida, "*", SearchOption.AllDirectories))
            if (!f.Substring(CarpetaSubida.Length + 1).Replace('\\', '/').StartsWith(".vercel")) File.Delete(f);
        foreach (var d in Directory.GetDirectories(CarpetaSubida).Where(d => Path.GetFileName(d) != ".vercel"))
            Directory.Delete(d, true);

        foreach (var f in Directory.GetFiles(origen, "*", SearchOption.AllDirectories))
        {
            string destino = Path.Combine(CarpetaSubida, f.Substring(origen.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(destino));
            File.Copy(f, destino, true);
        }

        // Que el navegador pregunte siempre si hay version nueva (el ServiceWorker
        // ya guarda el juego en el dispositivo, por version). Sin nada que compilar.
        File.WriteAllText(Path.Combine(CarpetaSubida, "vercel.json"),
@"{
  ""buildCommand"": """",
  ""outputDirectory"": ""."",
  ""headers"": [
    { ""source"": ""/(.*)"", ""headers"": [ { ""key"": ""Cache-Control"", ""value"": ""no-cache"" } ] }
  ]
}
");
    }

    // En Windows "vercel" es un .cmd: se lanza a traves de cmd
    private static int Ejecutar(string comando, string carpeta, out string salida)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo("cmd.exe", "/c " + comando)
            {
                WorkingDirectory = carpeta, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        try { p.Start(); }
        catch (System.Exception e) { salida = e.Message; return -1; }

        // Leer las dos salidas a la vez: si una se llena mientras se espera la otra, se cuelga
        var error = p.StandardError.ReadToEndAsync();
        salida = p.StandardOutput.ReadToEnd() + error.Result;
        p.WaitForExit();
        return p.ExitCode;
    }

    private static string Ultimas(string texto) =>
        string.Join("\n", texto.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).Reverse().Take(8).Reverse());
}
