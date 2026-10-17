using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

// Al compilar para una plataforma SIN Wwise (Android, navegador, Linux: ver
// Scripts/Audio/SinWwise.cs) quita de las escenas los componentes de Wwise
// (AkInitializer, AkGameObj, AkAmbient, AkRoom...).
//
// Solo afecta a la copia de la escena que entra en la build: las escenas del
// proyecto no se tocan. Sin esto, la build guardaria componentes cuyo codigo no
// existe en esa plataforma y al cargar saldrian errores de "script missing" y
// de "different serialization layout".
public class QuitarWwiseAlCompilar : IProcessSceneWithReport
{
    public int callbackOrder => 0;

    public static bool PlataformaSinWwise(BuildTarget t) =>
        t == BuildTarget.Android || t == BuildTarget.WebGL || t == BuildTarget.StandaloneLinux64;

    public void OnProcessScene(Scene escena, BuildReport informe)
    {
        if (informe == null || !PlataformaSinWwise(informe.summary.platform)) return;

        // Varias pasadas: un componente no se puede quitar mientras otro lo
        // necesite (AkAmbient necesita AkGameObj), asi que van cayendo por orden
        int quitados = 0;
        for (int pasada = 0; pasada < 4; pasada++)
        {
            int antes = quitados;
            foreach (var raiz in escena.GetRootGameObjects())
                foreach (var c in raiz.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (c == null || !EsDeWwise(c) || HayQuienLoNecesita(c)) continue;
                    Object.DestroyImmediate(c);
                    quitados++;
                }
            if (quitados == antes) break;
        }

        if (quitados > 0)
            Debug.Log("[Multiplataforma] " + escena.name + ": quitados " + quitados + " componentes de Wwise (" +
                      informe.summary.platform + " no tiene Wwise todavia).");
    }

    private static bool EsDeWwise(Component c)
    {
        string ensamblado = c.GetType().Assembly.GetName().Name;
        return ensamblado.StartsWith("AK.Wwise") || ensamblado.StartsWith("Ak.Wwise");
    }

    // Otro componente de Wwise del mismo objeto lo exige con [RequireComponent]
    private static bool HayQuienLoNecesita(Component c)
    {
        foreach (var otro in c.GetComponents<MonoBehaviour>())
        {
            if (otro == null || otro == c || !EsDeWwise(otro)) continue;
            foreach (RequireComponent r in otro.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                if ((r.m_Type0 != null && r.m_Type0.IsInstanceOfType(c)) ||
                    (r.m_Type1 != null && r.m_Type1.IsInstanceOfType(c)) ||
                    (r.m_Type2 != null && r.m_Type2.IsInstanceOfType(c)))
                    return true;
        }
        return false;
    }
}
