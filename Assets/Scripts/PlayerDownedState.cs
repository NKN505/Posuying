using Unity.Netcode;
using UnityEngine;

// Estado de "abatido" del jugador y reanimacion entre companeros.
//
// Flujo: al quedarse sin vida NO mueres, quedas ABATIDO con una cuenta atras.
//   - Un companero se acerca y mantiene E -> te levanta gratis
//   - Tu pulsas R -> gastas una vida del equipo y reapareces
//   - Se acaba la cuenta atras -> se gasta una vida automaticamente
//   - Si no quedan vidas -> quedas ELIMINADO hasta que se reinicie la partida
//
// Este mismo componente lleva los dos lados: el de estar abatido y el de
// levantar a otro. Va en el prefab del jugador.
public class PlayerDownedState : NetworkBehaviour
{
    [Header("Abatido")]
    [Tooltip("Segundos que aguantas abatido antes de gastar una vida")]
    public float bleedOutTime = 60f;
    [Tooltip("Vida con la que te levantas (no vuelves al maximo)")]
    public float healthAfterRevive = 300f;

    [Header("Reanimar")]
    public float reviveTime = 3f;
    public float reviveRange = 2.5f;
    public KeyCode reviveKey = KeyCode.E;
    public KeyCode giveUpKey = KeyCode.R;

    // Estado compartido: lo escribe el servidor
    private readonly NetworkVariable<bool> netDowned = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> netOut = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> netBleed = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> netReviveProgress = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsDowned => netDowned.Value;
    public bool IsOut => netOut.Value;
    public bool CanAct => !netDowned.Value && !netOut.Value;
    public float BleedRemaining => netBleed.Value;
    public float ReviveProgress => netReviveProgress.Value;

    // Quien me esta levantando ahora mismo (solo lo sabe el servidor)
    private ulong _reviverClientId;
    private bool _beingRevived;

    // A quien estoy levantando yo (solo en mi maquina)
    private PlayerDownedState _reviveTarget;

    void Update()
    {
        if (IsServer) ServerTick();
        if (IsOwner) OwnerInput();
    }

    // ---------- Servidor ----------

    public void GoDown()
    {
        if (!IsServer || netDowned.Value || netOut.Value) return;

        netDowned.Value = true;
        netBleed.Value = bleedOutTime;
        netReviveProgress.Value = 0f;
        _beingRevived = false;

        // Dejar la vida en 0 limpia: si no, sigue bajando a negativo con cada golpe
        var character = GetComponent<Character>();
        if (character != null) character.SetHealth(0f);
    }

    private void ServerTick()
    {
        if (!netDowned.Value) return;

        // La reanimacion solo avanza si quien me levanta sigue cerca y en pie
        if (_beingRevived && IsReviverValid())
        {
            netReviveProgress.Value += Time.deltaTime / Mathf.Max(0.1f, reviveTime);

            if (netReviveProgress.Value >= 1f)
            {
                Revive();
                return;
            }
        }
        else if (netReviveProgress.Value > 0f)
        {
            // Se ha alejado o soltado la tecla: la barra baja sola
            netReviveProgress.Value = Mathf.Max(0f, netReviveProgress.Value - Time.deltaTime);
            _beingRevived = false;
        }

        netBleed.Value -= Time.deltaTime;
        if (netBleed.Value <= 0f)
            BleedOut();
    }

    private bool IsReviverValid()
    {
        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null || player.OwnerClientId != _reviverClientId) continue;

            var state = player.GetComponent<PlayerDownedState>();
            if (state == null || !state.CanAct) return false;

