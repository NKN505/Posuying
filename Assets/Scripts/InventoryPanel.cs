using UnityEngine;
using UnityEngine.UI;

// Inventario amplio que se abre con TAB: arriba la mochila en cuadricula,
// abajo el cinturon.
//
// Como el contenido lo manda el servidor, mover objetos es en dos pasos:
// un clic elige el hueco de origen (se marca) y el siguiente clic elige el
// destino. El servidor decide si se mueve, se apila o se intercambia.
public class InventoryPanel : MonoBehaviour
{
    [Header("Referencias")]
    public Inventory inventory;              // se busca solo (jugador local)
    public Canvas canvas;                    // se busca solo si se deja vacio

    [Header("Tecla")]
    public KeyCode toggleKey = KeyCode.Tab;

    [Header("Estilo")]
    public int columns = 6;
    public float slotSize = 56f;
    public float spacing = 6f;
    public float padding = 16f;
    public Color panelColor = new Color(0f, 0f, 0f, 0.85f);
    public Color slotColor = new Color(1f, 1f, 1f, 0.12f);
    public Color hotbarSlotColor = new Color(1f, 1f, 1f, 0.22f);
    public Color selectedColor = new Color(1f, 1f, 0.3f, 0.6f);
    public Color sourceColor = new Color(0.3f, 0.8f, 1f, 0.7f);

    private RectTransform _root;
    private Image[] _bgs;
    private Image[] _icons;
    private Text[] _counts;
    private Text _hint;

    private int _sourceIndex = -1;   // hueco elegido a la espera de destino
    private bool _built;
    private bool _open;
    private Font _font;

    void Update()
    {
        if (!_built)
        {
            TryBuild();
            return;
        }

        if (Input.GetKeyDown(toggleKey) && (!UIState.NetMenuOpen || _open))
            SetOpen(!_open);
    }

