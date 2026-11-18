using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

// Copiar texto al portapapeles en todas las plataformas. En el navegador
// GUIUtility.systemCopyBuffer no llega al portapapeles real: se usa
// Plugins/WebGL/Portapapeles.jslib.
public static class Portapapeles
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void PosuyingCopiar(string texto);
#endif

    public static void Copiar(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return;
        GUIUtility.systemCopyBuffer = texto;
#if UNITY_WEBGL && !UNITY_EDITOR
        PosuyingCopiar(texto);
#endif
    }
}
