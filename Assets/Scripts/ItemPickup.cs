using UnityEngine;

// Objeto recogible del mundo. Al tocarlo el jugador, anade su ItemData al inventario.
// Requiere un Collider con IsTrigger activado.
public class ItemPickup : MonoBehaviour
{
    public ItemData item;
    public int amount = 1;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        Inventory inv = other.GetComponent<Inventory>();
        if (inv == null) return;

        int leftover = inv.AddItem(item, amount);

        if (leftover <= 0)
            Destroy(gameObject);          // se recogio todo
        else
            amount = leftover;            // inventario lleno: queda lo que sobro
    }
}
