using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

// Pantalla inicial del juego: crear partida, buscar partidas abiertas,
// entrar por codigo y opciones. Se construye por codigo sobre el Canvas.
//
// Solo se ve cuando NO estamos en partida; dentro del juego manda el panel
// rapido de NetworkUI (tecla Escape).
public class MainMenuUI : MonoBehaviour
{
    private enum Tab { Crear, Buscar, Codigo, Opciones }

    [Header("Referencias")]
    public OnlineSession onlineSession;
    public Canvas canvas;

    [Header("Fondo")]
    [Tooltip("Imagen de fondo del menu. Debe importarse como Sprite (2D and UI).")]
    public Sprite background;
    [Tooltip("Oscurecido sobre el fondo, para que se lea el texto")]
    public Color backgroundTint = new Color(0f, 0f, 0f, 0.35f);

    [Header("Estilo")]
    public Vector2 panelSize = new Vector2(620f, 460f);
    [Tooltip("Desplazamiento del panel. Negativo lo baja, para no tapar el titulo del arte.")]
    public Vector2 panelOffset = new Vector2(0f, -150f);
    public Color panelColor = new Color(0.05f, 0.06f, 0.09f, 0.88f);
    public Color accentColor = new Color(0.55f, 0.12f, 0.12f, 1f);
    public Color buttonColor = new Color(1f, 1f, 1f, 0.14f);

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
    private int _resIndex = -1;
    private bool _fullscreen = true;

    void Update()
    {
        if (!_built)
        {
            TryBuild();
            return;
        }

        bool inGame = NetworkManager.Singleton != null &&
                      (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);

        if (_root.gameObject.activeSelf == inGame)
            _root.gameObject.SetActive(!inGame);

        if (!inGame && _status != null && onlineSession != null)
            _status.text = onlineSession.Busy ? onlineSession.Status + " ..." : onlineSession.Status;
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

        // Nombre del jugador: se guarda solo y vale para crear y para unirse
        Label("lNombre", "Tu nombre:", panel, new Vector2(-w / 2f + 70f, y - 20f),
            new Vector2(120f, 24f), 14, TextAnchor.MiddleLeft, Color.white);

        InputField playerName = Input("nombreJugador", PlayerProfile.Name, panel,
            new Vector2(40f, y - 20f), new Vector2(w - 180f, 30f), false);
        playerName.characterLimit = PlayerProfile.MaxLength;
        playerName.onEndEdit.AddListener(value => PlayerProfile.Name = value);

        y -= 46f;

        // Pestanas
        string[] names = { "CREAR", "BUSCAR", "CODIGO", "OPCIONES" };
        float tabW = (w - 40f) / names.Length;

        for (int i = 0; i < names.Length; i++)
        {
            Tab tab = (Tab)i;
            float x = -w / 2f + 20f + tabW * i + tabW / 2f;
            Button(names[i], panel, new Vector2(x, y - 18f), new Vector2(tabW - 6f, 32f),
                () => ShowTab(tab));
        }

        float usedTop = top - (y - 40f);

        _content = NewRect("Contenido", panel, new Vector2(w - 40f, panelSize.y - usedTop - 54f));
        _content.anchoredPosition = new Vector2(0f, -usedTop / 2f + 4f);

        _status = Label("Estado", "", panel, new Vector2(-50f, -panelSize.y / 2f + 22f),
            new Vector2(w - 160f, 36f), 13, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));

        Button("SALIR", panel, new Vector2(w / 2f - 60f, -panelSize.y / 2f + 24f),
            new Vector2(96f, 30f), QuitGame, new Color(0.35f, 0.1f, 0.1f, 1f));
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
        if (background == null) return;

        RectTransform bg = NewRect("Fondo", _root, Vector2.zero);
        bg.anchorMin = Vector2.zero;
        bg.anchorMax = Vector2.one;
        bg.offsetMin = Vector2.zero;
        bg.offsetMax = Vector2.zero;

        Image image = bg.gameObject.AddComponent<Image>();
        image.sprite = background;
        image.color = Color.white;
        image.raycastTarget = false;

