using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

// Conexion por internet con Unity Multiplayer Services (Relay + codigo de partida).
// El host crea una sesion y recibe un codigo; el companero entra con ese codigo,
// sin necesidad de IPs ni de abrir puertos en el router.
//
// OJO: la API de Sessions ya configura el transporte y arranca el host/cliente,
// por eso aqui NO se llama a StartHost() ni StartClient().
public class OnlineSession : MonoBehaviour
{
    [Tooltip("Jugadores maximos en la partida (incluido el host)")]
    public int maxPlayers = 4;

    public string JoinCode { get; private set; } = "";
    public string Status { get; private set; } = "";
    public bool Busy { get; private set; }

    private ISession _session;

    // Unity exige identificarse antes de usar Relay (basta con un login anonimo)
    private async Task<bool> EnsureSignedInAsync()
    {
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            return true;
        }
        catch (System.Exception e)
        {
            Status = "No se pudo conectar con Unity: " + e.Message;
            Debug.LogException(e);
            return false;
        }
    }

    public async void CreateOnlineGame()
    {
        if (Busy) return;
        Busy = true;
        JoinCode = "";
        Status = "Conectando con Unity...";

        if (await EnsureSignedInAsync())
        {
            try
            {
                Status = "Creando partida...";

                var options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork();
                _session = await MultiplayerService.Instance.CreateSessionAsync(options);

                JoinCode = _session.Code;
                Status = "Partida creada: pasale el codigo a tu companero";
            }
            catch (System.Exception e)
            {
                Status = "Error al crear la partida: " + e.Message;
                Debug.LogException(e);
            }
        }

        Busy = false;
    }

    public async void JoinOnlineGame(string code)
    {
        if (Busy) return;

        if (string.IsNullOrWhiteSpace(code))
        {
            Status = "Escribe el codigo de la partida";
            return;
        }

        Busy = true;
        Status = "Conectando con Unity...";

        if (await EnsureSignedInAsync())
        {
            try
            {
                Status = "Entrando en la partida...";

                _session = await MultiplayerService.Instance
                    .JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());

                JoinCode = _session.Code;
                Status = "Conectado";
            }
            catch (System.Exception e)
            {
                Status = "No se pudo entrar: " + e.Message;
                Debug.LogException(e);
            }
        }

        Busy = false;
    }

    public async void LeaveOnlineGame()
    {
        if (_session == null) return;

        try { await _session.LeaveAsync(); }
        catch (System.Exception e) { Debug.LogException(e); }

        _session = null;
        JoinCode = "";
        Status = "Desconectado";
    }

    public bool HasSession => _session != null;
}
