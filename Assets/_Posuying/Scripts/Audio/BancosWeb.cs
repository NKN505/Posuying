using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

// Pantalla de arranque de la version web: deja los bancos de sonido de Wwise
// donde Wwise los busca y despues abre el juego.
//
// En el navegador Wwise NO lee los bancos de StreamingAssets (alli son
// direcciones web, no archivos): los busca en el almacenamiento interno del
// navegador (Application.persistentDataPath). Esta escena los descarga ahi la
// primera vez (~50 MB) y las siguientes veces, si no han cambiado, entra directa.
//
// La lista de archivos (StreamingAssets/BancosWeb.txt) la escribe Unity al hacer
// la build web (Multiplataforma.Compilar). Esta escena solo entra en esa build;
// en el resto de plataformas Wwise lee los bancos por su cuenta.
public class BancosWeb : MonoBehaviour
{
    [Tooltip("Escena del juego que se abre al terminar")]
    public string escenaJuego = "Mapa";
    [Tooltip("Descargas a la vez")]
    public int simultaneas = 6;

    public const string Manifiesto = "BancosWeb.txt";
    public const string Carpeta = "Audio/GeneratedSoundBanks/Web";

    private float _progreso;
    private string _estado = "Cargando...";

    IEnumerator Start()
    {
        if (Plataforma.EsWeb) yield return Descargar();
        SceneManager.LoadScene(escenaJuego);
    }

    private IEnumerator Descargar()
    {
        string raiz = Application.streamingAssetsPath + "/";
        var lista = UnityWebRequest.Get(raiz + Manifiesto);
        yield return lista.SendWebRequest();
        if (lista.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("[BancosWeb] No hay lista de bancos (" + lista.error + "): el juego ira sin sonido de Wwise.");
            yield break;
        }

        // Primera linea: version de los bancos. El resto: un archivo por linea
        string[] lineas = lista.downloadHandler.text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        if (lineas.Length < 2) yield break;
        string version = lineas[0];
        string[] archivos = lineas.Skip(1).ToArray();

        string destino = Path.Combine(Application.persistentDataPath, Carpeta);
        string marca = Path.Combine(destino, ".version");
        if (File.Exists(marca) && File.ReadAllText(marca) == version) yield break;   // ya estan

        // Bancos de otra version: fuera, que no se mezclen
        if (Directory.Exists(destino)) Directory.Delete(destino, true);

        int siguiente = 0, hechos = 0, fallos = 0;
        var activas = new List<KeyValuePair<UnityWebRequest, string>>();
        while (hechos < archivos.Length)
        {
            while (activas.Count < simultaneas && siguiente < archivos.Length)
            {
                string rel = archivos[siguiente++];
                string url = raiz + Carpeta + "/" + string.Join("/", rel.Split('/').Select(UnityWebRequest.EscapeURL).Select(s => s.Replace("+", "%20")));
                var r = UnityWebRequest.Get(url);
                r.SendWebRequest();
                activas.Add(new KeyValuePair<UnityWebRequest, string>(r, rel));
            }

            for (int i = activas.Count - 1; i >= 0; i--)
            {
                var r = activas[i].Key;
                if (!r.isDone) continue;

                if (r.result == UnityWebRequest.Result.Success)
                {
                    string ruta = Path.Combine(destino, activas[i].Value);
                    Directory.CreateDirectory(Path.GetDirectoryName(ruta));
                    File.WriteAllBytes(ruta, r.downloadHandler.data);
                }
                else
                {
                    fallos++;
                    Debug.LogWarning("[BancosWeb] No se pudo descargar " + activas[i].Value + ": " + r.error);
                }
                r.Dispose();
                activas.RemoveAt(i);
                hechos++;
            }

            _progreso = (float)hechos / archivos.Length;
            _estado = "Cargando el sonido... " + Mathf.RoundToInt(_progreso * 100f) + " %";
            yield return null;
        }

        // Solo se da por bueno si ha llegado todo: si no, se reintenta la proxima vez
        if (fallos == 0) File.WriteAllText(marca, version);
    }

    void OnGUI()
    {
        float h = Screen.height, w = Screen.width;
        GUI.color = new Color(0.055f, 0.067f, 0.078f, 1f);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);

        var estilo = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(h / 30f), fontStyle = FontStyle.Bold
        };
        GUI.color = new Color(0.91f, 0.9f, 0.87f, 1f);
        GUI.Label(new Rect(0, h * 0.42f, w, h * 0.08f), _estado, estilo);

        float bw = w * 0.4f, bh = Mathf.Max(4f, h / 120f);
        Rect barra = new Rect((w - bw) / 2f, h * 0.52f, bw, bh);
        GUI.color = new Color(1f, 1f, 1f, 0.15f);
        GUI.DrawTexture(barra, Texture2D.whiteTexture);
        GUI.color = new Color(0.78f, 0.17f, 0.13f, 1f);
        GUI.DrawTexture(new Rect(barra.x, barra.y, barra.width * _progreso, barra.height), Texture2D.whiteTexture);
    }
}
