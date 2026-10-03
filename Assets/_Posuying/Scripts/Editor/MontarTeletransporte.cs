using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Posuying > Partida > Montar teletransporte (telecomunicaciones > 3er tercio)
//
// Crea "Teletransporte_Telecom" en la escena abierta:
//   - Entrada: trigger con Teletransportador en el centro de la habitacion que
//     cierra la puerta de la llave ESCUDO (planta alta de telecomunicaciones), con
//     el mismo halo morado que la salida del final (pilar + luz), recortado a la
//     altura del techo.
//   - Destino: junto al porton de la tarjeta ROJA (3er tercio), en el lado desde
//     el que se abre con la tarjeta, mirando al porton.
//
// No toca nada mas. Ctrl+Z lo deshace. Guarda la escena (Ctrl+S) si esta bien.
// Si la entrada o el destino no quedan donde quieres, muevelos a mano: el
// teletransporte usa sus posiciones, no las recalcula.
public static class MontarTeletransporte
{
    const string NOMBRE = "Teletransporte_Telecom";
    const float DISTANCIA_DESTINO = 4f;   // metros por delante del porton
    const float RADIO_TRIGGER = 1f;

    [MenuItem("Posuying/Partida/Montar teletransporte (telecomunicaciones > 3er tercio)")]
    static void Montar()
    {
        var escena = EditorSceneManager.GetActiveScene();

        foreach (var t in Object.FindObjectsByType<Teletransportador>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.gameObject.scene == escena)
            {
                Debug.LogWarning("[Teletransporte] Ya hay uno montado (" + t.name + "). No hago nada.", t);
                Selection.activeObject = t.gameObject;
                return;
            }

        // 1. La puerta de la llave Escudo
        var puertas = new List<Door>();
        foreach (var d in Object.FindObjectsByType<Door>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (d.gameObject.scene == escena && d.cerrada && d.llave == KeyType.Escudo) puertas.Add(d);
        if (puertas.Count != 1)
        {
            Debug.LogError($"[Teletransporte] Esperaba 1 puerta cerrada con la llave Escudo y hay {puertas.Count}. No hago nada.");
            return;
        }
        var puerta = puertas[0];

        // 2. El porton de la tarjeta Roja
        PuertaTarjeta porton = null;
        foreach (var p in Object.FindObjectsByType<PuertaTarjeta>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (p.gameObject.scene == escena && p.tarjeta == ColorTarjeta.Roja) { porton = p; break; }
        if (porton == null) { Debug.LogError("[Teletransporte] No encuentro el porton de la tarjeta Roja. No hago nada."); return; }

        // 3. El halo de la salida final, para copiarlo
        DisparadorFinal salida = Object.FindFirstObjectByType<DisparadorFinal>(FindObjectsInactive.Include);

        Physics.SyncTransforms();   // en el editor los colliders pueden ir un paso por detras
        var info = new StringBuilder();
        Vector3 entrada = CentroHabitacion(puerta, info);
        porton.Geometria(out Vector3 centroPorton, out Vector3 ladoQueAbre);
        ladoQueAbre.y = 0f; ladoQueAbre.Normalize();
        Vector3 destino = AlSuelo(centroPorton + ladoQueAbre * DISTANCIA_DESTINO, 6f);

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Montar teletransporte");
        int grupo = Undo.GetCurrentGroup();

        var raiz = new GameObject(NOMBRE);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(raiz, escena);
        Undo.RegisterCreatedObjectUndo(raiz, NOMBRE);

        // Entrada
        var goEntrada = new GameObject("Entrada");
        goEntrada.transform.SetParent(raiz.transform, false);
        goEntrada.transform.position = entrada;
        goEntrada.layer = 2;   // Ignore Raycast, como la salida final
        var esfera = goEntrada.AddComponent<SphereCollider>();
        esfera.isTrigger = true;
        esfera.radius = RADIO_TRIGGER;
        esfera.center = new Vector3(0f, 1f, 0f);
        var tele = goEntrada.AddComponent<Teletransportador>();

        // Destino (mirando al porton)
        var goDestino = new GameObject("Destino");
        goDestino.transform.SetParent(raiz.transform, false);
        goDestino.transform.position = destino;
        goDestino.transform.rotation = Quaternion.LookRotation(-ladoQueAbre, Vector3.up);
        tele.destino = goDestino.transform;

        // Halo: copia de las marcas de la salida final, siempre visibles
        int copiadas = 0;
        float techo = AlturaLibre(entrada);
        if (salida != null && salida.marcas != null)
        {
            foreach (var marca in salida.marcas)
            {
                if (marca == null) continue;
                var copia = Object.Instantiate(marca, goEntrada.transform);
                copia.name = marca.name;
                Vector3 local = salida.transform.InverseTransformPoint(marca.transform.position);
                copia.transform.localPosition = local;
                copia.transform.localRotation = Quaternion.Inverse(salida.transform.rotation) * marca.transform.rotation;
                copia.transform.localScale = marca.transform.lossyScale;
                copia.SetActive(true);

                // El pilar de fuera mide 8 m: dentro de una habitacion se recorta al techo
                if (copia.GetComponent<MeshRenderer>() != null)
                {
                    float alto = Mathf.Min(copia.transform.localScale.y * 2f, techo - 0.05f);
                    var s = copia.transform.localScale; s.y = alto * 0.5f;
                    copia.transform.localScale = s;
                    copia.transform.localPosition = new Vector3(local.x, alto * 0.5f, local.z);
                }
                // La luz, a media altura de la habitacion
                var luz = copia.GetComponent<Light>();
                if (luz != null)
                    copia.transform.localPosition = new Vector3(local.x, Mathf.Min(local.y, techo * 0.6f), local.z);
                copiadas++;
            }
        }

        Undo.CollapseUndoOperations(grupo);
        EditorSceneManager.MarkSceneDirty(escena);
        Selection.activeObject = goEntrada;
        SceneView.lastActiveSceneView?.FrameSelected();

        Debug.Log($"[Teletransporte] Montado. Entrada en {entrada} (habitacion de la puerta '{puerta.name}', llave Escudo; " +
                  $"techo a {techo:F1} m). Destino en {destino}, a {DISTANCIA_DESTINO} m del porton '{porton.name}' por el lado de la tarjeta. " +
                  $"Halo: {copiadas} pieza(s) copiadas de la salida final. {info}" +
                  " Guarda la escena (Ctrl+S) si esta bien; Ctrl+Z lo deshace.", goEntrada);
    }

    // Centro aproximado de la habitacion que cierra la puerta: mira a los dos lados
    // del hueco y se queda con el mas cerrado (la habitacion), y dentro de el toma el
    // centro de los puntos donde chocan 16 rayos horizontales.
    static Vector3 CentroHabitacion(Door puerta, StringBuilder info)
    {
        Bounds b = new Bounds(puerta.transform.position, Vector3.zero);
        bool hay = false;
        Renderer mayor = null;
        foreach (var r in puerta.GetComponentsInChildren<Renderer>(true))
        {
            if (!hay) { b = r.bounds; hay = true; } else b.Encapsulate(r.bounds);
            if (mayor == null || r.bounds.size.sqrMagnitude > mayor.bounds.size.sqrMagnitude) mayor = r;
        }

        // Normal del hueco: el eje mas fino de la hoja, en horizontal
        Vector3 normal = puerta.transform.forward;
        if (mayor != null && mayor.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
        {
            Vector3 tam = mf.sharedMesh.bounds.size;
            Vector3[] ejes = { mayor.transform.right * tam.x, mayor.transform.up * tam.y, mayor.transform.forward * tam.z };
            float menor = float.MaxValue;
            foreach (var e in ejes)
            {
                Vector3 h = e; h.y = 0f;
                // solo ejes horizontales (el de la altura de la hoja no vale)
                if (Mathf.Abs(e.normalized.y) > 0.7f) continue;
                if (e.magnitude < menor) { menor = e.magnitude; normal = h.normalized; }
            }
        }
        normal.y = 0f; normal.Normalize();

        Vector3 centro = b.center;
        float suelo = b.min.y;
        float altura = suelo + 1.2f;

        Vector3 mejor = Vector3.zero;
        float mejorMedia = float.MaxValue;
        foreach (float lado in new[] { 1f, -1f })
        {
            Vector3 inicio = new Vector3(centro.x, altura, centro.z) + normal * lado * 1.2f;
            var puntos = new List<Vector3>();
            float suma = 0f;
            for (int i = 0; i < 16; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                float d = 25f;
                if (Physics.Raycast(inicio, dir, out var hit, 25f, ~0, QueryTriggerInteraction.Ignore)) d = hit.distance;
                suma += d;
                puntos.Add(inicio + dir * d);
            }
            float media = suma / 16f;
            Vector3 c = Vector3.zero;
            foreach (var p in puntos) c += p;
            c /= puntos.Count;
            info.Append($"Lado {(lado > 0 ? "A" : "B")}: distancia media a las paredes {media:F1} m. ");
            if (media < mejorMedia) { mejorMedia = media; mejor = new Vector3(c.x, altura, c.z); }
        }
        return AlSuelo(mejor, 1.0f);
    }

    // Baja el punto hasta el suelo (sin pasar a la planta de abajo)
    static Vector3 AlSuelo(Vector3 p, float subir)
    {
        if (Physics.Raycast(p + Vector3.up * subir, Vector3.down, out var hit, subir + 10f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point;
        return p;
    }

    static float AlturaLibre(Vector3 suelo)
    {
        if (Physics.Raycast(suelo + Vector3.up * 0.2f, Vector3.up, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore))
            return hit.distance + 0.2f;
        return 8f;
    }
}