        // Velo oscuro encima para que los textos se lean sobre el arte
        RectTransform tint = NewRect("Velo", _root, Vector2.zero);
        tint.anchorMin = Vector2.zero;
        tint.anchorMax = Vector2.one;
        tint.offsetMin = Vector2.zero;
        tint.offsetMax = Vector2.zero;
        AddImage(tint, backgroundTint).raycastTarget = false;
    }

    private void ShowTab(Tab tab)
    {
        _tab = tab;

        foreach (Transform child in _content)
            Destroy(child.gameObject);

        switch (tab)
        {
            case Tab.Crear: BuildCreateTab(); break;
            case Tab.Buscar: BuildBrowseTab(); break;
            case Tab.Codigo: BuildCodeTab(); break;
            case Tab.Opciones: BuildOptionsTab(); break;
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
                onlineSession.CreateOnlineGame(new OnlineSession.GameConfig
                {
                    name = _nameInput.text,
                    maxPlayers = _maxPlayers,
                    isPrivate = _isPrivate,
                    password = _passwordInput.text
                });
            }, accentColor);
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

            Button(text, _content, new Vector2(0f, y), new Vector2(w, 36f), () =>
            {
                _selectedSessionId = id;
                if (joinable)
                    onlineSession.JoinSessionById(id, _joinPasswordInput != null ? _joinPasswordInput.text : "");
            }, joinable ? buttonColor : new Color(1f, 0.3f, 0.3f, 0.15f));

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
            () => onlineSession.JoinOnlineGame(_codeInput.text), accentColor);

        Label("l6", "Para partidas privadas que te hayan pasado por chat", _content,
            new Vector2(0f, -90f), new Vector2(w, 22f), 12, TextAnchor.MiddleCenter,
            new Color(1f, 1f, 1f, 0.6f));
    }

    // ---------- Pestana OPCIONES ----------

    private void BuildOptionsTab()
    {
        float w = _content.sizeDelta.x;

        if (_resIndex < 0)
        {
            _resIndex = GameSettings.IndexOfCurrent();
            _fullscreen = GameSettings.SavedFullscreen;
        }

        var list = GameSettings.Resolutions;

        Label("l7", "Resolucion", _content, new Vector2(0f, 70f), new Vector2(w, 24f),
            16, TextAnchor.MiddleCenter, Color.white);

        Text resLabel = Label("res", "", _content, new Vector2(0f, 34f), new Vector2(220f, 30f),
            16, TextAnchor.MiddleCenter, Color.white);
        resLabel.text = list[Mathf.Clamp(_resIndex, 0, list.Count - 1)].x + " x " +
                        list[Mathf.Clamp(_resIndex, 0, list.Count - 1)].y;

        Button("<", _content, new Vector2(-140f, 34f), new Vector2(40f, 30f), () =>
        {
            _resIndex = (_resIndex - 1 + list.Count) % list.Count;
            resLabel.text = list[_resIndex].x + " x " + list[_resIndex].y;
        });

        Button(">", _content, new Vector2(140f, 34f), new Vector2(40f, 30f), () =>
        {
            _resIndex = (_resIndex + 1) % list.Count;
            resLabel.text = list[_resIndex].x + " x " + list[_resIndex].y;
        });

        Text fsLabel = null;
        fsLabel = Label("fs", FullscreenText(), _content, new Vector2(0f, -10f),
            new Vector2(w, 24f), 14, TextAnchor.MiddleCenter, Color.white);

        Button("Cambiar", _content, new Vector2(0f, -44f), new Vector2(160f, 30f), () =>
        {
            _fullscreen = !_fullscreen;
            fsLabel.text = FullscreenText();
        });

        Button("APLICAR", _content, new Vector2(0f, -100f), new Vector2(200f, 38f),
            () => GameSettings.Apply(list[_resIndex], _fullscreen), accentColor);
    }

    private string FullscreenText() => _fullscreen ? "Pantalla completa" : "En ventana";

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
        UnityEngine.Events.UnityAction onClick, Color? color = null)
    {
        RectTransform rt = NewRect("Btn_" + caption, parent, size);
        rt.anchoredPosition = pos;

        Image bg = AddImage(rt, color ?? buttonColor);
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(onClick);

        Text label = Label("Texto", caption, rt, Vector2.zero, size, 15,
            TextAnchor.MiddleCenter, Color.white);
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 9;
        label.resizeTextMaxSize = 15;

        return button;
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
}
