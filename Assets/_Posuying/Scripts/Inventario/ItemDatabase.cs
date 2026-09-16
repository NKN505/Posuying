using System.Collections.Generic;
using UnityEngine;

// Catalogo de todos los objetos del juego.
// Por la red no pueden viajar ScriptableObjects, solo datos simples, asi que
// mandamos un numero (la posicion en esta lista) y cada maquina lo traduce
// a su ItemData usando este mismo catalogo.
//
// Crear con: clic derecho en Project -> Create -> Inventario -> Catalogo de items
//
// IMPORTANTE: el orden de la lista ES el identificador. Si reordenas los
// elementos, las partidas guardadas o en curso veran objetos cambiados.
// Para retirar un objeto, deja su hueco vacio en vez de borrar la fila.
[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Inventario/Catalogo de items")]
public class ItemDatabase : ScriptableObject
{
    public List<ItemData> items = new List<ItemData>();

    public const int EmptyId = -1;

    public ItemData GetItem(int id)
    {
        if (id < 0 || id >= items.Count) return null;
        return items[id];
    }

    public int GetId(ItemData item)
    {
        if (item == null) return EmptyId;

        int index = items.IndexOf(item);
        if (index < 0)
            Debug.LogWarning("El item '" + item.itemName + "' no esta en el catalogo: " +
                             "anadelo o no se podra sincronizar por red.");
        return index;
    }
}
