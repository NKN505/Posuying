using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Coloca los edificios modelados en Blender sobre el greybox de TestMap.
///
/// DOS TRAMPAS QUE COSTARON CARO, documentadas para no repetirlas:
///
/// 1) El greybox cuelga de Environment_Prefab/Greybox, que esta en
///    (145, -2, 28). Al deducir las coordenadas leyendo el .unity en YAML se
///    leyo m_LocalPosition como si fuera posicion de mundo, y los edificios
///    acabaron 300 m al oeste. Las constantes de aqui YA llevan sumado ese
///    desplazamiento.
///
/// 2) La exportacion FBX (axis_forward='-Z' + bake_space_transform) hornea
///    180 grados sobre Y dentro de la malla: un modelo que en Blender crece
///    hacia +X/+Y llega creciendo hacia -X/-Z con rotacion cero. Por eso se
///    instancia con rotationY = 180, y asi el pivote vuelve a ser la esquina
///    suroeste. No se ve en las dimensiones, que salen correctas: hay que
///    mirar los bounds CONTRA el pivote.
///
/// La altura no va clavada: se sondea el suelo con raycast bajo la huella.
///
/// Uso:
///   Menu   Posuying > Colocar edificios en TestMap
///   Batch  Unity.exe -batchmode -quit -nographics -projectPath [proyecto]
///                    -executeMethod ColocarEdificios.DesdeConsola
/// </summary>
public static class ColocarEdificios
{
    const string RutaEscena = "Assets/Scenes/TestMap.unity";
    const string CarpetaArt = "Assets/Art/Edificios";

    struct Colocacion
    {
        public string Fbx;
        public float X, Z, Ancho, Fondo;
        public Colocacion(string fbx, float x, float z, float ancho, float fondo)
        {
            Fbx = fbx; X = x; Z = z; Ancho = ancho; Fondo = fondo;
        }
    }

    // Esquina suroeste de cada huella, ya en coordenadas de MUNDO.
    // Deducidas del mapa (0.623 m/px) + el desplazamiento (145, 28) del Greybox.
    static readonly Colocacion[] Edificios =
    {
        new Colocacion("Conserjeria",        -12.151f, 125.537f, 14.95f, 10.59f),
        new Colocacion("Nave_Mantenimiento",  29.590f, 128.652f, 14.33f, 10.59f),
    };

    [MenuItem("Posuying/Colocar edificios en TestMap")]
    public static void DesdeMenu()
    {
        var escena = SceneManager.GetActiveScene();
        if (escena.path != RutaEscena)
        {
            EditorUtility.DisplayDialog("Escena incorrecta",
                "Abre " + RutaEscena + " antes de ejecutar esto.", "Vale");
            return;
        }
        if (Colocar(escena)) EditorSceneManager.SaveScene(escena);
    }

    public static void DesdeConsola()
    {
        var escena = EditorSceneManager.OpenScene(RutaEscena, OpenSceneMode.Single);
        if (Colocar(escena)) EditorSceneManager.SaveScene(escena);
        AssetDatabase.SaveAssets();
    }

    /// <summary>Rehornea el NavMesh con los edificios recortandolo.</summary>
    [MenuItem("Posuying/Rehornear NavMesh")]
    public static void RehornearNavMesh()
    {
        var escena = SceneManager.GetActiveScene().path == RutaEscena
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(RutaEscena, OpenSceneMode.Single);

        foreach (var e in Edificios)
        {
            var go = BuscarRaiz(escena, e.Fbx);
            if (go == null) continue;
            foreach (var t in go.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    GameObjectUtility.GetStaticEditorFlags(t.gameObject)
                    | StaticEditorFlags.NavigationStatic);
        }
        UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
        UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        AssetDatabase.SaveAssets();
        Debug.Log("[Nav] NavMesh rehorneado y escena guardada.");
    }

