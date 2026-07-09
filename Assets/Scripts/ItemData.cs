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

    [Header("Consumible (solo si type = Consumable)")]
    public float healAmount = 0f;
}
