using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Nombre visible de cada jugador, compartido por red.
// Lo escribe su dueno al aparecer y lo leen todos.
public class PlayerName : NetworkBehaviour
{
    private readonly NetworkVariable<FixedString32Bytes> netName =
        new NetworkVariable<FixedString32Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public string Name => netName.Value.IsEmpty ? "Jugador" : netName.Value.ToString();

    // Momento en que entramos NOSOTROS en la partida. Sirve para no anunciar
    // como "recien llegados" a los que ya estaban cuando nos conectamos.
    private static float _localJoinTime = -1f;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            netName.Value = new FixedString32Bytes(PlayerProfile.Name);
            _localJoinTime = Time.time;
        }

        netName.OnValueChanged += OnNameChanged;

        if (!netName.Value.IsEmpty)
            AnnounceJoin();
    }

    public override void OnNetworkDespawn()
    {
        netName.OnValueChanged -= OnNameChanged;

        if (!IsOwner)
            Notifications.Show(Name + " ha salido de la partida");
    }

    // El nombre suele llegar un instante despues del personaje
    private void OnNameChanged(FixedString32Bytes previous, FixedString32Bytes current)
    {
        if (previous.IsEmpty && !current.IsEmpty)
            AnnounceJoin();
    }

    private void AnnounceJoin()
    {
        if (IsOwner) return;

        // Los que ya estaban al conectarnos no son "recien llegados"
        if (_localJoinTime < 0f || Time.time - _localJoinTime < 2f) return;

        Notifications.Show(Name + " se ha unido a la partida");
    }
}
