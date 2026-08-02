using System.Threading.Tasks;
using Unity.Netcode;
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
    // Todas las partidas del juego comparten este tipo. Es lo que permite
    // encontrarlas al listar: por defecto el SDK pone un GUID aleatorio y
    // entonces ninguna partida veria a las demas.
    public const string SessionType = "posuying";

    [Tooltip("Jugadores maximos por defecto (se puede cambiar al crear la partida)")]
    public int maxPlayers = 4;

    // Ajustes elegidos al crear una partida
    public class GameConfig
    {
        public string name = "Partida";
        public int maxPlayers = 4;
        public bool isPrivate = false;
        public string password = "";
    }

    // Partidas encontradas al buscar
    public System.Collections.Generic.IList<ISessionInfo> AvailableSessions { get; private set; }
        = new System.Collections.Generic.List<ISessionInfo>();

    public bool IsHost => _session != null && _session.IsHost;

    [Header("Perfil de identidad")]
    [Tooltip("Dejar vacio para que se elija solo. Dos instancias con perfiles distintos " +
             "cuentan como jugadores distintos, y asi puedes probar en un mismo PC.")]
    public string profileName = "";

    public string CurrentProfile { get; private set; } = "";

    public string JoinCode { get; private set; } = "";
    public string Status { get; private set; } = "";
    public bool Busy { get; private set; }

    private ISession _session;

    // Netcode no puede arrancar si ya estaba arrancado (por ejemplo si antes se
    // pulso "Cliente local" y se quedo intentando conectar). Lo apagamos antes.
    private async Task EnsureNetworkStoppedAsync()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (!nm.IsListening && !nm.IsClient && !nm.IsServer) return;

        Debug.Log("Habia una conexion previa abierta: se cierra antes de continuar.");
        nm.Shutdown();

        // Netcode necesita frames completos para soltarlo todo, no basta con ceder el turno
        await Task.Delay(300);

        int guard = 0;
        while ((nm.ShutdownInProgress || nm.IsListening || nm.IsClient || nm.IsServer) && guard < 40)
        {
            guard++;
            await Task.Delay(50);
        }

        if (nm.IsListening || nm.IsClient || nm.IsServer)
            Debug.LogWarning("Netcode sigue activo tras el apagado; la conexion puede fallar.");
    }

    // Unity exige identificarse antes de usar Relay (basta con un login anonimo)
    private async Task<bool> EnsureSignedInAsync()
    {
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                // El perfil define QUIEN eres para Unity. Sin esto, el editor y la
                // build del mismo PC comparten identidad y no puedes unirte a tu
                // propia partida ("ya estas conectado").
                string profile = ResolveProfileName();
                if (!string.IsNullOrEmpty(profile))
                    AuthenticationService.Instance.SwitchProfile(profile);

                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                CurrentProfile = AuthenticationService.Instance.Profile;
            }

            return true;
        }
        catch (System.Exception e)
        {
            Status = "No se pudo conectar con Unity: " + e.Message;
            Debug.LogException(e);
            return false;
        }
    }

    public void CreateOnlineGame() => CreateOnlineGame(new GameConfig { maxPlayers = maxPlayers });

    public async void CreateOnlineGame(GameConfig config)
    {
        if (Busy) return;
        Busy = true;
        JoinCode = "";
        Status = "Conectando con Unity...";

        await EnsureNetworkStoppedAsync();

        if (await EnsureSignedInAsync())
        {
            try
            {
                Status = "Creando partida...";

                var options = new SessionOptions
                {
                    Name = string.IsNullOrWhiteSpace(config.name) ? "Partida" : config.name.Trim(),
                    MaxPlayers = Mathf.Max(2, config.maxPlayers),
                    IsPrivate = config.isPrivate,
                    Type = SessionType
                };

                if (!string.IsNullOrWhiteSpace(config.password))
                    options.Password = config.password.Trim();

                options = options
                    .WithRelayNetwork()
                    .WithHostMigration(new WorldMigrationHandler());

                _session = await MultiplayerService.Instance.CreateSessionAsync(options);

                JoinCode = _session.Code;
                Status = config.isPrivate
                    ? "Partida privada creada: pasa el codigo a tus companeros"
                    : "Partida creada y visible en la lista";
            }
            catch (System.Exception e)
            {
                Status = "Error al crear la partida: " + e.Message;
                Debug.LogException(e);
            }
        }

        Busy = false;
    }

    // ---------- Buscar partidas ----------

    public async void RefreshSessionList()
    {
        if (Busy) return;
        Busy = true;
        Status = "Buscando partidas...";

        if (await EnsureSignedInAsync())
        {
            try
            {
                var query = new QuerySessionsOptions { Count = 25 };
                QuerySessionsResults results = await MultiplayerService.Instance
                    .QuerySessionsAsync(query);

                AvailableSessions = results.Sessions;
                Status = AvailableSessions.Count == 0
                    ? "No hay partidas abiertas ahora mismo"
                    : AvailableSessions.Count + " partida(s) encontradas";
            }
            catch (System.Exception e)
            {
                Status = "No se pudo buscar: " + e.Message;
                Debug.LogException(e);
            }
        }

        Busy = false;
    }

    public async void JoinSessionById(string sessionId, string password = "")
    {
        if (Busy || string.IsNullOrEmpty(sessionId)) return;

        Busy = true;
        Status = "Entrando en la partida...";

        await EnsureNetworkStoppedAsync();

        if (await EnsureSignedInAsync())
        {
            try
            {
                var joinOptions = new JoinSessionOptions()
                    .WithHostMigration(new WorldMigrationHandler());

                if (!string.IsNullOrWhiteSpace(password))
                    joinOptions.Password = password.Trim();

                _session = await MultiplayerService.Instance
                    .JoinSessionByIdAsync(sessionId, joinOptions);

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

    // ---------- Abrir / cerrar la partida (hotjoin) ----------

    public bool IsGameLocked => _session != null && _session.IsLocked;

    public void SetGameLocked(bool locked)
    {
        if (_session is IHostSession host)
        {
            host.IsLocked = locked;
            Status = locked
                ? "Partida cerrada: no puede entrar nadie mas"
                : "Partida abierta: se puede entrar sobre la marcha";
        }
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

        await EnsureNetworkStoppedAsync();

        if (await EnsureSignedInAsync())
        {
            string cleanCode = code.Trim().ToUpperInvariant();

            try
            {
                Status = "Entrando en la partida " + cleanCode + "...";

                // Tambien aqui: cualquiera de los dos puede acabar siendo el host
                var joinOptions = new JoinSessionOptions()
                    .WithHostMigration(new WorldMigrationHandler());

                _session = await MultiplayerService.Instance
                    .JoinSessionByCodeAsync(cleanCode, joinOptions);

                JoinCode = _session.Code;
                Status = "Conectado";
            }
            catch (SessionException e) when (e.Message.Contains("not found"))
            {
                // El caso mas habitual con diferencia
                Status = "No existe la partida '" + cleanCode + "'.\n" +
                         "Comprueba el codigo y que el host siga dentro:\n" +
                         "la partida se cierra en cuanto el host sale.";
                Debug.LogWarning("Lobby no encontrado con el codigo: " + cleanCode);
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

    // Elige el perfil por orden de prioridad:
    //   1) argumento de arranque  -profile <nombre>   (para lanzar 2 builds a la vez)
    //   2) el campo del Inspector
    //   3) automatico: el editor y la build usan perfiles distintos
    private string ResolveProfileName()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-profile")
                return Sanitize(args[i + 1]);
        }

        if (!string.IsNullOrWhiteSpace(profileName))
            return Sanitize(profileName);

        return Application.isEditor ? "editor" : "build";
    }

    // Unity solo admite letras, numeros, guion y guion bajo (maximo 30 caracteres)
    private string Sanitize(string raw)
    {
        var clean = new System.Text.StringBuilder();

        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                clean.Append(c);

            if (clean.Length >= 30) break;
        }

        return clean.ToString();
    }
}
