using UnityEditor;
using UnityEngine;

// Posuying > Jugador > Poner texturas al jugador (Military)
//
// El cuerpo (genSWAT) y los brazos de primera persona (fpArms_Pistol) salían en
// blanco: el FBX del cuerpo usa el material "genSWAT" del pack, que está vacío,
// y el de los brazos trae embebido "fpArms_mat", también sin texturas.
//
// Esto crea dos materiales URP/Lit propios en Assets/_Posuying/Materiales/Jugador
// con las texturas "Military" del pack (color, normal y oclusión) y los pone en el
// prefab Player, cambiando SOLO esos dos materiales por nombre. No toca el pack,
// ni los FBX, ni el arma, ni nada más del prefab. Se puede volver a pasar sin miedo.
public static class MaterialesJugador
{
    const string PREFAB = "Assets/_Posuying/Prefabs/Jugador/Player.prefab";
    const string CARPETA_PADRE = "Assets/_Posuying/Materiales";
    const string CARPETA = "Assets/_Posuying/Materiales/Jugador";

    // Texturas por GUID (sobreviven a que se muevan las carpetas del pack)
    const string CUERPO_COLOR = "d426e2e4dfc013a439a76e2bac0851e7";   // genSWAT/Textures/Military/Military_D.tif
    const string CUERPO_NORMAL = "d019a0eaec7f1484bbcb31bcc3177646";  // Military_NM.tif
    const string CUERPO_AO = "5a0fa22c9bb449c4a97d70b4c27e4787";      // genSWAT/Textures/Generic_AO.tif
    const string BRAZOS_COLOR = "d9895ca99d035274f9af119d912d76b2";   // fpArms/Textures/Military/fpArms_Military_D.tif
    const string BRAZOS_NORMAL = "5797b061425048d44b758e0486883dd8";  // fpArms_Military_NM.tif
    const string BRAZOS_AO = "5db8e1ef82d82cd41adc17c07d222fc3";      // fpArms/Textures/fpArms_Generic_AO.tif

    // Materiales vacíos que se sustituyen (nombre exacto)
    const string VACIO_CUERPO = "genSWAT";
    const string VACIO_BRAZOS = "fpArms_mat";
    const string MALLA_CUERPO = "genSWAT_LOD";   // genSWAT_LOD0, LOD1, LOD2

    const float BRILLO_CUERPO = 0.25f;   // tela: poco brillo
    const float BRILLO_BRAZOS = 0.30f;

    [MenuItem("Posuying/Jugador/Poner texturas al jugador (Military)")]
    static void Aplicar()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) { Debug.LogError("[Jugador] No encuentro el shader Universal Render Pipeline/Lit."); return; }

        if (!AssetDatabase.IsValidFolder(CARPETA))
            AssetDatabase.CreateFolder(CARPETA_PADRE, "Jugador");

        var cuerpo = Crear("Jugador_Cuerpo_Military", lit, CUERPO_COLOR, CUERPO_NORMAL, CUERPO_AO, BRILLO_CUERPO);
        var brazos = Crear("Jugador_Brazos_Military", lit, BRAZOS_COLOR, BRAZOS_NORMAL, BRAZOS_AO, BRILLO_BRAZOS);
        if (cuerpo == null || brazos == null) return;
        AssetDatabase.SaveAssets();

        var raiz = PrefabUtility.LoadPrefabContents(PREFAB);
        if (raiz == null) { Debug.LogError("[Jugador] No puedo abrir " + PREFAB); return; }
        try
        {
            int nCuerpo = 0, nBrazos = 0;
            foreach (var r in raiz.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool cambio = false;
                // Las mallas del cuerpo (genSWAT_LOD0/1/2) llevan el material del cuerpo en
                // todos sus huecos, tengan el que tengan (LOD0 venía con genSWAT_North, cuya
                // textura está en _MainTex, que URP no lee, y también salía pálido).
                bool esCuerpo = r.gameObject.name.StartsWith(MALLA_CUERPO);
                for (int i = 0; i < mats.Length; i++)
                {
                    if (esCuerpo)
                    {
                        if (mats[i] != cuerpo) { mats[i] = cuerpo; nCuerpo++; cambio = true; }
                        continue;
                    }
                    if (mats[i] == null) continue;
                    if (mats[i].name == VACIO_CUERPO) { mats[i] = cuerpo; nCuerpo++; cambio = true; }
                    else if (mats[i].name == VACIO_BRAZOS) { mats[i] = brazos; nBrazos++; cambio = true; }
                }
                if (cambio) r.sharedMaterials = mats;
            }

            if (nCuerpo + nBrazos == 0)
            {
                Debug.Log("[Jugador] El prefab ya tenía los materiales puestos; solo he actualizado los materiales.");
                return;
            }
            PrefabUtility.SaveAsPrefabAsset(raiz, PREFAB);
            Debug.Log($"[Jugador] Hecho: {nCuerpo} hueco(s) del cuerpo y {nBrazos} de los brazos con texturas Military. " +
                      "Materiales en " + CARPETA + ".");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(raiz);
        }
    }

    static Material Crear(string nombre, Shader lit, string gColor, string gNormal, string gAO, float brillo)
    {
        var color = Tex(gColor);
        if (color == null) { Debug.LogError("[Jugador] Falta la textura de color para " + nombre + " (guid " + gColor + ")."); return null; }

        string ruta = CARPETA + "/" + nombre + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null)
        {
            m = new Material(lit) { name = nombre };
            AssetDatabase.CreateAsset(m, ruta);
        }
        else m.shader = lit;

        m.SetFloat("_WorkflowMode", 1f);      // metálico
        m.EnableKeyword("_METALLIC_SETUP");
        m.DisableKeyword("_SPECULAR_SETUP");
        m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_BaseMap", color);
        m.SetTexture("_MainTex", color);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", brillo);

        var normal = Tex(gNormal);
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
        }
        var ao = Tex(gAO);
        if (ao != null)
        {
            m.SetTexture("_OcclusionMap", ao);
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_OCCLUSIONMAP");
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static Texture2D Tex(string guid)
    {
        string ruta = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(ruta) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
    }
}
