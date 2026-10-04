using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

// Pantalla inicial del juego: crear partida, buscar partidas abiertas,
// entrar por codigo y opciones. Se construye por codigo sobre el Canvas.
//
// Fuera de partida es el menu de inicio. Dentro, el mismo panel hace de menu de
// pausa (tecla Escape, que gestiona NetworkUI) y de opciones, para que todo
// tenga el mismo aspecto.
public class MainMenuUI : MonoBehaviour
{
    private enum Tab { Crear, Buscar, Codigo, Opciones, Pausa }

    // Lo que se esta mostrando: decide que pestanas y que fondo se ven
    private enum Vista { Inicio, Pausa, OpcionesEnPartida, Sala, Oculto }

    [Header("Referencias")]
    public OnlineSession onlineSession;
    public Canvas canvas;

    [Header("Fondo")]
    [Tooltip("Imagen de fondo del menu. Debe importarse como Sprite (2D and UI).")]
    public Sprite background;
    [Tooltip("Oscurecido sobre el fondo, para que se lea el texto")]
    public Color backgroundTint = new Color(0f, 0f, 0f, 0.35f);
    [Tooltip("Oscurecido sobre el juego con el menu de pausa abierto")]
    public Color pauseTint = new Color(0f, 0f, 0f, 0.6f);

    [Header("Estilo")]
    public Vector2 panelSize = new Vector2(620f, 460f);
    [Tooltip("Desplazamiento del panel. Negativo lo baja, para no tapar el titulo del arte.")]
    public Vector2 panelOffset = new Vector2(0f, -150f);
    public Color panelColor = new Color(0.05f, 0.06f, 0.09f, 0.88f);
    public Color accentColor = new Color(0.55f, 0.12f, 0.12f, 1f);
    public Color buttonColor = new Color(1f, 1f, 1f, 0.14f);

    [Header("Sonido (Wwise)")]
    [Tooltip("Play_Menu_Music")]
    public AK.Wwise.Event menuMusicPlay;
    [Tooltip("Stop_Menu_Music")]
    public AK.Wwise.Event menuMusicStop;
    [Tooltip("Play_Ambience_City: suena mientras estas en partida")]
    public AK.Wwise.Event ambiencePlay;
    [Tooltip("Milisegundos de fundido al cortar el ambiente al volver al menu")]
    public int ambienceFadeOutMs = 1500;
    [Tooltip("Play_UI_Menu_Hover")]
    public AK.Wwise.Event uiHover;
    [Tooltip("Play_UI_Menu_Click")]
    public AK.Wwise.Event uiClick;
    [Tooltip("Play_UI_Menu_Start: al crear o unirse a una partida")]
    public AK.Wwise.Event uiStart;

    // Un emisor por tipo de sonido: asi el volumen de Musica de Opciones solo
    // afecta a la musica, y parar el ambiente no corta nada mas.
    private GameObject _musicEmitter;
    private GameObject _ambienceEmitter;
    private GameObject _uiEmitter;
    private bool? _wasInGame = null;      // null = aun no se ha decidido nada
    private float _appliedMaster = -1f;
    private float _appliedMusic = -1f;
    private MenuCamera _menuCamera;

    // Para que el menu de Escape pueda abrir las opciones estando en partida
    public static MainMenuUI Instance { get; private set; }
    public bool OptionsOverlayOpen { get; private set; }

    private readonly List<GameObject> _tabButtons = new List<GameObject>();

    private Vista _vista = Vista.Inicio;
    private GameObject _fondo;
    private Image _velo;
    private GameObject _quitButton;
    private Text _pauseInfo;
    private Text _pauseLock;
    private Text _pauseLockButton;

    private RectTransform _root;
    private RectTransform _content;
    private Text _status;
    private Font _font;
    private Tab _tab = Tab.Crear;
    private bool _built;

    // Formulario de creacion
    private InputField _nameInput;
    private InputField _passwordInput;
    private int _maxPlayers = 4;
    private bool _isPrivate = false;

    // Union
    private InputField _codeInput;
    private InputField _joinPasswordInput;
    private string _selectedSessionId = "";

    // Opciones
    private enum OptionsTab { General, Graficos, Audio, Controles }
    private OptionsTab _optionsTab = OptionsTab.General;

    private int _resIndex = -1;
    private bool _fullscreen = true;

    void Update()
    {
        UpdateVolumes();
        UpdateKeyCapture();

        if (!_built)
        {
            TryBuild();
            return;
        }

        // Durante la migracion tampoco mostramos el menu: estamos "en partida",
        // solo que rehaciendo la conexion
        bool migrating = onlineSession != null && onlineSession.IsMigrating;

        bool inGame = migrating ||
                      (NetworkManager.Singleton != null &&
                       (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer));

        // Fuera de partida, el menu de inicio. Dentro, la pausa (Escape) o las
        // opciones abiertas desde ella; el resto del tiempo, oculto.
        if (!inGame) OptionsOverlayOpen = false;   // no arrastrarla a la siguiente partida

        Vista vista = !inGame ? Vista.Inicio
                    : migrating ? Vista.Oculto
                    : OptionsOverlayOpen ? Vista.OpcionesEnPartida
                    : NetworkUI.MenuOpen ? Vista.Pausa
                    : (SalaEspera.Instance != null && SalaEspera.Instance.Visible) ? Vista.Sala
                    : Vista.Oculto;

        UpdateMenuAudio(inGame);

        if (vista != _vista)
        {
            _vista = vista;
            _root.gameObject.SetActive(vista != Vista.Oculto);

            // En partida no se pone el arte del menu: se deja ver el juego, oscurecido
            if (_fondo != null) _fondo.SetActive(vista == Vista.Inicio);
            // En la sala no hay velo: se ve a los jugadores tal cual
            if (_velo != null)
                _velo.color = vista == Vista.Sala ? Color.clear
                            : vista != Vista.Inicio ? pauseTint
                            : (background != null ? backgroundTint : Color.clear);
            if (_panel != null) _panel.gameObject.SetActive(vista != Vista.Sala);
            if (_sala != null) _sala.gameObject.SetActive(vista == Vista.Sala);
            // SALIR cierra el juego entero: en partida se sale desde la pausa
            if (_quitButton != null) _quitButton.SetActive(vista == Vista.Inicio);
            if (_status != null && vista != Vista.Inicio) _status.text = "";

            if (vista == Vista.Inicio) ShowTab(Tab.Crear);
            else if (vista == Vista.Pausa) ShowTab(Tab.Pausa);
            else if (vista == Vista.OpcionesEnPartida) ShowTab(Tab.Opciones);
        }

        if (vista == Vista.Pausa) UpdatePauseTexts();
        if (vista == Vista.Sala) UpdateSala();

        if (!inGame && _status != null && onlineSession != null)
            _status.text = onlineSession.Busy ? onlineSession.Status + " ..." : onlineSession.Status;
    }

