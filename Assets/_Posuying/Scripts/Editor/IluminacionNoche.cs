using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Posuying > Iluminación > Noche con luna (escena abierta)
//
// Convierte la escena abierta (Mapa) en noche, con una luna que alumbra lo justo:
//   - Cielo "Night MoonBurst" (AllSkyFree), copiado y oscurecido en Escenas/Mapa/Cielo_Noche_Luna.mat
//   - La Directional Light pasa a ser la luna: azulada, floja, con sombras suaves y saliendo
//     del mismo lado del cielo en el que está pintada la luna (hacia +Z), algo más alta
//     para que llegue a las calles
//   - Luz ambiente en tres colores, azul muy oscuro (lo que se ve en las sombras)
//   - Niebla azul oscura, para que lo lejano se pierda
//   - Postprocesado propio de noche (copia del SampleSceneProfile + algo menos de color),
//     en Escenas/Mapa/Mapa_Noche_Volume.asset, para no tocar el de las otras escenas
//
// Ctrl+Z lo deshace (salvo los dos assets nuevos, que se quedan en la carpeta).
// Guarda la escena (Ctrl+S) si te gusta. Los valores están arriba, en constantes.
public static class IluminacionNoche
{
    const string CIELO_ORIGEN = "Assets/AllSkyFree/Night MoonBurst/Night Moon Burst.mat";
    const string CIELO = "Assets/_Posuying/Escenas/Mapa/Cielo_Noche_Luna.mat";
    const string PERFIL_ORIGEN = "Assets/Settings/SampleSceneProfile.asset";
    const string PERFIL = "Assets/_Posuying/Escenas/Mapa/Mapa_Noche_Volume.asset";

    // ---- Luna ----
    // El cielo pinta la luna hacia +Z y baja (unos 17°). La luz sale de ahí mismo,
    // pero a 35° de altura: con 17° las calles quedarían enteras en sombra.
    const float LUNA_ALTURA = 35f;
    const float LUNA_RUMBO = 0.6f;
    static readonly Color LUNA_COLOR = new Color(0.62f, 0.72f, 1.00f);
    const float LUNA_INTENSIDAD = 0.35f;
    const float LUNA_FUERZA_SOMBRA = 0.85f;

    // ---- Cielo ----
    const float CIELO_EXPOSICION = 0.6f;

    // ---- Ambiente (lo que se ve donde no da la luna) ----
    static readonly Color AMB_CIELO = new Color(0.090f, 0.110f, 0.170f);
    static readonly Color AMB_HORIZONTE = new Color(0.060f, 0.070f, 0.100f);
    static readonly Color AMB_SUELO = new Color(0.025f, 0.025f, 0.030f);
    const float REFLEJOS = 0.25f;

    // ---- Niebla ----
    static readonly Color NIEBLA_COLOR = new Color(0.035f, 0.045f, 0.070f);
    const float NIEBLA_DENSIDAD = 0.012f;

    // ---- Postprocesado ----
    const float SATURACION = -30f;
    const float BLOOM = 0.8f;
    const float VINETA = 0.3f;

