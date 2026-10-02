using Unity.Netcode;
using UnityEngine;

// Los prefabs de lo que puede quedar tirado en el suelo (municion, botiquin y la
// mochila de un jugador caido), en un solo sitio de la escena.
//
// Asi los enemigos y los jugadores no tienen que llevar cada uno sus referencias:
// se cambia el aspecto de la municion aqui y vale para todos.
// Los tres prefabs tienen que estar en la lista de Network Prefabs.
public class CatalogoObjetosSoltados : MonoBehaviour
{
    public static CatalogoObjetosSoltados Instance { get; private set; }

    public ObjetoSoltado municion;
    public ObjetoSoltado botiquin;
    public ObjetoSoltado mochila;

    [Header("Inventario")]
    [Tooltip("El catalogo de objetos: lo que hay en el suelo necesita traducir el numero que " +
             "viaja por red al objeto (y a su modelo) en todas las maquinas.")]
    public ItemDatabase database;

    [Tooltip("El botiquin como objeto del inventario. Con esto, los botiquines del suelo " +
             "se guardan en el inventario en vez de curar al pisarlos. Vacio = como antes.")]
    public ItemData itemBotiquin;

    [Tooltip("Las cuatro llaves como objetos del inventario, en el orden de KeyType: " +
             "Casco, Espada, Escudo, Armadura. Vacio = las llaves no aparecen en el inventario.")]
    public ItemData[] itemsLlave;

    /// <summary>El objeto de inventario de una llave, o null si no esta configurado.</summary>
    public static ItemData ItemDeLlave(KeyType tipo)
    {
        var c = Instance;
        int i = (int)tipo;
        return c != null && c.itemsLlave != null && i >= 0 && i < c.itemsLlave.Length ? c.itemsLlave[i] : null;
    }

    void Awake()
    {
        Instance = this;
    }

    // Crea el objeto en red, apoyado en el suelo bajo 'posicion'. Solo servidor.
    public ObjetoSoltado Soltar(ObjetoSoltado prefab, Vector3 posicion)
    {
        var nm = NetworkManager.Singleton;
        if (prefab == null || nm == null || !nm.IsServer) return null;

        // Buscar el suelo: sin esto un enemigo que muere en el aire (el saltarin)
        // dejaria la municion flotando. Se descartan los personajes: el rayo
        // chocaba con la capsula del propio enemigo y la caja salia a 1,9 m.
        var golpes = Physics.RaycastAll(posicion + Vector3.up * 1f, Vector3.down, 20f,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float mejor = float.NegativeInfinity;
        foreach (var g in golpes)
        {
            if (g.collider.GetComponentInParent<Character>() != null) continue;
            if (g.point.y > mejor) { mejor = g.point.y; posicion = g.point; }
        }

        var objeto = Instantiate(prefab, posicion, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        objeto.NetworkObject.Spawn(true);
        return objeto;
    }
}