    void Awake()
    {
        Instance = this;
    }

    // Lo llama el menu de Escape para abrir las opciones sin salir de la partida
    public void OpenOptionsOverlay()
    {
        OptionsOverlayOpen = true;   // Update cambia la vista y monta la pestana
    }

    public void CloseOptionsOverlay()
    {
        OptionsOverlayOpen = false;
    }

    private void TryBuild()
    {
        if (onlineSession == null)
            onlineSession = FindFirstObjectByType<OnlineSession>();

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();

        if (canvas == null) return;

        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildMenu();
        ShowTab(Tab.Crear);
        _built = true;
    }

    // ---------- Estructura ----------

    private void BuildMenu()
    {
        // Contenedor de todo el menu (fondo + panel), para poder ocultarlo de golpe
        _root = NewRect("MenuPrincipal", canvas.transform, Vector2.zero);
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = Vector2.zero;
        _root.offsetMax = Vector2.zero;

        BuildBackground();

        // Panel con los controles, desplazado para no tapar el titulo del arte
        RectTransform panel = NewRect("Panel", _root, panelSize);
        panel.anchoredPosition = panelOffset;
        AddImage(panel, panelColor);
        _panel = panel;
        BuildSala();

        float w = panelSize.x;
        float top = panelSize.y / 2f;
        float y = top - 12f;

        // Si hay arte de fondo, el titulo ya viene en la imagen
        if (background == null)
        {
            Label("Titulo", "POSUYING", panel, new Vector2(0f, y - 22f), new Vector2(w, 40f),
                26, TextAnchor.MiddleCenter, Color.white);
            y -= 52f;
        }

        // Pestanas
        string[] names = { "CREAR", "BUSCAR", "CODIGO", "OPCIONES" };
        float tabW = (w - 40f) / names.Length;

        for (int i = 0; i < names.Length; i++)
        {
            Tab tab = (Tab)i;
            float x = -w / 2f + 20f + tabW * i + tabW / 2f;
            Button button = Button(names[i], panel, new Vector2(x, y - 18f),
                new Vector2(tabW - 6f, 32f), () => ShowTab(tab));

            _tabButtons.Add(button.gameObject);
        }

        float usedTop = top - (y - 40f);

        _content = NewRect("Contenido", panel, new Vector2(w - 40f, panelSize.y - usedTop - 54f));
        _content.anchoredPosition = new Vector2(0f, -usedTop / 2f + 4f);

        _status = Label("Estado", "", panel, new Vector2(-50f, -panelSize.y / 2f + 22f),
            new Vector2(w - 160f, 36f), 13, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));

