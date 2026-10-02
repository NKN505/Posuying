// Herramientas de puertas del 1er tercio (edificio de telecomunicaciones).
// "Volcar estado" solo LEE: escribe Temp/puertas_1er_tercio.json con la jerarquía y transforms actuales.
using System.IO;
using System.Text;
using System.Globalization;
using UnityEditor;
using UnityEngine;

public static class RestaurarPuertas1erTercio
{
    static Transform Contenedor()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == "Puertas1er tercio") return t;
        return null;
    }

    static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "[{0},{1},{2}]", v.x, v.y, v.z);
    static string Q(Quaternion q) => string.Format(CultureInfo.InvariantCulture, "[{0},{1},{2},{3}]", q.x, q.y, q.z, q.w);

    static void Volcar(StringBuilder sb, Transform t, int nivel)
    {
        var mr = t.GetComponent<MeshRenderer>();
        var mc = t.GetComponent<MeshCollider>();
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        string prefab = src != null ? AssetDatabase.GetAssetPath(src) : "";
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "{{\"nivel\":{0},\"nombre\":\"{1}\",\"id\":{2},\"prefab\":\"{3}\",\"lp\":{4},\"lr\":{5},\"ls\":{6},\"wp\":{7},\"wr\":{8},\"ws\":{9},\"render\":{10},\"collider\":{11},\"activo\":{12}}},\n",
            nivel, t.name.Replace("\"", "'"), t.gameObject.GetInstanceID(), prefab, V(t.localPosition), Q(t.localRotation), V(t.localScale),
            V(t.position), Q(t.rotation), V(t.lossyScale), mr != null ? "true" : "false", mc != null ? "true" : "false",
            t.gameObject.activeInHierarchy ? "true" : "false");
        for (int i = 0; i < t.childCount; i++) Volcar(sb, t.GetChild(i), nivel + 1);
    }

    [MenuItem("Posuying/Puertas/Volcar estado de puertas 1er tercio (solo lectura)")]
    static void VolcarEstado()
    {
        var c = Contenedor();
        if (c == null) { Debug.LogError("[Puertas] No encuentro 'Puertas1er tercio'."); return; }
        var sb = new StringBuilder("[\n");
        Volcar(sb, c, 0);
        sb.Append("{}]\n");
        var ruta = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "puertas_1er_tercio.json");
        File.WriteAllText(ruta, sb.ToString());
        Debug.Log("[Puertas] Estado volcado en " + ruta);
    }
}
