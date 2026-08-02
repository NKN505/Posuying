using Unity.Netcode;
using UnityEngine;

// Objeto recogible del mundo. Necesita un Collider con IsTrigger y un NetworkObject.
//
// El servidor decide quien lo coge y avisa a ese jugador para que lo meta en SU inventario
// (el inventario es local de cada jugador). El objeto no se destruye: se marca como
// recogido en una variable de red y se oculta en todas las maquinas.
public class ItemPickup : NetworkBehaviour
{
    public ItemData item;
    public int amount = 1;

    private readonly NetworkVariable<bool> taken = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        taken.OnValueChanged += OnTakenChanged;
        ApplyTaken(taken.Value);
    }

    public override void OnNetworkDespawn()
    {
        taken.OnValueChanged -= OnTakenChanged;
    }

    private void OnTakenChanged(bool previous, bool current) => ApplyTaken(current);

    // Para el guardado del mundo al migrar de host
    public bool IsTaken => taken.Value;
    public void SetTaken(bool value)
    {
        if (IsServer) taken.Value = value;
    }

    private void ApplyTaken(bool isTaken)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            r.enabled = !isTaken;

        foreach (var c in GetComponentsInChildren<Collider>(true))
            c.enabled = !isTaken;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer || taken.Value) return;
        if (!other.CompareTag("Player")) return;

        // El inventario vive en el servidor, asi que lo anadimos aqui directamente
        Inventory inv = other.GetComponent<Inventory>();
        if (inv == null) return;

        int leftover = inv.AddItem(item, amount);

        if (leftover > 0)
        {
            // No le cabia: el objeto se queda en el suelo
            amount = leftover;
            return;
        }

        taken.Value = true;
    }
}