        _quitButton = Button("SALIR", panel, new Vector2(w / 2f - 60f, -panelSize.y / 2f + 24f),
            new Vector2(96f, 30f), QuitGame, new Color(0.35f, 0.1f, 0.1f, 1f)).gameObject;
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;   // en el editor, salir del Play
#else
        Application.Quit();
#endif
    }

    private void BuildBackground()
    {
        if (background != null)
        {
            RectTransform bg = NewRect("Fondo", _root, Vector2.zero);
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.offsetMin = Vector2.zero;
            bg.offsetMax = Vector2.zero;

            Image image = bg.gameObject.AddComponent<Image>();
            image.sprite = background;
            image.color = Color.white;
            image.raycastTarget = false;
            _fondo = bg.gameObject;
        }

        // Velo oscuro encima para que los textos se lean sobre el arte
        RectTransform tint = NewRect("Velo", _root, Vector2.zero);
        tint.anchorMin = Vector2.zero;
        tint.anchorMax = Vector2.one;
        tint.offsetMin = Vector2.zero;
        tint.offsetMax = Vector2.zero;
        // Sin arte no hace falta velo en el menu de inicio, pero si en la pausa
        _velo = AddImage(tint, background != null ? backgroundTint : Color.clear);
        _velo.raycastTarget = false;
    }

    private void ShowTab(Tab tab)
    {
        // En partida solo tienen sentido la pausa y las opciones: crear o buscar
        // otra partida desde aqui no aplica
        bool enPartida = _vista != Vista.Inicio;
        if (enPartida && tab != Tab.Pausa) tab = Tab.Opciones;

        _tab = tab;

        foreach (var button in _tabButtons)
            if (button != null) button.SetActive(!enPartida);

        foreach (Transform child in _content)
            Destroy(child.gameObject);

        switch (tab)
        {
            case Tab.Crear: BuildCreateTab(); break;
            case Tab.Buscar: BuildBrowseTab(); break;
            case Tab.Codigo: BuildCodeTab(); break;
            case Tab.Opciones: BuildOptionsTab(); break;
            case Tab.Pausa: BuildPauseTab(); break;
        }
    }

    // ---------- SALA DE ESPERA ----------
    // Solo los botones y los textos: a los jugadores se les ve en 3D detras
    // (los coloca y los enfoca SalaEspera). Por eso aqui no hay panel central,
    // solo un titulo arriba y una barra abajo.

    private RectTransform _panel;
    private RectTransform _sala;
    private Text _salaTitulo, _salaSubtitulo, _salaCodigo, _salaAviso, _salaBotonTexto;
    private GameObject _salaBoton, _salaCopiar;
    private Image _salaBotonFondo;

    private void BuildSala()
    {
        _sala = NewRect("Sala", _root, Vector2.zero);
        _sala.anchorMin = Vector2.zero;
        _sala.anchorMax = Vector2.one;
        _sala.offsetMin = Vector2.zero;
        _sala.offsetMax = Vector2.zero;
        _sala.gameObject.SetActive(false);

        // Arriba: titulo y cuantos sois
        RectTransform arriba = NewRect("Arriba", _sala, new Vector2(900f, 110f));
        arriba.anchorMin = arriba.anchorMax = new Vector2(0.5f, 1f);
        arriba.anchoredPosition = new Vector2(0f, -110f);
        _salaTitulo = Label("Titulo", "SALA DE ESPERA", arriba, new Vector2(0f, 22f), new Vector2(900f, 56f),
            40, TextAnchor.MiddleCenter, Color.white);
        _salaSubtitulo = Label("Subtitulo", "", arriba, new Vector2(0f, -26f), new Vector2(900f, 30f),
            18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));

        // Abajo: codigo, boton principal y salir
        RectTransform barra = NewRect("Barra", _sala, new Vector2(820f, 132f));
        barra.anchorMin = barra.anchorMax = new Vector2(0.5f, 0f);
        barra.anchoredPosition = new Vector2(0f, 120f);
        AddImage(barra, panelColor);

        _salaCodigo = Label("Codigo", "", barra, new Vector2(-270f, 30f), new Vector2(240f, 30f),
            18, TextAnchor.MiddleCenter, Color.white);
        _salaCopiar = Button("Copiar codigo", barra, new Vector2(-270f, -6f), new Vector2(170f, 28f), () =>
        {
            if (onlineSession != null) GUIUtility.systemCopyBuffer = onlineSession.JoinCode;
        }).gameObject;

        Button principal = Button("LISTO", barra, new Vector2(0f, 14f), new Vector2(280f, 60f), PulsarBotonSala, accentColor);
        _salaBoton = principal.gameObject;
        _salaBotonFondo = principal.GetComponent<Image>();
        _salaBotonTexto = principal.GetComponentInChildren<Text>();
        _salaBotonTexto.resizeTextMaxSize = 24;
        _salaBotonTexto.fontSize = 24;

        Button("SALIR DE LA SALA", barra, new Vector2(270f, 14f), new Vector2(190f, 36f),
            NetworkUI.LeaveGame, new Color(0.35f, 0.1f, 0.1f, 1f));

        _salaAviso = Label("Aviso", "", barra, new Vector2(0f, -44f), new Vector2(780f, 26f),
            14, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f));
    }

    private void PulsarBotonSala()
    {
        var sala = SalaEspera.Instance;
        if (sala == null || !sala.IsSpawned) return;

        if (sala.IsServer) { PostUI(uiStart); sala.Empezar(); }
        else sala.CambiarListo();
    }

    private void UpdateSala()
    {
        var sala = SalaEspera.Instance;
        if (sala == null || !sala.IsSpawned) return;

        string codigo = onlineSession != null ? onlineSession.JoinCode : "";
        _salaCodigo.text = string.IsNullOrEmpty(codigo) ? "Partida local" : "CODIGO:  " + codigo;
        _salaCopiar.SetActive(!string.IsNullOrEmpty(codigo));

        // Llegue con la partida empezada: solo queda esperar
        if (sala.EsperandoPermiso || (sala.FaseActual == SalaEspera.Fase.EnJuego))
        {
            _salaTitulo.text = "PARTIDA EN CURSO";
            _salaSubtitulo.text = "Esperando a que el anfitrion te deje entrar...";
            _salaBoton.SetActive(false);
            _salaAviso.text = "Si no responde en unos segundos, la solicitud se rechaza sola";
            return;
        }

        int jugadores = sala.JugadoresEnSala();
        int faltan = sala.FaltanPorEstarListos();
        _salaBoton.SetActive(true);

        if (sala.FaseActual == SalaEspera.Fase.CuentaAtras)
        {
            _salaTitulo.text = "LA PARTIDA EMPIEZA EN  " + Mathf.CeilToInt(sala.SegundosParaEmpezar);
            _salaSubtitulo.text = "";
        }
        else
        {
            _salaTitulo.text = "SALA DE ESPERA";
            _salaSubtitulo.text = jugadores + (jugadores == 1 ? " jugador" : " jugadores") +
                                  (faltan > 0 ? "   |   faltan " + faltan + " por estar listos" : "   |   todos listos");
        }

        string tecla = GameSettings.KeyLabel(GameSettings.ChatKey);
        if (sala.IsServer)
        {
            bool puede = faltan == 0 && sala.FaseActual == SalaEspera.Fase.Sala;
            _salaBotonTexto.text = sala.FaseActual == SalaEspera.Fase.CuentaAtras ? "EMPEZANDO..." : "EMPEZAR PARTIDA";
            _salaBotonFondo.color = puede ? accentColor : buttonColor;
            _salaAviso.text = faltan > 0
                ? "Eres el anfitrion: podras empezar cuando todos esten listos.   " + tecla + ": chat"
                : "Eres el anfitrion: empieza cuando quieras.   " + tecla + ": chat";
        }
        else
        {
            bool listo = sala.LocalListo;
            _salaBotonTexto.text = listo ? "LISTO  (pulsa para cancelar)" : "ESTOY LISTO";
            _salaBotonFondo.color = listo ? new Color(0.15f, 0.45f, 0.2f, 1f) : accentColor;
            _salaAviso.text = (listo ? "Esperando a que el anfitrion empiece la partida." : "Marca LISTO cuando lo estes.") +
                              "   " + tecla + ": chat";
        }
    }

    // ---------- PAUSA (Escape dentro de la partida) ----------

    private void BuildPauseTab()
    {
        float w = _content.sizeDelta.x;
        float y = _content.sizeDelta.y / 2f + 6f;
        var session = onlineSession;

        Label("pausa", "PAUSA", _content, new Vector2(0f, y), new Vector2(w, 36f),
            24, TextAnchor.MiddleCenter, Color.white);

        y -= 34f;
        _pauseInfo = Label("info", "", _content, new Vector2(0f, y), new Vector2(w, 22f),
            14, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f));

        if (session != null && !string.IsNullOrEmpty(session.JoinCode))
        {
            y -= 38f;
            Label("codigo", "CODIGO:  " + session.JoinCode, _content, new Vector2(-70f, y),
                new Vector2(w - 160f, 28f), 20, TextAnchor.MiddleLeft, Color.white);
            Button("Copiar", _content, new Vector2(w / 2f - 70f, y), new Vector2(130f, 28f),
                () => GUIUtility.systemCopyBuffer = session.JoinCode);
        }

        // Solo el anfitrion decide si puede entrar gente con la partida empezada
        _pauseLock = null;
        _pauseLockButton = null;
        if (session != null && session.IsHost)
        {
            y -= 38f;
            _pauseLock = Label("cerrada", "", _content, new Vector2(-70f, y),
                new Vector2(w - 160f, 24f), 14, TextAnchor.MiddleLeft, Color.white);
            Button boton = Button("Cerrar partida", _content, new Vector2(w / 2f - 70f, y),
                new Vector2(130f, 28f), () => session.SetGameLocked(!session.IsGameLocked));
            _pauseLockButton = boton.GetComponentInChildren<Text>();
        }

        float abajo = -_content.sizeDelta.y / 2f;
        Button("SEGUIR JUGANDO", _content, new Vector2(0f, abajo + 122f), new Vector2(280f, 40f),
            NetworkUI.CloseMenu, accentColor);
        Button("OPCIONES", _content, new Vector2(0f, abajo + 74f), new Vector2(280f, 40f),
            OpenOptionsOverlay);
        Button("SALIR DE LA PARTIDA", _content, new Vector2(0f, abajo + 26f), new Vector2(280f, 40f),
            NetworkUI.LeaveGame, new Color(0.35f, 0.1f, 0.1f, 1f));

        UpdatePauseTexts();
    }

    // Lo que puede cambiar con la pausa abierta: jugadores conectados y el candado
    private void UpdatePauseTexts()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (_pauseInfo != null)
        {
            string rol = nm.IsHost ? "Anfitrion" : (nm.IsServer ? "Servidor" : "Invitado");
            _pauseInfo.text = nm.IsServer
                ? rol + "   |   Jugadores conectados: " + nm.ConnectedClientsIds.Count
                : rol;
        }

        if (_pauseLock != null && onlineSession != null)
        {
            bool cerrada = onlineSession.IsGameLocked;
            _pauseLock.text = cerrada ? "Partida CERRADA (no entra nadie mas)"
                                      : "Partida ABIERTA (se puede entrar en marcha)";
            if (_pauseLockButton != null) _pauseLockButton.text = cerrada ? "Abrir partida" : "Cerrar partida";
        }
    }

    // ---------- Pestana CREAR ----------

    private void BuildCreateTab()
    {
        float w = _content.sizeDelta.x;
        float y = _content.sizeDelta.y / 2f - 30f;

        Label("l1", "Nombre de la partida", _content, new Vector2(0f, y), new Vector2(w, 22f),
            14, TextAnchor.MiddleLeft, Color.white);
        _nameInput = Input("nombre", "Partida de " + SystemInfo.deviceName, _content,
            new Vector2(0f, y - 30f), new Vector2(w, 34f), false);

        y -= 66f;
        Label("l2", "Jugadores maximos", _content, new Vector2(0f, y), new Vector2(w, 22f),
            14, TextAnchor.MiddleLeft, Color.white);

        Text playersLabel = Label("num", _maxPlayers.ToString(), _content,
            new Vector2(0f, y - 30f), new Vector2(120f, 30f), 18, TextAnchor.MiddleCenter, Color.white);

        Button("-", _content, new Vector2(-90f, y - 30f), new Vector2(40f, 30f), () =>
        {
            _maxPlayers = Mathf.Max(2, _maxPlayers - 1);
            playersLabel.text = _maxPlayers.ToString();
        });

        Button("+", _content, new Vector2(90f, y - 30f), new Vector2(40f, 30f), () =>
        {
            _maxPlayers = Mathf.Min(8, _maxPlayers + 1);
            playersLabel.text = _maxPlayers.ToString();
        });

        y -= 66f;
        Text privacy = null;
        privacy = Label("priv", PrivacyText(), _content, new Vector2(0f, y), new Vector2(w, 22f),
            14, TextAnchor.MiddleLeft, Color.white);

        Button("Cambiar", _content, new Vector2(w / 2f - 70f, y), new Vector2(130f, 28f), () =>
        {
            _isPrivate = !_isPrivate;
            privacy.text = PrivacyText();
        });

        y -= 44f;
        Label("l3", "Contrasena (opcional)", _content, new Vector2(0f, y), new Vector2(w, 22f),
            14, TextAnchor.MiddleLeft, Color.white);
        _passwordInput = Input("pass", "", _content, new Vector2(0f, y - 30f),
            new Vector2(w, 34f), true);

        // El boton va debajo del campo, siguiendo el flujo (antes se anclaba al
        // fondo del panel y acababa montandose encima de la contrasena)
        y -= 70f;

        Button("CREAR PARTIDA", _content, new Vector2(0f, y),
            new Vector2(240f, 42f), () =>
            {
                PostUI(uiStart);
                onlineSession.CreateOnlineGame(new OnlineSession.GameConfig
                {
                    name = _nameInput.text,
                    maxPlayers = _maxPlayers,
                    isPrivate = _isPrivate,
                    password = _passwordInput.text
                });
            }, accentColor, false);
    }

    private string PrivacyText()
    {
        return _isPrivate
            ? "Privada  (solo con codigo)"
            : "Publica  (aparece en la lista)";
    }

    // ---------- Pestana BUSCAR ----------

    private void BuildBrowseTab()
    {
        float w = _content.sizeDelta.x;
        float top = _content.sizeDelta.y / 2f;

        Button("Actualizar lista", _content, new Vector2(0f, top - 22f), new Vector2(200f, 32f),
            () => { onlineSession.RefreshSessionList(); Invoke(nameof(RefreshBrowse), 1.5f); });

        var sessions = onlineSession != null ? onlineSession.AvailableSessions : null;

        if (sessions == null || sessions.Count == 0)
        {
            Label("vacio", "No hay partidas. Pulsa 'Actualizar lista'.", _content,
                new Vector2(0f, 0f), new Vector2(w, 30f), 14, TextAnchor.MiddleCenter,
                new Color(1f, 1f, 1f, 0.6f));
            return;
        }

        float y = top - 70f;
        int shown = Mathf.Min(sessions.Count, 6);

        for (int i = 0; i < shown; i++)
        {
            ISessionInfo info = sessions[i];
            string id = info.Id;

            string tags = "";
            if (info.HasPassword) tags += "  [clave]";
            if (info.IsLocked) tags += "  [cerrada]";

            string text = info.Name + "   " +
                          (info.MaxPlayers - info.AvailableSlots) + "/" + info.MaxPlayers + tags;

            bool joinable = !info.IsLocked && info.AvailableSlots > 0;

            bool needsPassword = info.HasPassword;

            Button(text, _content, new Vector2(0f, y), new Vector2(w, 36f), () =>
            {
                _selectedSessionId = id;
                if (!joinable) { PlayClick(); return; }

                string typed = _joinPasswordInput != null ? _joinPasswordInput.text : "";

                // Intentar entrar sin la contrasena deja la red en mal estado y
                // luego ya no se puede entrar en ninguna partida. Mejor no intentarlo.
                if (needsPassword && string.IsNullOrWhiteSpace(typed))
                {
                    _status.text = "Esa partida pide contrasena: escribela abajo y vuelve a pulsar";
                    PlayClick();
                    return;
                }

                PostUI(uiStart);
                onlineSession.JoinSessionById(id, typed);
            }, joinable ? buttonColor : new Color(1f, 0.3f, 0.3f, 0.15f), false);

            y -= 42f;
        }

        Label("l4", "Contrasena (si la pide)", _content,
            new Vector2(0f, -_content.sizeDelta.y / 2f + 62f), new Vector2(w, 20f),
            13, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.7f));

        _joinPasswordInput = Input("joinpass", "", _content,
            new Vector2(0f, -_content.sizeDelta.y / 2f + 32f), new Vector2(w, 32f), true);
    }

    private void RefreshBrowse()
    {
        if (_tab == Tab.Buscar) ShowTab(Tab.Buscar);
    }

    // ---------- Pestana CODIGO ----------

    private void BuildCodeTab()
    {
        float w = _content.sizeDelta.x;

        Label("l5", "Codigo de la partida", _content, new Vector2(0f, 60f), new Vector2(w, 24f),
            16, TextAnchor.MiddleCenter, Color.white);

        _codeInput = Input("codigo", "", _content, new Vector2(0f, 20f), new Vector2(260f, 40f), false);

        Button("UNIRSE", _content, new Vector2(0f, -40f), new Vector2(200f, 40f),
            () => { PostUI(uiStart); onlineSession.JoinOnlineGame(_codeInput.text); },
            accentColor, false);

        Label("l6", "Para partidas privadas que te hayan pasado por chat", _content,
            new Vector2(0f, -90f), new Vector2(w, 22f), 12, TextAnchor.MiddleCenter,
            new Color(1f, 1f, 1f, 0.6f));
    }

    // ---------- Pestana OPCIONES ----------

    private void BuildOptionsTab()
    {
        GameSettings.Load();

        if (_resIndex < 0)
        {
            _resIndex = GameSettings.IndexOfCurrentResolution();
            _fullscreen = GameSettings.SavedFullscreen;
        }

        float w = _content.sizeDelta.x;
        float top = _content.sizeDelta.y / 2f;

        // Sub-pestanas de categoria
        string[] names = { "GENERAL", "GRAFICOS", "AUDIO", "CONTROLES" };
        float tabW = 122f;

        for (int i = 0; i < names.Length; i++)
        {
            OptionsTab tab = (OptionsTab)i;
            float x = (i - (names.Length - 1) * 0.5f) * tabW * 1.05f;
            Color color = _optionsTab == tab ? accentColor : buttonColor;

            Button(names[i], _content, new Vector2(x, top - 16f), new Vector2(tabW, 28f),
                () => { _optionsTab = tab; ShowTab(Tab.Opciones); }, color);
        }

        switch (_optionsTab)
        {
            case OptionsTab.General: BuildGeneralOptions(); break;
            case OptionsTab.Graficos: BuildGraphicsOptions(); break;
            case OptionsTab.Audio: BuildAudioOptions(); break;
            case OptionsTab.Controles: BuildControlsOptions(); break;
        }

        // El audio se aplica solo al mover los sliders: en esa pestana APLICAR
        // sobra y solo haria pensar que hay que pulsarlo
        bool needsApply = _optionsTab != OptionsTab.Audio && _optionsTab != OptionsTab.Controles;

        // En partida el boton comparte fila con el de volver
        float applyX = OptionsOverlayOpen ? -110f : 0f;

        if (needsApply)
        {
            Button("APLICAR", _content, new Vector2(applyX, -top + 24f), new Vector2(200f, 34f), () =>
            {
                GameSettings.ApplyResolution(GameSettings.Resolutions[_resIndex], _fullscreen);
                GameSettings.SaveAndApply();
            }, accentColor);
        }

        if (OptionsOverlayOpen)
        {
            Button("VOLVER", _content, new Vector2(needsApply ? 110f : 0f, -top + 24f),
                new Vector2(200f, 34f), CloseOptionsOverlay);
        }
    }

    // ---------- Categorias ----------

    private void BuildGeneralOptions()
    {
        float w = _content.sizeDelta.x;

        // Nombre del jugador
        float y = RowY(0);
        Label("lname", "Tu nombre", _content, new Vector2(-w / 2f + 85f, y),
            new Vector2(170f, 22f), 14, TextAnchor.MiddleLeft, Color.white);

        InputField nameField = Input("nombreJugador", PlayerProfile.Name, _content,
            new Vector2(w / 2f - 130f, y), new Vector2(220f, 28f), false);
        nameField.characterLimit = PlayerProfile.MaxLength;
        nameField.onEndEdit.AddListener(value => PlayerProfile.Name = value);

        // Deslizante y en vivo: con los pasos de 0,25 de antes no se podia afinar,
        // y asi se nota al momento si la ajustas con la partida en marcha
        SensitivityRow(1);

        ToggleRow(2, "Invertir eje Y", () => GameSettings.OnOff(GameSettings.InvertY),
            () => GameSettings.InvertY = !GameSettings.InvertY);

        StepRow(3, "Campo de vision", () => Mathf.RoundToInt(GameSettings.FieldOfView) + " grados",
            () => GameSettings.FieldOfView = Mathf.Max(60f, GameSettings.FieldOfView - 5f),
            () => GameSettings.FieldOfView = Mathf.Min(110f, GameSettings.FieldOfView + 5f));

        // Los NPCs los crea el servidor, asi que aqui solo manda el anfitrion.
        // Se puede cambiar en mitad de la partida: aparecen o desaparecen al vuelo.
        ToggleRow(4, "Companeros NPC", () => GameSettings.OnOff(GameSettings.NpcsEnabled),
            () => GameSettings.NpcsEnabled = !GameSettings.NpcsEnabled);

        StepRow(5, "Cuantos NPC", () => GameSettings.NpcCount.ToString(),
            () => GameSettings.NpcCount = Mathf.Max(1, GameSettings.NpcCount - 1),
            () => GameSettings.NpcCount = Mathf.Min(8, GameSettings.NpcCount + 1));

        // El minimapa es cosa de cada jugador: no depende del anfitrion
        ToggleRow(6, "Minimapa", () => GameSettings.OnOff(GameSettings.MinimapEnabled),
            () => GameSettings.MinimapEnabled = !GameSettings.MinimapEnabled);

        StepRow(7, "Alcance minimapa", () => Mathf.RoundToInt(GameSettings.MinimapRange) + " m",
            () => GameSettings.MinimapRange = Mathf.Max(15f, GameSettings.MinimapRange - 5f),
            () => GameSettings.MinimapRange = Mathf.Min(100f, GameSettings.MinimapRange + 5f));
    }

    private void BuildGraphicsOptions()
    {
        var list = GameSettings.Resolutions;

        StepRow(0, "Resolucion",
            () => list[Mathf.Clamp(_resIndex, 0, list.Count - 1)].x + " x " +
                  list[Mathf.Clamp(_resIndex, 0, list.Count - 1)].y,
            () => _resIndex = (_resIndex - 1 + list.Count) % list.Count,
            () => _resIndex = (_resIndex + 1) % list.Count);

        ToggleRow(1, "Pantalla", () => _fullscreen ? "Completa" : "En ventana",
            () => _fullscreen = !_fullscreen);

        StepRow(2, "Calidad", () => QualitySettings.names[Mathf.Clamp(GameSettings.QualityLevel, 0, QualitySettings.names.Length - 1)],
            () => GameSettings.QualityLevel = Mathf.Max(0, GameSettings.QualityLevel - 1),
            () => GameSettings.QualityLevel = Mathf.Min(QualitySettings.names.Length - 1, GameSettings.QualityLevel + 1));

        StepRow(3, "Fotogramas", () => GameSettings.FpsLabel(GameSettings.TargetFps),
            () => GameSettings.TargetFps = StepFps(-1),
            () => GameSettings.TargetFps = StepFps(1));

        StepRow(4, "Distancia sombras", () => Mathf.RoundToInt(GameSettings.ShadowDistance) + " m",
            () => GameSettings.ShadowDistance = Mathf.Max(10f, GameSettings.ShadowDistance - 10f),
            () => GameSettings.ShadowDistance = Mathf.Min(100f, GameSettings.ShadowDistance + 10f));

        StepRow(5, "Escala de render", () => GameSettings.Percent(GameSettings.RenderScale),
            () => GameSettings.RenderScale = Mathf.Max(0.5f, GameSettings.RenderScale - 0.1f),
            () => GameSettings.RenderScale = Mathf.Min(1f, GameSettings.RenderScale + 0.1f));

        ToggleRow(6, "Contador de FPS", () => GameSettings.OnOff(GameSettings.ShowFps),
            () => GameSettings.ShowFps = !GameSettings.ShowFps);

        Label("nota_fps", "\"VSync\" sincroniza con tu monitor; para bajar consumo,\nelige un limite concreto (60, 75...).",
            _content, new Vector2(0f, RowY(7) - 6f), new Vector2(_content.sizeDelta.x, 40f),
            11, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.55f));
    }

    // El audio se aplica al instante mientras arrastras: aqui no hace falta APLICAR
    private void BuildAudioOptions()
    {
        SliderRow(0, "Volumen general", GameSettings.MasterVolume,
            v => { GameSettings.MasterVolume = v; GameSettings.ApplyVolumes(); },
            () => GameSettings.Percent(GameSettings.MasterVolume));

        SliderRow(1, "Musica", GameSettings.MusicVolume,
            v => { GameSettings.MusicVolume = v; GameSettings.ApplyVolumes(); },
            () => GameSettings.Percent(GameSettings.MusicVolume));

        SliderRow(2, "Efectos", GameSettings.SfxVolume,
            v => { GameSettings.SfxVolume = v; GameSettings.ApplyVolumes(); },
            () => GameSettings.Percent(GameSettings.SfxVolume));

        // Hasta el 200 %: hay microfonos que llegan muy bajos y hace falta margen
        SliderRow(3, "Voz de otros jugadores", GameSettings.VoiceVolume,
            v => { GameSettings.VoiceVolume = v; GameSettings.ApplyVolumes(); },
            () => GameSettings.Percent(GameSettings.VoiceVolume), 0f, 2f);

        ToggleRow(4, "Chat de voz", () => GameSettings.OnOff(GameSettings.VoiceEnabled),
            () => { GameSettings.VoiceEnabled = !GameSettings.VoiceEnabled; GameSettings.ApplyVolumes(); PlayerPrefs.Save(); });

        MicRow(5);

        Label("aviso", "Los cambios se aplican al momento.\nLos efectos aun no tienen sonidos: su volumen queda guardado.",
            _content, new Vector2(0f, RowY(7)), new Vector2(_content.sizeDelta.x, 40f),
            12, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.55f));
    }

    // Sensibilidad: barra para el ajuste gordo y botones < > para afinar de
    // centesima en centesima (la barra sola no da para tanto: 1 pixel = 0,03).
    private void SensitivityRow(int index)
    {
        float w = _content.sizeDelta.x;
        float y = RowY(index);
        float right = w / 2f - 105f;

        Label("l_sens", "Sensibilidad raton", _content, new Vector2(-w / 2f + 85f, y),
            new Vector2(170f, 22f), 14, TextAnchor.MiddleLeft, Color.white);

        Text value = Label("v_sens", GameSettings.MouseSensitivity.ToString("0.00"), _content,
            new Vector2(right + 84f, y), new Vector2(46f, 24f), 14, TextAnchor.MiddleRight, Color.white);

        Slider slider = NewSlider("s_sens", _content, new Vector2(right - 40f, y), new Vector2(150f, 24f));
        slider.minValue = GameSettings.MinSensitivity;
        slider.maxValue = GameSettings.MaxSensitivity;
        slider.SetValueWithoutNotify(GameSettings.MouseSensitivity);

        System.Action<float> poner = v =>
        {
            GameSettings.MouseSensitivity = Mathf.Round(v * 100f) / 100f;
            GameSettings.ApplySensitivity();
            value.text = GameSettings.MouseSensitivity.ToString("0.00");
        };

        slider.onValueChanged.AddListener(v => poner(v));

        var trigger = slider.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var alSoltar = new UnityEngine.EventSystems.EventTrigger.Entry
        {
            eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp
        };
        alSoltar.callback.AddListener(_ => PlayerPrefs.Save());
        trigger.triggers.Add(alSoltar);

        Button("<", _content, new Vector2(right - 130f, y), new Vector2(24f, 24f), () =>
        {
            poner(GameSettings.MouseSensitivity - 0.01f);
            slider.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            PlayerPrefs.Save();
        });

        Button(">", _content, new Vector2(right + 50f, y), new Vector2(24f, 24f), () =>
        {
            poner(GameSettings.MouseSensitivity + 0.01f);
            slider.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            PlayerPrefs.Save();
        });
    }

    // Fila para elegir microfono: < nombre >. Mas ancha que StepRow porque los
    // nombres de los dispositivos son largos; si aun asi no cabe, la letra encoge.
    private void MicRow(int index)
    {
        float w = _content.sizeDelta.x;
        float y = RowY(index);

        Label("l_mic", "Microfono", _content, new Vector2(-w / 2f + 60f, y),
            new Vector2(120f, 22f), 14, TextAnchor.MiddleLeft, Color.white);

        float centro = w / 2f - 185f;
        Text value = Label("v_mic", GameSettings.MicLabel(), _content, new Vector2(centro, y),
            new Vector2(290f, 24f), 14, TextAnchor.MiddleCenter, Color.white);
        value.resizeTextForBestFit = true;
        value.resizeTextMinSize = 9;
        value.resizeTextMaxSize = 14;

        Button("<", _content, new Vector2(centro - 165f, y), new Vector2(30f, 24f),
            () => { GameSettings.StepMic(-1); value.text = GameSettings.MicLabel(); });

        Button(">", _content, new Vector2(centro + 165f, y), new Vector2(30f, 24f),
            () => { GameSettings.StepMic(1); value.text = GameSettings.MicLabel(); });
    }

    // Las teclas se guardan nada mas elegirlas: aqui tampoco hace falta APLICAR
    private void BuildControlsOptions()
    {
        KeyRow(0, "Chat de texto", () => GameSettings.ChatKey, k => GameSettings.ChatKey = k);
        KeyRow(1, "Chat de voz (mantener)", () => GameSettings.VoiceKey, k => GameSettings.VoiceKey = k);
        KeyRow(2, "Marcar para el equipo", () => GameSettings.PingKey, k => GameSettings.PingKey = k);
        KeyRow(3, "Usar objeto elegido", () => GameSettings.UseKey, k => GameSettings.UseKey = k);
        KeyRow(4, "Soltar objeto elegido", () => GameSettings.DropKey, k => GameSettings.DropKey = k);

        Label("aviso_teclas", "Pulsa el boton y despues la tecla nueva. Escape cancela.",
            _content, new Vector2(0f, RowY(6)), new Vector2(_content.sizeDelta.x, 40f),
            12, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.55f));
    }

    // ---------- Eleccion de teclas ----------

    /// <summary>True mientras se espera la tecla nueva: Escape cancela en vez de cerrar el menu.</summary>
    public static bool CapturingKey { get; private set; }

    private System.Action<KeyCode> _alCapturar;
    private Text _textoCaptura;
    private System.Func<KeyCode> _leerCaptura;
    private int _frameCaptura;

    // Fila con un boton que muestra la tecla y, al pulsarlo, espera la nueva
    private void KeyRow(int index, string label, System.Func<KeyCode> read, System.Action<KeyCode> write)
    {
        float w = _content.sizeDelta.x;
        float y = RowY(index);

        Label("l_" + label, label, _content, new Vector2(-w / 2f + 110f, y),
            new Vector2(220f, 22f), 14, TextAnchor.MiddleLeft, Color.white);

        Button button = null;
        button = Button(GameSettings.KeyLabel(read()), _content, new Vector2(w / 2f - 105f, y),
            new Vector2(170f, 26f), () =>
            {
                CancelKeyCapture();
                _textoCaptura = button.GetComponentInChildren<Text>();
                _textoCaptura.text = "Pulsa una tecla...";
                _leerCaptura = read;
                _alCapturar = write;
                _frameCaptura = Time.frameCount;
                CapturingKey = true;
            });
    }

    private void CancelKeyCapture()
    {
        if (_textoCaptura != null && _leerCaptura != null)
            _textoCaptura.text = GameSettings.KeyLabel(_leerCaptura());
        _alCapturar = null;
        _textoCaptura = null;
        _leerCaptura = null;
        CapturingKey = false;
    }

    private void UpdateKeyCapture()
    {
        if (!CapturingKey) return;

        // Si la fila ya no existe (se cambio de pestana o se cerro el menu), se cancela
        if (_textoCaptura == null || !_textoCaptura.gameObject.activeInHierarchy) { CancelKeyCapture(); return; }
        if (Time.frameCount == _frameCaptura || !UnityEngine.Input.anyKeyDown) return;

        if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { CancelKeyCapture(); return; }

        foreach (KeyCode key in System.Enum.GetValues(typeof(KeyCode)))
        {
            // El clic izquierdo y el derecho son disparar y apuntar; los mandos, otro dia
            if (key == KeyCode.None || key == KeyCode.Mouse0 || key == KeyCode.Mouse1) continue;
            if (key >= KeyCode.JoystickButton0) continue;
            if (!UnityEngine.Input.GetKeyDown(key)) continue;

            _alCapturar(key);
            GameSettings.SaveKeys();
            CancelKeyCapture();
            return;
        }
    }

    // ---------- Filas reutilizables ----------

    private float RowY(int index) => _content.sizeDelta.y / 2f - 58f - index * 32f;

    // Fila con < valor >
    private void StepRow(int index, string label, System.Func<string> read,
        System.Action previous, System.Action next)
    {
        float w = _content.sizeDelta.x;
        float y = RowY(index);
        float right = w / 2f - 105f;

        Label("l_" + label, label, _content, new Vector2(-w / 2f + 85f, y),
            new Vector2(170f, 22f), 14, TextAnchor.MiddleLeft, Color.white);

        Text value = Label("v_" + label, read(), _content, new Vector2(right, y),
            new Vector2(120f, 24f), 14, TextAnchor.MiddleCenter, Color.white);

        Button("<", _content, new Vector2(right - 82f, y), new Vector2(30f, 24f),
            () => { previous(); value.text = read(); });

        Button(">", _content, new Vector2(right + 82f, y), new Vector2(30f, 24f),
            () => { next(); value.text = read(); });
    }

    // Fila con barra deslizante y el valor a la derecha. Cada movimiento llama a
    // onChange, asi que lo que haga onChange se nota mientras arrastras.
    private void SliderRow(int index, string label, float initial,
        System.Action<float> onChange, System.Func<string> read, float min = 0f, float max = 1f)
    {
        float w = _content.sizeDelta.x;
        float y = RowY(index);
        float right = w / 2f - 105f;

        Label("l_" + label, label, _content, new Vector2(-w / 2f + 85f, y),
            new Vector2(170f, 22f), 14, TextAnchor.MiddleLeft, Color.white);

        Text value = Label("v_" + label, read(), _content, new Vector2(right + 80f, y),
            new Vector2(54f, 24f), 14, TextAnchor.MiddleRight, Color.white);

        Slider slider = NewSlider("s_" + label, _content, new Vector2(right - 24f, y),
            new Vector2(170f, 24f));

        // Sin notificar: poner el valor inicial no debe contar como un cambio
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(Mathf.Clamp(initial, min, max));
        slider.onValueChanged.AddListener(v => { onChange(v); value.text = read(); });

        // Mientras arrastras solo se guarda en memoria; a disco, al soltar
        var trigger = slider.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var alSoltar = new UnityEngine.EventSystems.EventTrigger.Entry
        {
            eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp
        };
        alSoltar.callback.AddListener(_ => PlayerPrefs.Save());
        trigger.triggers.Add(alSoltar);
    }

    // Fila con boton de Cambiar
    private void ToggleRow(int index, string label, System.Func<string> read, System.Action toggle)
    {
        float w = _content.sizeDelta.x;
        float y = RowY(index);
        float right = w / 2f - 105f;

        Label("l_" + label, label, _content, new Vector2(-w / 2f + 85f, y),
            new Vector2(170f, 22f), 14, TextAnchor.MiddleLeft, Color.white);

        Text value = Label("v_" + label, read(), _content, new Vector2(right - 40f, y),
            new Vector2(140f, 24f), 14, TextAnchor.MiddleCenter, Color.white);

        Button("Cambiar", _content, new Vector2(right + 82f, y), new Vector2(84f, 24f),
            () => { toggle(); value.text = read(); });
    }

    private int StepFps(int direction)
    {
        int index = System.Array.IndexOf(GameSettings.FpsOptions, GameSettings.TargetFps);
        if (index < 0) index = 1;

        index = (index + direction + GameSettings.FpsOptions.Length) % GameSettings.FpsOptions.Length;
        return GameSettings.FpsOptions[index];
    }

    // ---------- Constructores de interfaz ----------

    private RectTransform NewRect(string name, Transform parent, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        return rt;
    }

    private Image AddImage(RectTransform rt, Color color)
    {
        Image image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private Text Label(string name, string content, Transform parent, Vector2 pos, Vector2 size,
        int fontSize, TextAnchor anchor, Color color)
    {
        RectTransform rt = NewRect(name, parent, size);
        rt.anchoredPosition = pos;

        Text text = rt.gameObject.AddComponent<Text>();
        text.font = _font;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        text.text = content;
        return text;
    }

    private Button Button(string caption, Transform parent, Vector2 pos, Vector2 size,
        UnityEngine.Events.UnityAction onClick, Color? color = null, bool playClick = true)
    {
        RectTransform rt = NewRect("Btn_" + caption, parent, size);
        rt.anchoredPosition = pos;

        Image bg = AddImage(rt, color ?? buttonColor);
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(onClick);

        // Sonidos del boton. Los de empezar partida ponen playClick = false
        // porque ya lanzan su propio sonido (uiStart).
        if (playClick)
            button.onClick.AddListener(PlayClick);
        rt.gameObject.AddComponent<UIHoverSound>().onEnter = PlayHover;

        Text label = Label("Texto", caption, rt, Vector2.zero, size, 15,
            TextAnchor.MiddleCenter, Color.white);
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 9;
        label.resizeTextMaxSize = 15;

        return button;
    }

    // Slider de 0 a 1 con el estilo del menu: carril tenue, relleno del color de
    // acento y tirador blanco. Montado a mano porque todo el menu se crea en runtime.
    private Slider NewSlider(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        const float handleWidth = 12f;
        const float trackHeight = 6f;

        RectTransform rt = NewRect(name, parent, size);
        rt.anchoredPosition = pos;

        // Zona de clic invisible con la altura de la fila entera: el carril solo
        // mide 6 px y seria muy dificil de acertar
        AddImage(rt, new Color(0f, 0f, 0f, 0f));

        RectTransform track = NewRect("Carril", rt, Vector2.zero);
        track.anchorMin = new Vector2(0f, 0.5f);
        track.anchorMax = new Vector2(1f, 0.5f);
        track.sizeDelta = new Vector2(0f, trackHeight);
        AddImage(track, buttonColor).raycastTarget = false;

        // Relleno y tirador van en zonas encogidas medio tirador por cada lado,
        // para que en 0 y en 100 el tirador no se salga del carril
        RectTransform fillArea = NewRect("ZonaRelleno", rt, Vector2.zero);
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.sizeDelta = new Vector2(-handleWidth, trackHeight);

        RectTransform fill = NewRect("Relleno", fillArea, Vector2.zero);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.sizeDelta = new Vector2(handleWidth, 0f);   // llega hasta debajo del tirador
        AddImage(fill, accentColor).raycastTarget = false;

        RectTransform handleArea = NewRect("ZonaTirador", rt, Vector2.zero);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.sizeDelta = new Vector2(-handleWidth, 0f);

        RectTransform handle = NewRect("Tirador", handleArea, Vector2.zero);
        handle.anchorMin = new Vector2(0f, 0.2f);
        handle.anchorMax = new Vector2(0f, 0.8f);
        handle.sizeDelta = new Vector2(handleWidth, 0f);
        Image handleImage = AddImage(handle, Color.white);

        Slider slider = rt.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;

        // Sin navegacion por teclado: si no, las flechas moverian el volumen
        // mientras el jugador navega por el menu
        slider.navigation = new Navigation { mode = Navigation.Mode.None };

        return slider;
    }

    private InputField Input(string name, string initial, Transform parent, Vector2 pos,
        Vector2 size, bool isPassword)
    {
        RectTransform rt = NewRect(name, parent, size);
        rt.anchoredPosition = pos;
        AddImage(rt, new Color(1f, 1f, 1f, 0.10f));

        Text text = Label("Texto", "", rt, Vector2.zero, size - new Vector2(16f, 8f),
            15, TextAnchor.MiddleLeft, Color.white);
        text.supportRichText = false;

        InputField field = rt.gameObject.AddComponent<InputField>();
        field.textComponent = text;
        field.text = initial;
        if (isPassword) field.contentType = InputField.ContentType.Password;

        return field;
    }
    // ---------- Sonido (Wwise) ----------

    // Wwise solo reproduce sobre objetos registrados (con AkGameObj). Se crean
    // aqui como hijos del menu, que vive toda la partida.
    private GameObject Emitter(ref GameObject field, string name)
    {
        if (field == null)
        {
            field = new GameObject(name);
            field.transform.SetParent(transform, false);
            field.AddComponent<AkGameObj>();
        }
        return field;
    }

    private void PostUI(AK.Wwise.Event evt)
    {
        if (evt != null && evt.IsValid())
            evt.Post(Emitter(ref _uiEmitter, "Audio_UI"));
    }

    private void PlayClick() { PostUI(uiClick); }
    private void PlayHover() { PostUI(uiHover); }

    // Musica del menu y ambiente de partida. Solo actua en el CAMBIO
    // menu <-> partida, no cada frame. Abrir las Opciones con Escape en mitad
    // de la partida no cuenta como volver al menu.
    private void UpdateMenuAudio(bool inGame)
    {
        if (_wasInGame == inGame) return;
        _wasInGame = inGame;

        GameObject music = Emitter(ref _musicEmitter, "Audio_Musica");
        GameObject ambience = Emitter(ref _ambienceEmitter, "Audio_Ambiente");

        if (inGame)
        {
            if (menuMusicStop != null && menuMusicStop.IsValid()) menuMusicStop.Post(music);
            if (ambiencePlay != null && ambiencePlay.IsValid()) ambiencePlay.Post(ambience);
        }
        else
        {
            if (ambiencePlay != null && ambiencePlay.IsValid()) ambiencePlay.Stop(ambience, ambienceFadeOutMs);
            if (menuMusicPlay != null && menuMusicPlay.IsValid()) menuMusicPlay.Post(music);
        }
    }

    // Los sliders de Opciones movian el AudioListener de Unity, que Wwise no
    // escucha. Aqui se trasladan a Wwise: Maestro = salida general, Musica = el
    // emisor de la musica del menu (que se oye por la camara del menu).
    private void UpdateVolumes()
    {
        if (!AkUnitySoundEngine.IsInitialized()) return;

        float master = GameSettings.MasterVolume;
        if (!Mathf.Approximately(master, _appliedMaster))
        {
            AkUnitySoundEngine.SetOutputVolume(0, master);
            _appliedMaster = master;
        }

        if (_menuCamera == null) _menuCamera = FindFirstObjectByType<MenuCamera>();
        if (_menuCamera == null) return;

        float music = GameSettings.MusicVolume;
        if (!Mathf.Approximately(music, _appliedMusic))
        {
            GameObject emitter = Emitter(ref _musicEmitter, "Audio_Musica");
            AkUnitySoundEngine.SetGameObjectOutputBusVolume(
                AkUnitySoundEngine.GetAkGameObjectID(emitter),
                AkUnitySoundEngine.GetAkGameObjectID(_menuCamera.gameObject),
                music);
            _appliedMusic = music;
        }
    }
}

// Sonido al pasar el raton por encima de un boton. Se usa esto y no un
// EventTrigger porque el EventTrigger se queda con TODOS los eventos del raton
// (arrastre, rueda) y romperia el scroll de las listas.
public class UIHoverSound : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler
{
    public System.Action onEnter;

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        onEnter?.Invoke();
    }
}
