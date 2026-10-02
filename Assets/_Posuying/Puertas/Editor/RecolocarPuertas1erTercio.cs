// Recoloca las 30 PuertaMadera de "Puertas1er tercio" con los valores de tu guardado de las 19:51.
// Empareja por ORDEN en la jerarquía y comprueba antes que el orden coincide (las dobles hacen de referencia).
// Se deshace con Ctrl+Z. Menú: Posuying > Puertas > Recolocar puertas 1er tercio
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class RecolocarPuertas1erTercio
{
    struct D { public int idx; public string name; public float[] v;
        public D(int i, string n, float[] x) { idx = i; name = n; v = x; } }

    static readonly D[] Datos = {
        new D(0, "PuertaMadera (1)", new float[] { -71.05373f, 5.5114036f, 71.92934f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 169.86028f, 86.933846f, 121.814644f }),
        new D(1, "PuertaMadera (2)", new float[] { -71.086845f, 5.5114045f, 80.627815f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 183.6722f, 86.933846f, 121.814644f }),
        new D(2, "PuertaMadera (3)", new float[] { -57.503807f, 5.511406f, 80.57462f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 175.75151f, 86.933846f, 121.814644f }),
        new D(3, "PuertaMadera (4)", new float[] { -51.48657f, 5.5114064f, 79.45145f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 86.4757f, 86.933846f, 99.13763f }),
        new D(4, "PuertaMadera (5)", new float[] { -51.51283f, 5.511407f, 83.68252f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 91.904434f, 86.933846f, 101.19752f }),
        new D(5, "PuertaMadera (6)", new float[] { -53.704254f, 5.511407f, 82.494965f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 127.28792f, 86.933846f, 103.89571f }),
        new D(7, "PuertaMadera (7)", new float[] { -55.555073f, 5.5114074f, 90.106064f, 0.011787637f, -0.70700854f, -0.70700854f, -0.011787637f, 91.05797f, 86.93384f, 97.92071f }),
        new D(8, "PuertaMadera (8)", new float[] { -73.048775f, 5.5114064f, 90.07216f, -0.70709467f, 0.004142076f, 0.004142076f, 0.70709467f, 94.37112f, 86.93385f, 99.40197f }),
        new D(10, "PuertaMadera (9)", new float[] { -76.992096f, 5.511404f, 79.47231f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 84.7669f, 86.933846f, 99.56876f }),
        new D(11, "PuertaMadera (10)", new float[] { -77.011215f, 5.511405f, 83.70685f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 102.3325f, 86.933846f, 99.81248f }),
        new D(12, "PuertaMadera (11)", new float[] { -74.88432f, 5.511405f, 82.50022f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 122.74194f, 86.933846f, 101.740814f }),
        new D(13, "PuertaMadera (12)", new float[] { -72.474884f, 9.111406f, 81.8004f, -0.70710313f, 0.0022710182f, 0.0022710182f, 0.70710313f, 99.94005f, 86.93385f, 96.313965f }),
        new D(14, "PuertaMadera (13)", new float[] { -75.57769f, 9.111408f, 90.026085f, -0.7070923f, 0.004535444f, 0.004535444f, 0.7070923f, 138.88489f, 86.933846f, 99.74792f }),
        new D(15, "PuertaMadera (14)", new float[] { -73.712074f, 9.111406f, 71.52374f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 108.693405f, 86.933846f, 121.814644f }),
        new D(17, "PuertaMadera (15)", new float[] { -51.976418f, 9.111408f, 71.45962f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 81.43507f, 86.933846f, 103.65695f }),
        new D(18, "PuertaMadera (16)", new float[] { -53.690273f, 9.111408f, 79.603584f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 90.546394f, 86.933846f, 99.28355f }),
        new D(19, "PuertaMadera (17)", new float[] { -53.682915f, 9.111408f, 82.59887f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 111.892914f, 86.933846f, 103.228165f }),
        new D(20, "PuertaMadera (18)", new float[] { -51.9734f, 9.111408f, 80.59247f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 88.70951f, 86.933846f, 101.05536f }),
        new D(22, "PuertaMadera", new float[] { -69.54707f, 1.6114014f, 71.58828f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 115.96933f, 86.933846f, 121.814644f }),
        new D(23, "PuertaMadera (1)", new float[] { -69.55577f, 1.6114026f, 82.328964f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 214.93753f, 86.933846f, 121.814644f }),
        new D(24, "PuertaMadera (2)", new float[] { -55.765457f, 1.6114033f, 79.47318f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 179.01239f, 86.933846f, 121.814644f }),
        new D(25, "PuertaMadera (3)", new float[] { -57.26158f, 1.6114035f, 81.78563f, -0.01015931f, -0.7070338f, -0.7070338f, 0.01015931f, 134.35765f, 86.93384f, 131.31616f }),
        new D(26, "PuertaMadera (4)", new float[] { -59.045467f, 1.6114025f, 76.82426f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 133.59663f, 86.933846f, 121.814644f }),
        new D(27, "PuertaMadera (5)", new float[] { -59.048923f, 1.6114025f, 72.05393f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, -152.35745f, 86.933846f, 121.814644f }),
        new D(28, "PuertaMadera (6)", new float[] { -58.997654f, 1.6114033f, 79.51018f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 175.69353f, 86.933846f, 121.814644f }),
        new D(29, "PuertaMadera (7)", new float[] { -69.61575f, 1.6114037f, 91.15883f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 145.4637f, 86.933846f, 121.814644f }),
        new D(30, "PuertaMadera (8)", new float[] { -58.849f, 1.6114043f, 91.17268f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 145.80821f, 86.933846f, 121.814644f }),
        new D(31, "PuertaMadera (9)", new float[] { -58.004868f, -1.5885942f, 90.883385f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 164.32855f, 86.933846f, 111.595604f }),
        new D(33, "PuertaMadera (10)", new float[] { -58.046814f, -1.5885968f, 72.072495f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 159.80573f, 86.933846f, 114.232895f }),
        new D(35, "PuertaMadera", new float[] { -57.510002f, 5.5114055f, 71.92645f, -0.50073534f, -0.4992636f, -0.4992636f, 0.50073534f, 179.57849f, 86.933846f, 121.814644f }),
    };

    static readonly Dictionary<int, string> Referencias = new Dictionary<int, string> {
        { 6, "PuertaMaderaDoble" },
        { 9, "PuertaMaderaDoble (1)" },
        { 16, "PuertaMaderaDoble (2)" },
        { 21, "PuertaMaderaDobleExterior" },
        { 32, "PuertaMaderaDoble" },
        { 34, "PuertaMaderaDoble (1)" },
    };

    [MenuItem("Posuying/Puertas/Recolocar puertas 1er tercio")]
    static void Recolocar()
    {
        Transform c = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == "Puertas1er tercio") { c = t; break; }
        if (c == null) { Debug.LogError("[Puertas] No encuentro 'Puertas1er tercio'."); return; }
        if (c.childCount != 36) { Debug.LogError($"[Puertas] Hay {c.childCount} hijos y esperaba 36. No toco nada."); return; }
        foreach (var kv in Referencias)
            if (c.GetChild(kv.Key).name != kv.Value)
            { Debug.LogError($"[Puertas] En la posición {kv.Key} esperaba '{kv.Value}' y hay '{c.GetChild(kv.Key).name}'. No toco nada."); return; }

        Undo.SetCurrentGroupName("Recolocar puertas 1er tercio");
        int g = Undo.GetCurrentGroup();
        int n = 0;
        foreach (var d in Datos)
        {
            var t = c.GetChild(d.idx);
            Undo.RecordObject(t, "Recolocar puerta");
            Undo.RecordObject(t.gameObject, "Recolocar puerta");
            Vector3 p = t.localPosition, s = t.localScale; Quaternion r = t.localRotation;
            if (!float.IsNaN(d.v[0])) p.x = d.v[0];
            if (!float.IsNaN(d.v[1])) p.y = d.v[1];
            if (!float.IsNaN(d.v[2])) p.z = d.v[2];
            if (!float.IsNaN(d.v[3])) r = new Quaternion(d.v[3], d.v[4], d.v[5], d.v[6]);
            if (!float.IsNaN(d.v[7])) s.x = d.v[7];
            if (!float.IsNaN(d.v[8])) s.y = d.v[8];
            if (!float.IsNaN(d.v[9])) s.z = d.v[9];
            t.localPosition = p; t.localRotation = r; t.localScale = s;
            t.gameObject.name = d.name;
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
            n++;
        }
        Undo.CollapseUndoOperations(g);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        Debug.Log($"[Puertas] Recolocadas {n} puertas en su sitio. Revisa y guarda (Ctrl+S).");
    }
}
