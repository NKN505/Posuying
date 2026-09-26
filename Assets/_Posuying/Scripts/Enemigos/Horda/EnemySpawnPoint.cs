using System.Collections.Generic;
using UnityEngine;

// Punto donde el HordeDirector puede hacer aparecer enemigos.
//
// El director no usa cualquier punto: elige uno que este a buena distancia del
// jugador, que NINGUN jugador este mirando y desde el que haya ruta por el NavMesh
// hasta el. Asi un enemigo nunca aparece delante de nadie ni encerrado al otro
// lado de un muro o de un porton cerrado.
//
// Se pueden mover, borrar o duplicar a mano en la escena: el director los
// encuentra solos (registro estatico), no hay que enlazarlos en ningun sitio.
public class EnemySpawnPoint : MonoBehaviour
{
    // Todos los puntos activos de la escena (se evita buscarlos cada vez)
    public static readonly List<EnemySpawnPoint> All = new List<EnemySpawnPoint>();

    [Tooltip("Los enemigos aparecen repartidos dentro de este radio alrededor del punto")]
    public float radio = 1.5f;

    // Momento del ultimo enemigo que salio de aqui (lo gestiona el HordeDirector)
    [System.NonSerialized] public float ultimoUso = -999f;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.1f, radio);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
    }
}
