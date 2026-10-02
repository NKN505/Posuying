using UnityEngine;

public enum ItemType { Consumable, Weapon, Generic }

// Definicion de un objeto del inventario. Se crea desde el editor:
// clic derecho en Project -> Create -> Inventario -> Item
[CreateAssetMenu(fileName = "NuevoItem", menuName = "Inventario/Item")]
public class ItemData : ScriptableObject
{
    public string itemName = "Item";
    public Sprite icon;
    public ItemType type = ItemType.Generic;

    [Tooltip("Cuantas unidades caben en un mismo hueco")]
    public int maxStack = 1;

    [Header("En el suelo")]
    [Tooltip("Modelo que se ve cuando un jugador suelta este objeto. Vacio = se ve como una mochila.")]
    public GameObject modeloSuelo;
    [Tooltip("Tamano en metros de su lado mas largo cuando esta en el suelo")]
    public float tamanoSuelo = 0.5f;
    [Tooltip("Giro del modelo en el suelo (grados), para que quede de pie o tumbado como convenga")]
    public Vector3 rotacionSuelo = Vector3.zero;
    [Tooltip("Opcional: material con el que se pinta en el suelo, si los del modelo no se ven bien")]
    public Material materialSuelo;

    [Header("Consumible (solo si type = Consumable)")]
    public float healAmount = 0f;
}