    void OnDestroy()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= Refresh;
    }

    private void TryBuild()
    {
        if (inventory == null)
            inventory = NetworkPlayer.LocalInventory;   // aparece al conectar

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();

        if (inventory == null || canvas == null) return;

        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        BuildPanel();

        inventory.OnInventoryChanged += Refresh;
        Refresh();

        _built = true;
        SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        _open = open;
        _root.gameObject.SetActive(open);
        UIState.InventoryOpen = open;

        _sourceIndex = -1;   // al abrir o cerrar, ninguna seleccion pendiente

        if (open)
        {
            _root.SetAsLastSibling();
            Refresh();
        }
    }

    // ---------- Construccion de la interfaz ----------

    private void BuildPanel()
    {
        int backpack = inventory.backpackSize;
        int hotbar = inventory.hotbarSize;
        int rows = Mathf.CeilToInt(backpack / (float)columns);

        float gridWidth = columns * slotSize + (columns - 1) * spacing;
        float hotbarWidth = hotbar * slotSize + (hotbar - 1) * spacing;
        float contentWidth = Mathf.Max(gridWidth, hotbarWidth);

        float titleH = 28f;
        float gapH = 22f;
        float hintH = 22f;
        float contentHeight = titleH + rows * slotSize + (rows - 1) * spacing
                              + gapH + slotSize + hintH;

        GameObject rootGO = new GameObject("PanelInventario", typeof(RectTransform), typeof(Image));
        _root = rootGO.GetComponent<RectTransform>();
        _root.SetParent(canvas.transform, false);
        _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.anchoredPosition = Vector2.zero;
        _root.sizeDelta = new Vector2(contentWidth + padding * 2, contentHeight + padding * 2);
        rootGO.GetComponent<Image>().color = panelColor;

        int total = inventory.TotalSlots;
        _bgs = new Image[total];
        _icons = new Image[total];
        _counts = new Text[total];

        float topY = contentHeight / 2f;

        CreateLabel("Titulo", "MOCHILA", _root, new Vector2(0f, topY - titleH / 2f),
            new Vector2(contentWidth, titleH), 18, TextAnchor.MiddleCenter, Color.white);

        // Cuadricula de la mochila
        float gridTop = topY - titleH;
        float gridStartX = -gridWidth / 2f + slotSize / 2f;

        for (int i = 0; i < backpack; i++)
        {
            int slotIndex = hotbar + i;
            int col = i % columns;
            int row = i / columns;

            Vector2 pos = new Vector2(
                gridStartX + col * (slotSize + spacing),
                gridTop - slotSize / 2f - row * (slotSize + spacing));

            CreateSlot(slotIndex, pos, "");
        }

        // Fila del cinturon
        float hotbarY = gridTop - rows * slotSize - (rows - 1) * spacing - gapH - slotSize / 2f;
        float hotbarStartX = -hotbarWidth / 2f + slotSize / 2f;

        CreateLabel("TituloCinturon", "CINTURON", _root,
            new Vector2(0f, hotbarY + slotSize / 2f + 10f),
            new Vector2(contentWidth, 20f), 13, TextAnchor.MiddleCenter,
            new Color(1f, 1f, 1f, 0.6f));

        for (int i = 0; i < hotbar; i++)
        {
            Vector2 pos = new Vector2(hotbarStartX + i * (slotSize + spacing), hotbarY);
            CreateSlot(i, pos, (i + 1).ToString());
        }

        // Linea de ayuda abajo del panel
        _hint = CreateLabel("Ayuda", "", _root,
            new Vector2(0f, -contentHeight / 2f + hintH / 2f),
            new Vector2(contentWidth, hintH), 12, TextAnchor.MiddleCenter,
            new Color(1f, 1f, 1f, 0.6f));
    }

    private void CreateSlot(int slotIndex, Vector2 position, string keyLabel)
    {
        GameObject slotGO = new GameObject("Slot" + slotIndex, typeof(RectTransform), typeof(Image));
        RectTransform rt = slotGO.GetComponent<RectTransform>();
        rt.SetParent(_root, false);
        rt.sizeDelta = new Vector2(slotSize, slotSize);
        rt.anchoredPosition = position;

        Image bg = slotGO.GetComponent<Image>();
        bg.raycastTarget = true;          // necesario para detectar el clic
        _bgs[slotIndex] = bg;

        var button = slotGO.AddComponent<InventorySlotButton>();
        button.index = slotIndex;
        button.onClick = OnSlotClicked;

        GameObject iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        RectTransform irt = iconGO.GetComponent<RectTransform>();
        irt.SetParent(rt, false);
        irt.sizeDelta = new Vector2(slotSize * 0.8f, slotSize * 0.8f);
        Image icon = iconGO.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;       // que el clic llegue al fondo del hueco
        icon.enabled = false;
        _icons[slotIndex] = icon;

        _counts[slotIndex] = CreateLabel("Count", "", rt, Vector2.zero,
            new Vector2(slotSize - 4f, slotSize - 4f), 16, TextAnchor.LowerRight, Color.white);

        if (!string.IsNullOrEmpty(keyLabel))
            CreateLabel("Key", keyLabel, rt, Vector2.zero, new Vector2(slotSize - 4f, slotSize - 4f),
                12, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.6f));
    }

    private Text CreateLabel(string name, string content, Transform parent, Vector2 position,
        Vector2 size, int fontSize, TextAnchor anchor, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;

        Text text = go.GetComponent<Text>();
        text.font = _font;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        text.text = content;
        return text;
    }

    // ---------- Mover objetos ----------

    private void OnSlotClicked(int index)
    {
        if (_sourceIndex < 0)
        {
            // Primer clic: elegimos de donde sacar (si hay algo)
            if (inventory.GetSlot(index).IsEmpty) return;
            _sourceIndex = index;
        }
        else if (_sourceIndex == index)
        {
            _sourceIndex = -1;   // clic en el mismo hueco: cancelar
        }
        else
        {
            // Segundo clic: el servidor decide si mover, apilar o intercambiar
            inventory.MoveSlotServerRpc(_sourceIndex, index);
            _sourceIndex = -1;
        }

        Refresh();
    }

    private void Refresh()
    {
        if (_bgs == null || inventory == null) return;

        for (int i = 0; i < _bgs.Length; i++)
        {
            bool isHotbar = inventory.IsHotbarSlot(i);

            if (i == _sourceIndex)
                _bgs[i].color = sourceColor;
            else if (isHotbar && i == inventory.SelectedIndex)
                _bgs[i].color = selectedColor;
            else
                _bgs[i].color = isHotbar ? hotbarSlotColor : slotColor;

            Inventory.SlotData slot = inventory.GetSlot(i);
            ItemData item = inventory.GetItemAt(i);

            if (!slot.IsEmpty && item != null)
            {
                _icons[i].enabled = item.icon != null;
                _icons[i].sprite = item.icon;
                _counts[i].text = slot.count > 1 ? slot.count.ToString() : "";
            }
            else
            {
                _icons[i].enabled = false;
                _counts[i].text = "";
            }
        }

        if (_hint != null)
        {
            _hint.text = _sourceIndex < 0
                ? "Clic en un objeto para cogerlo"
                : "Clic en otro hueco para moverlo (o en el mismo para cancelar)";
        }
    }
}
