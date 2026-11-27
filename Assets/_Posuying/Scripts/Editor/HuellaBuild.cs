using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Antes de cada build (por el menu Posuying o por File > Build) apunta en
// Resources/HuellaBuild.txt una huella del codigo del juego. Plataforma.Huella la lee
// y SalaEspera no deja jugar juntas a dos builds de la misma version con codigo
// distinto (todas eran "0.2.0" y cada companero tenia una distinta).
//
// Las builds de todas las plataformas hechas con el mismo codigo tienen la misma
// huella, asi que el juego cruzado sigue funcionando.
public class HuellaBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    private const string Ruta = "Assets/_Posuying/Resources/HuellaBuild.txt";

    public void OnPreprocessBuild(BuildReport report)
    {
        string huella = Calcular();
        Directory.CreateDirectory(Path.GetDirectoryName(Ruta));
        File.WriteAllText(Ruta, huella);
        AssetDatabase.ImportAsset(Ruta, ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("[HuellaBuild] " + huella);
    }

    /// <summary>
    /// Huella de los scripts del juego (sin los de editor). No cambia por los saltos
    /// de linea de Windows/Mac ni por el orden de los archivos.
    /// </summary>
    public static string Calcular()
    {
        string raiz = Path.Combine(Application.dataPath, "_Posuying/Scripts").Replace('\\', '/');
        var archivos = Directory.GetFiles(raiz, "*.cs", SearchOption.AllDirectories)
            .Select(f => f.Replace('\\', '/'))
            .Where(f => !f.Contains("/Editor/"))
            .OrderBy(f => f, System.StringComparer.Ordinal);

        var todo = new StringBuilder();
        foreach (var f in archivos)
        {
            string texto = File.ReadAllText(f).Replace("\r", "").TrimStart('﻿');
            todo.Append(f.Substring(raiz.Length)).Append('\n').Append(texto).Append('\n');
        }

        using (var sha = SHA1.Create())
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(todo.ToString()));
            return string.Concat(h.Take(4).Select(b => b.ToString("x2")));
        }
    }
}