    /// <summary>Comprueba posicion, orientacion y apoyo de cada edificio.</summary>
    public static void Verificar()
    {
        var escena = EditorSceneManager.OpenScene(RutaEscena, OpenSceneMode.Single);
        foreach (var e in Edificios)
        {
            var go = BuscarRaiz(escena, e.Fbx);
            if (go == null) { Debug.Log("[Verif] FALTA " + e.Fbx); continue; }
            var rends = go.GetComponentsInChildren<MeshRenderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            var p = go.transform.position;
            Debug.Log("[Verif] " + e.Fbx
                + " pos=" + p.ToString("F2")
                + " rotY=" + go.transform.eulerAngles.y.ToString("F0")
                + " | X " + b.min.x.ToString("F2") + ".." + b.max.x.ToString("F2")
                + " (crece " + (b.max.x > p.x + 1f ? "+X ok" : "-X MAL") + ")"
                + " | Z " + b.min.z.ToString("F2") + ".." + b.max.z.ToString("F2")
                + " (crece " + (b.max.z > p.z + 1f ? "+Z ok" : "-Z MAL") + ")"
                + " | Y " + b.min.y.ToString("F2") + ".." + b.max.y.ToString("F2")
                + " | colliders=" + go.GetComponentsInChildren<MeshCollider>().Length);
        }
    }

    static GameObject BuscarRaiz(Scene escena, string nombre)
    {
        foreach (var g in escena.GetRootGameObjects())
            if (g != null && g.name == nombre) return g;
        return null;
    }

    /// <summary>Sondea el suelo bajo la huella y devuelve la cota de apoyo.</summary>
    static float SondearSuelo(GameObject go, Colocacion e, out int aciertos, out float minimo)
    {
        var cols = go.GetComponentsInChildren<Collider>();
        foreach (var c in cols) c.enabled = false;      // ignorar el propio edificio

        float maximo = float.NegativeInfinity;
        minimo = float.PositiveInfinity;
        aciertos = 0;
        for (int i = 0; i <= 5; i++)
            for (int j = 0; j <= 4; j++)
            {
                float px = e.X + 0.6f + i * (e.Ancho - 1.2f) / 5f;
                float pz = e.Z + 0.6f + j * (e.Fondo - 1.2f) / 4f;
                RaycastHit h;
                if (Physics.Raycast(new Vector3(px, 300f, pz), Vector3.down, out h, 800f))
                {
                    maximo = Mathf.Max(maximo, h.point.y);
                    minimo = Mathf.Min(minimo, h.point.y);
                    aciertos++;
                }
            }

        foreach (var c in cols) c.enabled = true;
        return aciertos > 0 ? maximo : 0f;
    }

    static bool Colocar(Scene escena)
    {
        int puestos = 0;
        foreach (var e in Edificios)
        {
            var ruta = CarpetaArt + "/" + e.Fbx + ".fbx";
            var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
            if (modelo == null) { Debug.LogError("[Posuying] falta " + ruta); continue; }

            var previo = BuscarRaiz(escena, e.Fbx);
            if (previo != null) Object.DestroyImmediate(previo);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(modelo, escena);
            inst.name = e.Fbx;
            inst.transform.rotation = Quaternion.Euler(0f, 180f, 0f);  // deshacer el horneado
            inst.transform.position = new Vector3(e.X, 300f, e.Z);     // fuera de en medio
            inst.isStatic = true;

            foreach (var filtro in inst.GetComponentsInChildren<MeshFilter>())
            {
                var col = filtro.gameObject.AddComponent<MeshCollider>();
                col.sharedMesh = filtro.sharedMesh;
            }

            int aciertos; float minimo;
            float y = SondearSuelo(inst, e, out aciertos, out minimo);
            inst.transform.position = new Vector3(e.X, y, e.Z);

            Debug.Log("[Posuying] " + e.Fbx + " -> " + inst.transform.position.ToString("F2")
                      + "  suelo sondeado " + minimo.ToString("F2") + ".." + y.ToString("F2")
                      + " (" + aciertos + "/30)");
            puestos++;
        }

        if (puestos > 0) EditorSceneManager.MarkSceneDirty(escena);
        Debug.Log("[Posuying] colocados " + puestos + " de " + Edificios.Length);
        return puestos > 0;
    }
}
