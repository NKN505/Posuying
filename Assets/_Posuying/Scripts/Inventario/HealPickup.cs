using Unity.Netcode;
using UnityEngine;

// Botiquin del mundo. Necesita un Collider con IsTrigger y un NetworkObject.
//
// Como es un objeto colocado en la escena, NO lo destruimos (a Netcode no le gusta):
// marcamos "recogido" en una variable de red y lo ocultamos en todas las maquinas.
// Ventaja extra: mas adelante se podria hacer que reaparezca con solo poner taken = false.
public class HealPickup : NetworkBehaviour
{
    public float healAmount = 250f;

    private readonly NetworkVariable<bool> taken = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        taken.OnValueChanged += OnTakenChanged;
        ApplyTaken(taken.Value);   // un jugador que entre tarde lo vera ya recogido
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
        if (!IsServer || taken.Value) return;   // la decision es del servidor

        Character character = other.GetComponent<Character>();
        if (character == null || !character.GetIsPlayer()) return;

        character.Heal(healAmount);   // ya estamos en el servidor
        taken.Value = true;           // se oculta en todas las maquinas
    }
}
