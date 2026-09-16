using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Alinea los clips humanoides del jugador con el forward de la camara.
///
/// El problema: los clips de pistola de Mixamo plantan al personaje de costado.
/// La curva RootQ del clip guarda ese giro, asi que el cuerpo se dibuja rotado
/// respecto al eje de la camara, que cuelga de la raiz del Player.
///
/// La solucion: medir ese giro y compensarlo con el campo "Offset" de la
/// rotacion de raiz (AnimationClipSettings.orientationOffsetY).
///
/// El giro se mide como MEDIA CIRCULAR del yaw a lo largo de todo el clip, no
/// con el primer frame: asi el balanceo natural del cuerpo queda centrado en el
/// frente en vez de sesgado a un lado.
///
/// Menu:
///   NKN > Clips > 1. Analizar alineacion   -> solo mide y escribe en consola
///   NKN > Clips > 2. Aplicar alineacion    -> mide y guarda
/// </summary>
public static class NKNAlinearClips
{
    const string CARPETA = "Assets/NKN/Animations/Player";

    // Muestras a lo largo del clip para promediar el yaw.
    const int MUESTRAS = 120;

    // Por debajo de esto se considera ya alineado y no se toca.
    const float UMBRAL_GRADOS = 0.5f;

    // Unidades de AnimationClipSettings.orientationOffsetY.
    // Si al aplicar ves que el personaje gira una cantidad ridiculamente
    // pequena, pon esto a true y vuelve a aplicar.
    const bool OFFSET_EN_RADIANES = false;

    // Clips que NO se tocan todavia.
    static readonly string[] EXCLUIDOS =
    {
        "Pistol Kneeling",   // pendiente de incorporar al animator
    };

    [MenuItem("NKN/Clips/1. Analizar alineacion")]
    public static void Analizar() { Procesar(false); }

    [MenuItem("NKN/Clips/2. Aplicar alineacion")]
    public static void Aplicar() { Procesar(true); }

    static void Procesar(bool escribir)
    {
        if (!AssetDatabase.IsValidFolder(CARPETA))
        {
            Debug.LogError("[NKN] No existe la carpeta " + CARPETA);
            return;
        }

        var lineas = new List<string>();
        int tocados = 0, saltados = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { CARPETA }))
        {
            string ruta = AssetDatabase.GUIDToAssetPath(guid);

            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ruta))
            {
                var clip = o as AnimationClip;
                if (clip == null || clip.name.StartsWith("__")) continue;

                if (System.Array.IndexOf(EXCLUIDOS, clip.name) >= 0)
                {
                    lineas.Add(string.Format("{0,-24} EXCLUIDO", clip.name));
                    saltados++;
                    continue;
                }

                if (!clip.isHumanMotion)
                {
                    lineas.Add(string.Format("{0,-24} no es humanoide, se salta", clip.name));
                    saltados++;
                    continue;
                }

                float yaw;
                if (!MedirYaw(clip, out yaw))
                {
                    lineas.Add(string.Format("{0,-24} sin curvas RootQ, se salta", clip.name));
                    saltados++;
                    continue;
                }

                var ajustes = AnimationUtility.GetAnimationClipSettings(clip);
                float actual = ajustes.orientationOffsetY;

                if (Mathf.Abs(yaw) < UMBRAL_GRADOS)
                {
                    lineas.Add(string.Format("{0,-24} yaw {1,7:F2}   ya alineado", clip.name, yaw));
                    saltados++;
                    continue;
                }

                float offset = -yaw;
                if (OFFSET_EN_RADIANES) offset *= Mathf.Deg2Rad;

                lineas.Add(string.Format("{0,-24} yaw {1,7:F2}   offset {2,7:F2}  (antes {3:F2})",
                                         clip.name, yaw, offset, actual));

                if (escribir)
                {
                    ajustes.orientationOffsetY = offset;
                    AnimationUtility.SetAnimationClipSettings(clip, ajustes);
                    EditorUtility.SetDirty(clip);
                    tocados++;
                }
            }
        }

        if (escribir)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log(string.Format("[NKN] {0} de la alineacion de clips\n{1}\n\n{2} modificados, {3} sin tocar.",
                                escribir ? "APLICACION" : "ANALISIS (no se ha escrito nada)",
                                string.Join("\n", lineas.ToArray()),
                                tocados, saltados));
    }

    /// <summary>
    /// Media circular del yaw de la curva RootQ, en grados.
    /// Se promedia en el circulo (sumando senos y cosenos) y no aritmeticamente,
    /// para que no falle si el angulo cruza +-180.
    /// </summary>
    static bool MedirYaw(AnimationClip clip, out float grados)
    {
        grados = 0f;

        AnimationCurve cx = null, cy = null, cz = null, cw = null;

        foreach (var b in AnimationUtility.GetCurveBindings(clip))
        {
            switch (b.propertyName)
            {
                case "RootQ.x": cx = AnimationUtility.GetEditorCurve(clip, b); break;
                case "RootQ.y": cy = AnimationUtility.GetEditorCurve(clip, b); break;
                case "RootQ.z": cz = AnimationUtility.GetEditorCurve(clip, b); break;
                case "RootQ.w": cw = AnimationUtility.GetEditorCurve(clip, b); break;
            }
        }

        if (cx == null || cy == null || cz == null || cw == null) return false;

        float duracion = clip.length;
        if (duracion <= 0f) return false;

        float sumaSen = 0f, sumaCos = 0f;

        for (int i = 0; i < MUESTRAS; i++)
        {
            float t = duracion * i / (MUESTRAS - 1);

            var q = new Quaternion(cx.Evaluate(t), cy.Evaluate(t), cz.Evaluate(t), cw.Evaluate(t));
            if (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) continue;
            q.Normalize();

            // Yaw alrededor de Y, en radianes.
            float yaw = Mathf.Atan2(2f * (q.w * q.y + q.x * q.z),
                                    1f - 2f * (q.y * q.y + q.x * q.x));

            sumaSen += Mathf.Sin(yaw);
            sumaCos += Mathf.Cos(yaw);
        }

        grados = Mathf.Atan2(sumaSen, sumaCos) * Mathf.Rad2Deg;
        return true;
    }
}
