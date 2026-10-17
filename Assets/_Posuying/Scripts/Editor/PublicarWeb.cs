using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using Debug = UnityEngine.Debug;

// Posuying > Multiplataforma > Publicar web (rama web para Vercel)
//
// La version web se publica en Vercel desde una rama aparte del repositorio,
// "web", que SOLO tiene la build (index.html, Build/, TemplateData/...), nada
// del proyecto de Unity. Vercel la sirve tal cual cada vez que se sube.
//
// Esa rama vive en su propia carpeta, al lado del proyecto:
//     D:\Windows\ProyectosUnity\Posuying-web
// (un "git worktree": es el mismo repositorio, pero con la rama web abierta alli,
// asi no hay que cambiar de rama en el proyecto para publicar).
//
// Este menu: hace la build web, la copia a esa carpeta y deja escritos los dos
// comandos para subirla. El commit y el push los haces tu.
public static class PublicarWeb
{
    public const string Rama = "web";

    private static string Proyecto => Directory.GetParent(UnityEngine.Application.dataPath).FullName;
    public static string CarpetaRama => Path.Combine(Directory.GetParent(Proyecto).FullName, "Posuying-web");

    [MenuItem("Posuying/Multiplataforma/Publicar web (rama web para Vercel)")]
    private static void Menu()
    {
        if (!EditorUtility.DisplayDialog("Publicar web",
                "Se hara la build web (unos minutos) y se copiara a la rama \"" + Rama + "\" en:\n" + CarpetaRama +
                "\n\nDespues solo tendras que hacer commit y push de esa carpeta.", "Adelante", "Cancelar"))
            return;

        string resultado = Publicar(true);
        Debug.Log("[PublicarWeb] " + resultado);
        EditorUtility.DisplayDialog("Publicar web", resultado, "Vale");
    }

    /// <summary>Hace la build (si se pide), la copia a la rama web y devuelve que hacer despues.</summary>
    public static string Publicar(bool compilar)
    {
        if (compilar)
        {
            string build = Multiplataforma.Compilar("Web");
            if (!build.Contains("build lista")) return build;
        }

        string origen = Path.Combine(Proyecto, "Builds", "Web");
        if (!File.Exists(Path.Combine(origen, "index.html"))) return "No hay build web en " + origen + ". Compilala primero.";

        // GitHub no admite archivos de mas de 100 MB (sin LFS, que no queremos para esto)
        var grandes = Directory.GetFiles(origen, "*", SearchOption.AllDirectories)
            .Where(f => new FileInfo(f).Length > 95L * 1024 * 1024).ToArray();
        if (grandes.Length > 0)
            return "No se puede subir: " + string.Join(", ", grandes.Select(Path.GetFileName)) +
                   " pasa de 95 MB y GitHub no admite archivos de mas de 100 MB. Hay que aligerar la build.";

        string error = PrepararCarpeta();
        if (error != null) return error;

        // Fuera lo de la publicacion anterior (menos .git y lo que es de la rama)
        foreach (var f in Directory.GetFiles(CarpetaRama, "*", SearchOption.AllDirectories))
        {
            string rel = f.Substring(CarpetaRama.Length + 1).Replace('\\', '/');
            if (rel == ".git" || rel.StartsWith(".git/") || rel == "vercel.json" || rel == "README.md" || rel == ".gitattributes") continue;
            File.Delete(f);
        }
        foreach (var dir in Directory.GetDirectories(CarpetaRama).Where(d => !Path.GetFileName(d).StartsWith(".git")))
            Directory.Delete(dir, true);

        // La build nueva
        foreach (var f in Directory.GetFiles(origen, "*", SearchOption.AllDirectories))
        {
            string rel = f.Substring(origen.Length + 1);
            string destino = Path.Combine(CarpetaRama, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destino));
            File.Copy(f, destino, true);
        }

        EscribirArchivosDeLaRama();

        long mb = Directory.GetFiles(CarpetaRama, "*", SearchOption.AllDirectories)
                           .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".git"))
                           .Sum(f => new FileInfo(f).Length) / (1024 * 1024);
        return "Build web (" + mb + " MB, version " + UnityEngine.Application.version + ") copiada en:\n" + CarpetaRama +
               "\n\nPara publicarla, en esa carpeta:\n" +
               "  git add -A\n  git commit -m \"Web " + UnityEngine.Application.version + "\"\n  git push -u origin " + Rama +
               "\n\nVercel la pone en linea sola al recibir el push.";
    }

    // Crea la carpeta de la rama la primera vez (o la recupera si ya existia en GitHub)
    private static string PrepararCarpeta()
    {
        if (Directory.Exists(Path.Combine(CarpetaRama, ".git")) || File.Exists(Path.Combine(CarpetaRama, ".git")))
            return null;

        Git("fetch origin " + Rama, out _);   // por si un companero ya la creo
        bool enGitHub = Git("rev-parse --verify --quiet refs/remotes/origin/" + Rama, out _) == 0;
        bool enLocal = Git("rev-parse --verify --quiet refs/heads/" + Rama, out _) == 0;

        string comando = enLocal ? "worktree add \"" + CarpetaRama + "\" " + Rama
                       : enGitHub ? "worktree add -b " + Rama + " \"" + CarpetaRama + "\" origin/" + Rama
                       // Rama huerfana: empieza vacia, sin nada del proyecto de Unity
                       : "worktree add --orphan -b " + Rama + " \"" + CarpetaRama + "\"";

        return Git(comando, out string salida) == 0 ? null : "No se pudo preparar la rama " + Rama + ":\n" + salida;
    }

    private static void EscribirArchivosDeLaRama()
    {
        // Sin LFS en esta rama: Vercel tendria que bajarse cada build por LFS y la
        // cuota gratuita de GitHub se acabaria en unas pocas publicaciones
        File.WriteAllText(Path.Combine(CarpetaRama, ".gitattributes"),
            "# Rama web: archivos normales, sin Git LFS\n* -filter -diff -merge -text\n");

        // Que el navegador pregunte siempre si hay version nueva (el ServiceWorker
        // ya guarda el juego en el dispositivo, por version)
        File.WriteAllText(Path.Combine(CarpetaRama, "vercel.json"),
@"{
  ""headers"": [
    { ""source"": ""/(.*)"", ""headers"": [ { ""key"": ""Cache-Control"", ""value"": ""no-cache"" } ] },
    { ""source"": ""/(.*).unityweb"", ""headers"": [ { ""key"": ""Content-Type"", ""value"": ""application/octet-stream"" } ] }
  ]
}
");

        File.WriteAllText(Path.Combine(CarpetaRama, "README.md"),
@"# Posuying - version web

Esta rama solo tiene la build web del juego. **No se edita a mano**: la genera
Unity con `Posuying > Multiplataforma > Publicar web (rama web para Vercel)`.

Vercel la publica sola en cada push a esta rama.

Configuracion del proyecto en Vercel (una sola vez):
- Framework Preset: Other
- Build Command: vacio (no hay que compilar nada)
- Output Directory: `.`
- Production Branch: `web`

Antes de publicar una version nueva, sube la version del juego en Unity
(Project Settings > Player > Version): asi los moviles que ya la instalaron
como app se actualizan, y solo juegan juntos jugadores con la misma version.
");
    }

    private static int Git(string argumentos, out string salida)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo("git", argumentos)
            {
                WorkingDirectory = Proyecto, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        p.Start();
        salida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }
}
