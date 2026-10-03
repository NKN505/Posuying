using Unity.Netcode;
using UnityEngine;

// Hasta que tercio del mapa ha llegado el equipo.
//
// Funciona como un punto de control: en cuanto CUALQUIER jugador en pie entra
// en un tercio mas avanzado, ese tercio pasa a ser el del equipo, y quien gaste
// una vida reaparece alli (SpawnManager filtra los puntos por tercio). Nunca
// retrocede: volver atras no te quita el punto de control.
//
// Autoridad del servidor. Va en el mismo objeto que MatchManager (tiene NetworkObject).
public class ProgresoTercios : NetworkBehaviour
{
    public static ProgresoTercios Instance { get; private set; }

    [Tooltip("Cada cuantos segundos mira donde estan los jugadores")]
    public float intervalo = 0.5f;

    private readonly NetworkVariable<int> netTercio = new NetworkVariable<int>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // El tercio del equipo (1, 2 o 3). Lo leen todas las maquinas.
    public int TercioAlcanzado => netTercio.Value;

    private float _siguiente;

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (!IsServer || Time.time < _siguiente) return;
        _siguiente = Time.time + intervalo;

        int maximo = netTercio.Value;
        string quien = null;

        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null) continue;

            // Un abatido no cuenta: no queremos mover el punto de control a donde
            // alguien acaba de caer arrastrado por la horda.
            var estado = player.GetComponent<PlayerDownedState>();
            if (estado != null && !estado.CanAct) continue;

            int tercio = ZonaTercio.TercioEn(player.transform.position);
            if (tercio > maximo)
            {
                maximo = tercio;
                var nombre = player.GetComponent<PlayerName>();
                quien = nombre != null ? nombre.Name : "Un jugador";
            }
        }

        if (maximo > netTercio.Value)
        {
            netTercio.Value = maximo;
            AnunciarClientRpc(quien, maximo);
        }
    }

    [ClientRpc]
    private void AnunciarClientRpc(string quien, int tercio)
    {
        Notifications.Show(quien + " ha llegado al " + (tercio == 1 ? "1er" : tercio == 2 ? "2º" : "3er") +
                           " tercio: el equipo reaparecera aqui");
    }

    // Tras un cambio de anfitrion: se conserva el tercio al que habia llegado el equipo
    public void Restaurar(int tercio)
    {
        if (IsServer) netTercio.Value = Mathf.Clamp(tercio, 1, 3);
    }

    // Al reiniciar la partida se vuelve a empezar desde el principio
    public void Reiniciar()
    {
        if (IsServer) netTercio.Value = 1;
    }
}
