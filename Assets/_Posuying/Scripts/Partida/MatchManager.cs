using Unity.Netcode;
using UnityEngine;

// Lleva las vidas compartidas del equipo y decide cuando se acaba la partida.
// Es autoridad del servidor, como la vida de los personajes.
//
// Se pone en un objeto de la escena que tenga NetworkObject.
public class MatchManager : NetworkBehaviour
{
    public static MatchManager Instance { get; private set; }

    [Header("Vidas compartidas")]
    [Tooltip("Vidas del equipo cuando hay mas de un jugador")]
    public int coopLives = 5;
    [Tooltip("Vidas jugando solo (nadie puede levantarte, por eso son menos)")]
    public int soloLives = 3;

    private readonly NetworkVariable<int> netLives = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> netMatchOver = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int Lives => netLives.Value;
    public bool MatchOver => netMatchOver.Value;

    private bool _livesReady;
    private bool _startedAsCoop;

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (!IsServer) return;

        // Las vidas se fijan cuando ya hay alguien jugando: antes no sabemos
        // si la partida es en solitario o acompanada.
        if (NetworkPlayer.AllPlayers.Count == 0) return;

        bool isCoop = NetworkManager.ConnectedClientsIds.Count > 1;

        if (!_livesReady)
        {
            netLives.Value = isCoop ? coopLives : soloLives;
            _startedAsCoop = isCoop;
            _livesReady = true;
            return;
        }

        // Si empezaste solo y luego entra alguien, el equipo gana las vidas de
        // diferencia. No se reponen las ya gastadas ni se pasa del tope de coop,
        // asi que entrar y salir no sirve para farmear vidas.
        if (isCoop && !_startedAsCoop)
        {
            _startedAsCoop = true;

            int bonus = Mathf.Max(0, coopLives - soloLives);
            int before = netLives.Value;
            netLives.Value = Mathf.Min(coopLives, before + bonus);

            if (netLives.Value > before)
                AnnounceBonusClientRpc(netLives.Value - before, netLives.Value);
        }
    }

    [ClientRpc]
    private void AnnounceBonusClientRpc(int gained, int total)
    {
        Notifications.Show("Ha entrado un companero: +" + gained +
                           " vidas de equipo (ahora " + total + ")");
    }

    // Gasta una vida. Devuelve false si ya no quedaba ninguna.
    public bool TryConsumeLife()
    {
        if (!IsServer) return false;
        if (netLives.Value <= 0) return false;

        netLives.Value--;
        AnnounceLivesClientRpc(netLives.Value);
        return true;
    }

    [ClientRpc]
    private void AnnounceLivesClientRpc(int remaining)
    {
        Notifications.Show(remaining > 0
            ? "Quedan " + remaining + " vida(s) de equipo"
            : "Ultima vida gastada: no quedan mas");
    }

    // Se llama cada vez que alguien queda eliminado
    public void CheckDefeat()
    {
        if (!IsServer || netMatchOver.Value) return;

        var players = NetworkPlayer.AllPlayers;
        if (players.Count == 0) return;

        foreach (var player in players)
        {
            if (player == null) continue;

            var state = player.GetComponent<PlayerDownedState>();
            if (state == null) continue;

            // Si queda alguien en pie o abatido (todavia recuperable), seguimos
            if (!state.IsOut) return;
        }

        netMatchOver.Value = true;
        Notifications.Show("Habeis caido todos");
    }

    // ---------- Reiniciar ----------

    [ServerRpc(RequireOwnership = false)]
    public void RestartMatchServerRpc()
    {
        if (!IsServer) return;

        bool isCoop = NetworkManager.ConnectedClientsIds.Count > 1;

        netLives.Value = isCoop ? coopLives : soloLives;
        _startedAsCoop = isCoop;
        netMatchOver.Value = false;

        // Todos vuelven a estar en pie y en su punto de aparicion
        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null) continue;

            var state = player.GetComponent<PlayerDownedState>();
            if (state != null) state.ResetForNewMatch();
        }

        // Vaciar el mapa de enemigos: el director volvera a llenarlo
        foreach (var enemy in FindObjectsByType<EnemyBehaviour>(FindObjectsSortMode.None))
        {
            if (enemy != null && enemy.NetworkObject != null && enemy.NetworkObject.IsSpawned)
                Destroy(enemy.gameObject);
        }

        AnnounceRestartClientRpc();
    }

    [ClientRpc]
    private void AnnounceRestartClientRpc()
    {
        Notifications.Show("Partida reiniciada");
    }
}
