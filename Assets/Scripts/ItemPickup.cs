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

        NetworkObject playerObject = other.GetComponent<NetworkObject>();
        if (playerObject == null) return;

        // No hace falta mandar el ItemData por la red: este mismo objeto existe
        // en todas las maquinas, asi que cada una ya sabe que item es.
        GiveItemClientRpc(playerObject.OwnerClientId);
        taken.Value = true;
    }

    [ClientRpc]
    private void GiveItemClientRpc(ulong targetClientId)
    {
        // Solo lo guarda el jugador que lo ha recogido
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;

        Inventory inv = NetworkPlayer.LocalInventory;
        if (inv != null)
            inv.AddItem(item, amount);
    }
}
