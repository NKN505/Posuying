using System.Collections.Generic;
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

    // Para que el menu de Escape pueda abrir las opciones estando en partida
    public static MainMenuUI Instance { get; private set; }
    public bool OptionsOverlayOpen { get; private set; }

    private readonly List<GameObject> _tabButtons = new List<GameObject>();

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
    private enum OptionsTab { General, Graficos, Audio }
    private OptionsTab _optionsTab = OptionsTab.General;

    private int _resIndex = -1;
    private bool _fullscreen = true;

    void Update()
    {
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

        // Se ve fuera de partida, o dentro si han abierto las opciones desde Escape
        bool show = !inGame || OptionsOverlayOpen;

        if (_root.gameObject.activeSelf != show)
            _root.gameObject.SetActive(show);

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
        OptionsOverlayOpen = true;
        ShowTab(Tab.Opciones);
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
        // En partida solo tienen sentido las opciones: crear o buscar otra
        // partida desde aqui no aplica
        if (OptionsOverlayOpen) tab = Tab.Opciones;

        _tab = tab;

        foreach (var button in _tabButtons)
            if (button != null) button.SetActive(!OptionsOverlayOpen);

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

            bool needsPassword = info.HasPassword;

            Button(text, _content, new Vector2(0f, y), new Vector2(w, 36f), () =>
            {
                _selectedSessionId = id;
                if (!joinable) return;

                string typed = _joinPasswordInput != null ? _joinPasswordInput.text : "";

                // Intentar entrar sin la contrasena deja la red en mal estado y
                // luego ya no se puede entrar en ninguna partida. Mejor no intentarlo.
                if (needsPassword && string.IsNullOrWhiteSpace(typed))
                {
                    _status.text = "Esa partida pide contrasena: escribela abajo y vuelve a pulsar";
                    return;
                }

                onlineSession.JoinSessionById(id, typed);
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
        GameSettings.Load();

        if (_resIndex < 0)
        {
            _resIndex = GameSettings.IndexOfCurrentResolution();
            _fullscreen = GameSettings.SavedFullscreen;
        }

        float w = _content.sizeDelta.x;
        float top = _content.sizeDelta.y / 2f;

        // Sub-pestanas de categoria
        string[] names = { "GENERAL", "GRAFICOS", "AUDIO" };
        float tabW = 130f;

        for (int i = 0; i < names.Length; i++)
        {
            OptionsTab tab = (OptionsTab)i;
            float x = -tabW * 1.05f + i * tabW * 1.05f;
            Color color = _optionsTab == tab ? accentColor : buttonColor;

            Button(names[i], _content, new Vector2(x, top - 16f), new Vector2(tabW, 28f),
                () => { _optionsTab = tab; ShowTab(Tab.Opciones); }, color);
        }

        switch (_optionsTab)
        {
            case OptionsTab.General: BuildGeneralOptions(); break;
            case OptionsTab.Graficos: BuildGraphicsOptions(); break;
            case OptionsTab.Audio: BuildAudioOptions(); break;
        }

        // En partida el boton comparte fila con el de volver
        float applyX = OptionsOverlayOpen ? -110f : 0f;

        Button("APLICAR", _content, new Vector2(applyX, -top + 24f), new Vector2(200f, 34f), () =>
        {
            GameSettings.ApplyResolution(GameSettings.Resolutions[_resIndex], _fullscreen);
            GameSettings.SaveAndApply();
        }, accentColor);

        if (OptionsOverlayOpen)
        {
            Button("VOLVER AL JUEGO", _content, new Vector2(110f, -top + 24f),
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

        StepRow(1, "Sensibilidad raton", () => GameSettings.MouseSensitivity.ToString("0.00"),
            () => GameSettings.MouseSensitivity = Mathf.Max(0.25f, GameSettings.MouseSensitivity - 0.25f),
            () => GameSettings.MouseSensitivity = Mathf.Min(5f, GameSettings.MouseSensitivity + 0.25f));

        ToggleRow(2, "Invertir eje Y", () => GameSettings.OnOff(GameSettings.InvertY),
            () => GameSettings.InvertY = !GameSettings.InvertY);

        StepRow(3, "Campo de vision", () => Mathf.RoundToInt(GameSettings.FieldOfView) + " grados",
            () => GameSettings.FieldOfView = Mathf.Max(60f, GameSettings.FieldOfView - 5f),
            () => GameSettings.FieldOfView = Mathf.Min(110f, GameSettings.FieldOfView + 5f));
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

    private void BuildAudioOptions()
    {
        StepRow(0, "Volumen general", () => GameSettings.Percent(GameSettings.MasterVolume),
            () => GameSettings.MasterVolume = Mathf.Max(0f, GameSettings.MasterVolume - 0.1f),
            () => GameSettings.MasterVolume = Mathf.Min(1f, GameSettings.MasterVolume + 0.1f));

        StepRow(1, "Musica", () => GameSettings.Percent(GameSettings.MusicVolume),
            () => GameSettings.MusicVolume = Mathf.Max(0f, GameSettings.MusicVolume - 0.1f),
            () => GameSettings.MusicVolume = Mathf.Min(1f, GameSettings.MusicVolume + 0.1f));

        StepRow(2, "Efectos", () => GameSettings.Percent(GameSettings.SfxVolume),
            () => GameSettings.SfxVolume = Mathf.Max(0f, GameSettings.SfxVolume - 0.1f),
            () => GameSettings.SfxVolume = Mathf.Min(1f, GameSettings.SfxVolume + 0.1f));

        Label("aviso", "El juego aun no tiene sonidos: musica y efectos\nquedan guardados para cuando los haya.",
            _content, new Vector2(0f, RowY(4)), new Vector2(_content.sizeDelta.x, 40f),
            12, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.55f));
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
