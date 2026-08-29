using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
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

    // True mientras se rehace la conexion tras un cambio de anfitrion
    public bool IsMigrating { get; private set; }

    private const float MigrationTimeout = 30f;
    private float _migrationStart;

    // Al perder la conexion no podemos concluir nada de inmediato: en una migracion
    // Netcode tambien te desconecta. Esperamos un poco a ver si vuelve.
    private const float ReconnectGrace = 15f;
    private float _disconnectedAt = -1f;
    private bool _leavingOnPurpose;
    private bool _wasHost;

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
        // MUY IMPORTANTE: si hay una sesion del SDK viva, NO se puede llamar a
        // NetworkManager.Shutdown(). El SDK lo detecta como un apagado "por fuera"
        // y aborta su propio arranque con "Failed to start the network manager",
        // dejando la red inservible hasta reiniciar. Se sale por la sesion.
        if (_session != null)
        {
            try { await _session.LeaveAsync(); }
            catch (System.Exception e) { Debug.LogWarning("Al salir de la sesion: " + e.Message); }

            UnhookSessionEvents();
            _session = null;
            return;
        }

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (!nm.IsListening && !nm.IsClient && !nm.IsServer) return;

        // Aqui no hay sesion: solo puede venir de una partida local (F1/F2),
        // que si se apaga con Shutdown porque el SDK no esta involucrado.
        Debug.Log("Cerrando una partida local previa.");
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

    // Las partidas locales (F1/F2) fijan el puerto 7777 en el transporte. Si en el
    // mismo equipo hay otra instancia usandolo, la siguiente no consigue arrancar
    // la red. Para las partidas online no necesitamos puerto fijo: que lo elija
    // el sistema (0) y asi nunca chocan dos instancias.
    private void FreeLocalPort()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        var transport = nm.GetComponent<UnityTransport>();
        if (transport == null) return;

        transport.SetConnectionData("0.0.0.0", 0);
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
        FreeLocalPort();

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
                _wasHost = true;   // haber sido host es lo que ensucia la red
                HookSessionEvents();

                JoinCode = _session.Code;
                Status = config.isPrivate
                    ? "Partida privada creada: pasa el codigo a tus companeros"
                    : "Partida creada y visible en la lista";
            }
            catch (System.Exception e)
            {
                HandleNetworkError(e, "Error al crear la partida: ");
            }
        }

        Busy = false;
    }

    // Si Netcode se ha quedado en mal estado, reintentar no sirve de nada:
    // hay que reconstruirlo entero (destruir el NetworkManager y recargar).
    private void HandleNetworkError(System.Exception e, string prefix)
    {
        Debug.LogException(e);

        if (NetworkReset.LooksLikeBrokenNetwork(e.Message))
        {
            Status = "La red se quedo en mal estado: reiniciandola...";
            NetworkReset.HardReset(e.Message);
            return;
        }

        Status = prefix + e.Message;
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
        FreeLocalPort();

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
                HookSessionEvents();

                JoinCode = _session.Code;
                Status = "Conectado";
            }
            catch (System.Exception e)
            {
                HandleNetworkError(e, "No se pudo entrar: ");
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
        FreeLocalPort();

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
                HookSessionEvents();

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
                HandleNetworkError(e, "No se pudo entrar: ");
            }
        }

        Busy = false;
    }

    public async void LeaveOnlineGame()
    {
        if (_session == null) return;

        _leavingOnPurpose = true;   // que no lo confunda con una caida
        UnhookSessionEvents();

        // Si sale el anfitrion, dejar la sesion es lo que dispara la migracion
        // en los demas jugadores.
        try { await _session.LeaveAsync(); }
        catch (System.Exception e) { Debug.LogException(e); }

        _session = null;
        JoinCode = "";
        IsMigrating = false;
        _disconnectedAt = -1f;
        Status = "Desconectado";

        // El SDK ya apaga Netcode al dejar la sesion. Hacerlo tambien nosotros
        // es justo lo que rompia la siguiente conexion.
        _wasHost = false;
        _leavingOnPurpose = false;
    }

    public bool HasSession => _session != null;

    // ---------- Avisos de la sesion ----------

    private void HookSessionEvents()
    {
        if (_session == null) return;

        _session.SessionHostChanged += OnHostChanged;
        _session.PlayerHasLeft += OnPlayerLeftSession;
        _session.SessionMigrated += OnSessionMigrated;
        _session.RemovedFromSession += OnRemovedFromSession;
        _session.Deleted += OnSessionDeleted;
    }

    private void UnhookSessionEvents()
    {
        if (_session == null) return;

        _session.SessionHostChanged -= OnHostChanged;
        _session.PlayerHasLeft -= OnPlayerLeftSession;
        _session.SessionMigrated -= OnSessionMigrated;
        _session.RemovedFromSession -= OnRemovedFromSession;
        _session.Deleted -= OnSessionDeleted;
    }

    // Nos han sacado de la sesion (por ejemplo al migrar el host).
    // Hay que soltar la sesion vieja o al intentar volver a entrar Unity dira
    // que ya somos miembros y nos rechazara.
    private void OnRemovedFromSession()
    {
        Debug.Log("Nos han sacado de la sesion: limpiando estado local.");
        ClearSessionState("Te han sacado de la partida");
    }

    private void OnSessionDeleted()
    {
        Debug.Log("La sesion ya no existe: limpiando estado local.");
        ClearSessionState("La partida se ha cerrado");
    }

    private async void ClearSessionState(string message)
    {
        UnhookSessionEvents();

        _session = null;
        JoinCode = "";
        IsMigrating = false;
        Busy = false;
        _disconnectedAt = -1f;
        Status = message;

        // Nos han sacado: el SDK ya ha desmontado la red por su cuenta.
        // No tocamos NetworkManager para no romper la siguiente conexion.
        _wasHost = false;
        await Task.Yield();
    }

    private void OnHostChanged(string hostId)
    {
        string who = ResolvePlayerName(hostId);
        Notifications.Show(who + " es ahora el anfitrion");
    }

    // Al cambiar de anfitrion la conexion se rehace: todos salen y vuelven a entrar.
    // No es un fallo, pero sin avisar parece que el juego se ha colgado.
    private void OnSessionMigrated()
    {
        IsMigrating = true;
        _migrationStart = Time.time;
        Status = "Cambiando de anfitrion...";
        Notifications.Show("Cambiando de anfitrion: reconectando...");
    }

    // ---------- Vigilancia de la conexion ----------

    void Start()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        nm.OnClientDisconnectCallback += OnNetcodeDisconnect;
        nm.OnTransportFailure += OnTransportFailure;
    }

    void OnDestroy()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        nm.OnClientDisconnectCallback -= OnNetcodeDisconnect;
        nm.OnTransportFailure -= OnTransportFailure;
    }

    private void OnNetcodeDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        // Si seguimos dentro es que se fue OTRO jugador: no es asunto nuestro
        if (nm.IsServer || nm.IsConnectedClient) return;

        BeginReconnectWatch();
    }

    private void OnTransportFailure() => BeginReconnectWatch();

    private void BeginReconnectWatch()
    {
        if (_leavingOnPurpose || _session == null) return;   // salida voluntaria: normal
        if (_disconnectedAt >= 0f) return;                   // ya estabamos vigilando

        _disconnectedAt = Time.time;
        Status = "Conexion perdida: intentando volver...";
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        bool backInGame = nm != null && (nm.IsConnectedClient || (nm.IsServer && nm.IsListening));

        // 1) Cambio de anfitrion en curso
        if (IsMigrating)
        {
            if (backInGame)
            {
                IsMigrating = false;
                _disconnectedAt = -1f;
                Status = "Reconectado";
                Notifications.Show("Reconectado a la partida");
            }
            else if (Time.time - _migrationStart > MigrationTimeout)
            {
                IsMigrating = false;
                ClearSessionState("No se pudo reconectar tras el cambio de anfitrion");
            }
            return;
        }

        // 2) Nos hemos quedado sin conexion sin que sea una migracion
        if (_disconnectedAt < 0f) return;

        if (backInGame)
        {
            _disconnectedAt = -1f;   // volvio sola
            Status = "Reconectado";
            return;
        }

        if (Time.time - _disconnectedAt > ReconnectGrace)
        {
            _disconnectedAt = -1f;
            Notifications.Show("Se ha perdido la conexion con la partida");
            ClearSessionState("Se ha perdido la conexion");
        }
    }

    private void OnPlayerLeftSession(string playerId)
    {
        // El aviso normal de salida lo da PlayerName al desaparecer su personaje.
        // Aqui solo cubrimos el caso de que se fuera sin llegar a aparecer.
        Debug.Log("Ha salido de la sesion el jugador " + playerId);
    }

    // El id de sesion es el mismo que guarda PersistentPlayerId, asi que
    // podemos traducirlo al nombre visible del jugador.
    private string ResolvePlayerName(string sessionPlayerId)
    {
        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null) continue;

            var id = player.GetComponent<PersistentPlayerId>();
            if (id != null && id.Id == sessionPlayerId)
            {
                var name = player.GetComponent<PlayerName>();
                return name != null ? name.Name : "Otro jugador";
            }
        }

        return "Otro jugador";
    }

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