    [MenuItem("Posuying/Iluminación/Noche con luna (escena abierta)")]
    static void Aplicar()
    {
        var escena = EditorSceneManager.GetActiveScene();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Noche con luna");
        int grupo = Undo.GetCurrentGroup();

        // 1. Cielo
        var cielo = AssetDatabase.LoadAssetAtPath<Material>(CIELO);
        if (cielo == null)
        {
            var origen = AssetDatabase.LoadAssetAtPath<Material>(CIELO_ORIGEN);
            if (origen == null)
            {
                Debug.LogError("[Noche] No encuentro " + CIELO_ORIGEN + ". ¿Se ha movido AllSkyFree?");
                return;
            }
            cielo = new Material(origen) { name = "Cielo_Noche_Luna" };
            AssetDatabase.CreateAsset(cielo, CIELO);
        }
        if (cielo.HasProperty("_Exposure")) cielo.SetFloat("_Exposure", CIELO_EXPOSICION);
        EditorUtility.SetDirty(cielo);

        // 2. Luna (la Directional Light de la escena; si no hay, se crea)
        Light luna = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (l.type == LightType.Directional && l.gameObject.scene == escena) { luna = l; break; }
        if (luna == null)
        {
            var go = new GameObject("Luna");
            SceneManager_Mover(go, escena);
            Undo.RegisterCreatedObjectUndo(go, "Luna");
            luna = Undo.AddComponent<Light>(go);
            luna.type = LightType.Directional;
        }
        Undo.RecordObject(luna.gameObject, "Luna");
        Undo.RecordObject(luna.transform, "Luna");
        Undo.RecordObject(luna, "Luna");
        luna.gameObject.name = "Luna";
        luna.gameObject.SetActive(true);
        luna.enabled = true;
        luna.transform.rotation = Quaternion.Euler(180f - LUNA_ALTURA, LUNA_RUMBO, 0f);
        luna.useColorTemperature = false;
        luna.color = LUNA_COLOR;
        luna.intensity = LUNA_INTENSIDAD;
        luna.shadows = LightShadows.Soft;
        luna.shadowStrength = LUNA_FUERZA_SOMBRA;

        // 3. Ambiente, niebla, reflejos
        var rs = ObjetoRenderSettings();
        if (rs != null) Undo.RecordObject(rs, "Noche con luna");
        RenderSettings.skybox = cielo;
        RenderSettings.sun = luna;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = AMB_CIELO;
        RenderSettings.ambientEquatorColor = AMB_HORIZONTE;
        RenderSettings.ambientGroundColor = AMB_SUELO;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = REFLEJOS;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = NIEBLA_COLOR;
        RenderSettings.fogDensity = NIEBLA_DENSIDAD;

        // 4. Postprocesado de noche
        string avisoPerfil = PrepararPostprocesado(escena);

        Undo.CollapseUndoOperations(grupo);
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(escena);
        AssetDatabase.SaveAssets();
        Selection.activeObject = luna.gameObject;
        SceneView.RepaintAll();

        Debug.Log("[Noche] Hecho en '" + escena.name + "': cielo nocturno, luna (" + LUNA_INTENSIDAD +
                  "), ambiente azul oscuro y niebla. " + avisoPerfil +
                  " Guarda la escena (Ctrl+S) si te gusta; Ctrl+Z lo deshace.", luna);
    }

    static string PrepararPostprocesado(UnityEngine.SceneManagement.Scene escena)
    {
        var perfil = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PERFIL);
        if (perfil == null)
        {
            if (!AssetDatabase.CopyAsset(PERFIL_ORIGEN, PERFIL))
                return "(No he podido copiar " + PERFIL_ORIGEN + ": postprocesado sin cambios.)";
            AssetDatabase.ImportAsset(PERFIL);
            perfil = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PERFIL);
            if (perfil == null) return "(Postprocesado sin cambios.)";
        }

        if (!perfil.TryGet(out ColorAdjustments color))
        {
            color = perfil.Add<ColorAdjustments>(false);
            color.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(color, perfil);
        }
        color.active = true;
        color.saturation.Override(SATURACION);

        if (perfil.TryGet(out Bloom bloom))
        {
            bloom.active = true;
            bloom.intensity.Override(BLOOM);       // que brillen bombillas, faroles y lentes del portón
            bloom.threshold.Override(0.9f);
        }
        if (perfil.TryGet(out Vignette vineta))
        {
            vineta.active = true;
            vineta.intensity.Override(VINETA);
        }
        EditorUtility.SetDirty(perfil);

        Volume global = null;
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (v.isGlobal && v.gameObject.scene == escena) { global = v; break; }
        if (global == null)
        {
            var go = new GameObject("Volume Noche");
            SceneManager_Mover(go, escena);
            Undo.RegisterCreatedObjectUndo(go, "Volume Noche");
            global = Undo.AddComponent<Volume>(go);
            global.isGlobal = true;
        }
        Undo.RecordObject(global, "Noche con luna");
        global.sharedProfile = perfil;
        return "Postprocesado: " + PERFIL + ".";
    }

    static void SceneManager_Mover(GameObject go, UnityEngine.SceneManagement.Scene escena)
    {
        if (go.scene != escena) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, escena);
    }

    // RenderSettings no es un Object público; Unity lo expone por un método interno.
    // Sin él todo funciona igual, pero Ctrl+Z no devolvería cielo, ambiente ni niebla.
    static Object ObjetoRenderSettings()
    {
        var m = typeof(RenderSettings).GetMethod("GetRenderSettings", BindingFlags.NonPublic | BindingFlags.Static);
        return m != null ? m.Invoke(null, null) as Object : null;
    }
}