            return Vector3.Distance(player.transform.position, transform.position) <= reviveRange;
        }

        return false;
    }

    private void Revive()
    {
        if (!IsServer) return;

        netDowned.Value = false;
        netReviveProgress.Value = 0f;
        netBleed.Value = 0f;
        _beingRevived = false;

        // La vida vive en Character, no en este componente
        var character = GetComponent<Character>();
        if (character != null) character.SetHealth(healthAfterRevive);

        AnnounceClientRpc(NameOfThisPlayer() + " ha sido levantado");
    }

    // Se acabo la cuenta atras: se gasta una vida automaticamente
    private void BleedOut()
    {
        if (!IsServer) return;

        if (MatchManager.Instance != null && MatchManager.Instance.TryConsumeLife())
        {
            RespawnFromLife();
            AnnounceClientRpc(NameOfThisPlayer() + " ha gastado una vida del equipo");
        }
        else
        {
            netDowned.Value = false;
            netOut.Value = true;
            netBleed.Value = 0f;

            // Esto si es una muerte de verdad: no quedan vidas y de aqui no se
            // vuelve hasta que se reinicie la partida. El cuerpo cae con ragdoll
            // y se queda en el sitio.
            SoltarRagdoll();

            AnnounceClientRpc(NameOfThisPlayer() + " esta fuera de combate");

            if (MatchManager.Instance != null)
                MatchManager.Instance.CheckDefeat();
        }
    }

    // El ragdoll lo lanza Character, que es quien habla con la red: elige el tipo
    // de muerte en el servidor y lo manda a todos para que vean la misma caida.
    private void SoltarRagdoll()
    {
        var character = GetComponent<Character>();
        if (character != null) character.MorirConRagdoll();
    }

    private void RespawnFromLife()
    {
        netDowned.Value = false;
        netOut.Value = false;
        netBleed.Value = 0f;
        netReviveProgress.Value = 0f;
        _beingRevived = false;

        var controller = GetComponent<PlayerController>();
        if (controller != null) controller.RespawnNow();
    }

    // Lo pide el jugador abatido al pulsar R
    [ServerRpc]
    public void GiveUpServerRpc()
    {
        if (!netDowned.Value) return;

        if (MatchManager.Instance != null && MatchManager.Instance.TryConsumeLife())
        {
            RespawnFromLife();
            AnnounceClientRpc(NameOfThisPlayer() + " ha gastado una vida del equipo");
        }
        else
        {
            AnnounceOwnerClientRpc("No quedan vidas de equipo");
        }
    }

    // Lo piden los companeros mientras mantienen la tecla
    [ServerRpc(RequireOwnership = false)]
    public void BeginReviveServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!netDowned.Value) return;

        _reviverClientId = rpcParams.Receive.SenderClientId;
        _beingRevived = true;
    }

    [ServerRpc(RequireOwnership = false)]
    public void CancelReviveServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != _reviverClientId) return;
        _beingRevived = false;
    }

    // Para reiniciar la partida
    public void ResetForNewMatch()
    {
        if (!IsServer) return;

        netDowned.Value = false;
        netOut.Value = false;
        netBleed.Value = 0f;
        netReviveProgress.Value = 0f;
        _beingRevived = false;

        // Si el jugador se quedo hecho un guinapo en el suelo, hay que deshacer
        // el ragdoll ANTES de reaparecerlo: si no, vuelve a la partida como un
        // monton de huesos sin Animator.
        var character = GetComponent<Character>();
        if (character != null) character.LevantarDeRagdoll();

        var controller = GetComponent<PlayerController>();
        if (controller != null) controller.RespawnNow();
    }

    // ---------- Entrada del jugador ----------

    private void OwnerInput()
    {
        if (UIState.BlocksGameplay) return;

        // Estando abatido: gastar una vida
        if (netDowned.Value)
        {
            if (Input.GetKeyDown(giveUpKey))
                GiveUpServerRpc();
            return;
        }

        if (netOut.Value) return;

        // En pie: levantar al companero mas cercano
        PlayerDownedState target = FindNearbyDowned();

        if (target != null && Input.GetKey(reviveKey))
        {
            if (_reviveTarget != target)
            {
                if (_reviveTarget != null) _reviveTarget.CancelReviveServerRpc();
                _reviveTarget = target;
                target.BeginReviveServerRpc();
            }
        }
        else if (_reviveTarget != null)
        {
            _reviveTarget.CancelReviveServerRpc();
            _reviveTarget = null;
        }
    }

    // El companero abatido mas cercano dentro del alcance
    public PlayerDownedState FindNearbyDowned()
    {
        PlayerDownedState best = null;
        float bestDistance = reviveRange;

        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null || player.gameObject == gameObject) continue;

            var state = player.GetComponent<PlayerDownedState>();
            if (state == null || !state.IsDowned) continue;

            float distance = Vector3.Distance(player.transform.position, transform.position);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = state;
            }
        }

        return best;
    }

    // ---------- Avisos ----------

    private string NameOfThisPlayer()
    {
        var component = GetComponent<PlayerName>();
        return component != null ? component.Name : "Un jugador";
    }

    [ClientRpc]
    private void AnnounceClientRpc(string message) => Notifications.Show(message);

    [ClientRpc]
    private void AnnounceOwnerClientRpc(string message)
    {
        if (IsOwner) Notifications.Show(message);
    }
}
