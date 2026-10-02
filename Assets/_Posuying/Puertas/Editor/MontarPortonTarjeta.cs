using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Posuying > Puertas > Montar portón con tarjeta (muro / ROJA 3er tercio)
// Crea el objeto del portón con PuertaTarjeta usando las dos hojas como piezas
// móviles, y coloca su tarjeta delante, en el lado desde el que se abre.
// No toca prefabs ni guarda la escena. Ctrl+Z lo deshace todo.
public static class MontarPortonTarjeta
{
    [MenuItem("Posuying/Puertas/Montar portón con tarjeta (muro)")]
    static void MontarAzul() => Montar("P_dr.001", "P_piz.001", ColorTarjeta.Azul, "Porton_Tarjeta",
        "Tarjeta_Acceso", "Assets/_Posuying/Puertas/Tarjeta/Tarjeta_Acceso.fbx");

    [MenuItem("Posuying/Puertas/Montar portón con tarjeta ROJA (3er tercio)")]
    static void MontarRoja() => Montar("P_dr", "P_piz", ColorTarjeta.Roja, "Porton_Tarjeta_Roja",
        "Tarjeta_Acceso_Roja", "Assets/_Posuying/Puertas/Tarjeta/Tarjeta_Acceso_Roja.fbx");

    static void Montar(string nDr, string nIz, ColorTarjeta color, string nombrePorton, string nombreTarjeta, string FBX)
    {
        var dr = Buscar(nDr);
        var iz = Buscar(nIz);
        if (dr == null || iz == null) return;

        foreach (var pt in Object.FindObjectsByType<PuertaTarjeta>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (pt.hojas != null && System.Array.IndexOf(pt.hojas, dr) >= 0)
            {
                Debug.LogWarning("[Portón] Ya está montado en " + pt.name + ". No hago nada.", pt);
                Selection.activeObject = pt.gameObject;
                return;
            }

        foreach (var h in new[] { dr, iz })
            if ((GameObjectUtility.GetStaticEditorFlags(h.gameObject) & StaticEditorFlags.BatchingStatic) != 0)
                Debug.LogWarning("[Portón] " + h.name + " está marcado como Static (Batching): en el juego no se verá moverse.", h);

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Montar portón con tarjeta");
        int grupo = Undo.GetCurrentGroup();

        // Portón
        var go = new GameObject(nombrePorton);
        SceneManager.MoveGameObjectToScene(go, dr.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(go, nombrePorton);
        var puerta = Undo.AddComponent<PuertaTarjeta>(go);   // el NetworkObject lo trae RequireComponent
        var so = new SerializedObject(puerta);
        var arr = so.FindProperty("hojas");
        arr.arraySize = 2;
        arr.GetArrayElementAtIndex(0).objectReferenceValue = dr;
        arr.GetArrayElementAtIndex(1).objectReferenceValue = iz;
        so.FindProperty("tarjeta").enumValueIndex = (int)color;
        so.ApplyModifiedPropertiesWithoutUndo();
        puerta.Geometria(out Vector3 centro, out Vector3 lado);
        go.transform.position = centro;

        // Tarjeta, 3 m por delante en el lado que abre, flotando a 0,9 m del suelo
        var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(FBX);
        if (modelo != null)
        {
            var t = (GameObject)PrefabUtility.InstantiatePrefab(modelo, dr.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(t, nombreTarjeta);
            t.name = nombreTarjeta;
            Vector3 p = centro + lado * 3f;
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                p = hit.point + Vector3.up * 0.9f;
            t.transform.position = p;
            t.transform.localScale = Vector3.one * 4f;
            var tj = Undo.AddComponent<Tarjeta>(t);
            tj.color = color;
            EditorUtility.SetDirty(tj);
        }
        else Debug.LogWarning("[Portón] No encuentro " + FBX + "; coloca la tarjeta a mano.");

        Undo.CollapseUndoOperations(grupo);
        Selection.activeObject = go;
        Debug.Log($"[Portón] Montado: {nombrePorton} (hojas {nDr} y {nIz}, tarjeta {color}) y una {nombreTarjeta} en el lado que abre. " +
                  "Guarda la escena (Ctrl+S) si está bien; Ctrl+Z lo deshace.");
    }

    static Transform Buscar(string nombre)
    {
        var encontrados = new List<Transform>();
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var esc = SceneManager.GetSceneAt(s);
            if (!esc.isLoaded) continue;
            foreach (var r in esc.GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                    if (t.name == nombre) encontrados.Add(t);
        }
        if (encontrados.Count != 1)
        {
            Debug.LogError($"[Portón] Hay {encontrados.Count} objetos llamados {nombre}; esperaba 1. No hago nada.");
            return null;
        }
        return encontrados[0];
    }
}
