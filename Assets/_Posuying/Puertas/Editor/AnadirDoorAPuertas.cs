using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Posuying > Puertas > Añadir Door a todas las puertas
// Busca en la escena abierta cada puerta (el objeto "Puerta..." que contiene
// alguna "Hoja") y le añade Door y NetworkObject si no los tiene.
// No toca los prefabs ni guarda la escena. Se deshace entero con Ctrl+Z.
public static class AnadirDoorAPuertas
{
    [MenuItem("Posuying/Puertas/Añadir Door a todas las puertas")]
    static void Ejecutar()
    {
        var puertas = new List<GameObject>();
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var escena = SceneManager.GetSceneAt(s);
            if (!escena.isLoaded) continue;
            foreach (var raiz in escena.GetRootGameObjects())
                foreach (var t in raiz.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.Contains("Hoja")) continue;
                    var puerta = RaizPuerta(t);
                    if (puerta != null && !puertas.Contains(puerta.gameObject)) puertas.Add(puerta.gameObject);
                }
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Añadir Door a todas las puertas");
        int grupo = Undo.GetCurrentGroup();

        int conDoor = 0, conNet = 0;
        var log = new StringBuilder();
        foreach (var go in puertas)
        {
            string cambios = "";
            if (go.GetComponentInParent<NetworkObject>(true) == null)
            {
                Undo.AddComponent<NetworkObject>(go);
                conNet++; cambios += " +NetworkObject";
            }
            if (go.GetComponent<Door>() == null)
            {
                Undo.AddComponent<Door>(go);
                conDoor++; cambios += " +Door";
            }
            log.AppendLine(Ruta(go.transform) + (cambios == "" ? "  (ya lo tenía)" : cambios));
        }
        Undo.CollapseUndoOperations(grupo);

        Debug.Log($"[Puertas] {puertas.Count} puertas encontradas. Door añadido a {conDoor}, NetworkObject a {conNet}. " +
                  "Guarda la escena (Ctrl+S) si está bien; Ctrl+Z lo deshace todo.\n" + log);
    }

    // El antepasado más cercano cuyo nombre empieza por "Puerta" (sin contar "Puertas...", que son grupos)
    static Transform RaizPuerta(Transform hoja)
    {
        for (var p = hoja.parent; p != null; p = p.parent)
            if (p.name.StartsWith("Puerta") && !p.name.StartsWith("Puertas")) return p;
        return null;
    }

    static string Ruta(Transform t)
    {
        string r = t.name;
        while (t.parent != null) { t = t.parent; r = t.name + "/" + r; }
        return r;
    }
}
