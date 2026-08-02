using Unity.Collections;
using Unity.Netcode;
using Unity.Services.Authentication;
using UnityEngine;

// Identificador estable de cada jugador, que sobrevive a un cambio de host.
//
// El id de cliente de Netcode (0, 1, 2...) se reparte de nuevo tras la migracion,
// asi que no sirve para saber "de quien era este inventario". Usamos el id de la
// cuenta de Unity, que es el mismo antes y despues.
public class PersistentPlayerId : NetworkBehaviour
{
    // Lo escribe su dueno al aparecer; el servidor lo lee para restaurar su estado
    private readonly NetworkVariable<FixedString64Bytes> netId =
        new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public string Id => netId.Value.ToString();

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
            netId.Value = new FixedString64Bytes(ResolveLocalId());

        if (IsServer)
        {
            netId.OnValueChanged += OnIdReceived;
            TryRestore();   // por si ya llego (el host conoce el suyo al instante)
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            netId.OnValueChanged -= OnIdReceived;
    }

    private void OnIdReceived(FixedString64Bytes previous, FixedString64Bytes current) => TryRestore();

    // Cuando ya sabemos quien es, le devolvemos lo que tuviera guardado
    private void TryRestore()
    {
        if (!IsServer || string.IsNullOrEmpty(Id)) return;

        if (WorldRestorer.Instance != null)
            WorldRestorer.Instance.RestorePlayer(Id, GetComponent<Character>(), GetComponent<Inventory>());
    }

    private string ResolveLocalId()
    {
        // Id de la cuenta de Unity; si no estamos identificados (partida local por IP)
        // usamos uno propio guardado en el equipo.
        if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
            return AuthenticationService.Instance.PlayerId;

        const string key = "local_player_id";
        if (!PlayerPrefs.HasKey(key))
        {
            PlayerPrefs.SetString(key, System.Guid.NewGuid().ToString("N").Substring(0, 16));
            PlayerPrefs.Save();
        }
        return PlayerPrefs.GetString(key);
    }
}
