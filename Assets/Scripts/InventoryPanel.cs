using UnityEngine;
using UnityEngine.UI;

// Inventario amplio que se abre con TAB (estilo Minecraft):
// arriba la mochila en cuadricula, abajo el cinturon, y se mueven objetos
// cogiendolos con un clic y soltandolos con otro clic.
//
// Construye toda la interfaz por codigo: basta con poner este componente
// en un objeto del Canvas y asignarle (o dejar que encuentre) el Canvas.
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

    private RectTransform _root;
    private Image[] _bgs;
    private Image[] _icons;
    private Text[] _counts;

    // Objeto "en la mano" mientras lo movemos de un hueco a otro
    private ItemData _heldItem;
    private int _heldCount;
    private int _heldFrom = -1;   // hueco del que salio, para poder devolverlo
    private RectTransform _heldRoot;
    private Image _heldIcon;
    private Text _heldCountText;

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

        // No abrir el inventario si el menu de red esta delante
        if (Input.GetKeyDown(toggleKey) && (!UIState.NetMenuOpen || _open))
            SetOpen(!_open);

        if (_open && _heldItem != null)
            _heldRoot.position = Input.mousePosition;
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
        BuildHeldIcon();

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

        if (!open)
        {
            // Si cerramos con algo en la mano, lo devolvemos al inventario
            ReturnHeldToInventory();
            _heldRoot.gameObject.SetActive(false);
        }
        else
        {
            _root.SetAsLastSibling();
            _heldRoot.SetAsLastSibling();
            Refresh();   // por si quedo algo en la mano de la ultima vez
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
        float contentHeight = titleH + rows * slotSize + (rows - 1) * spacing + gapH + slotSize;

        // Panel de fondo, centrado en pantalla
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

        // Titulo
        CreateLabel("Titulo", "MOCHILA", _root, new Vector2(0f, topY - titleH / 2f),
            new Vector2(contentWidth, titleH), 18, TextAnchor.MiddleCenter, Color.white);

        // Cuadricula de la mochila (indices hotbar..total-1)
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

            CreateSlot(slotIndex, pos, slotColor, "");
        }

        // Fila del cinturon, abajo
        float hotbarY = gridTop - rows * slotSize - (rows - 1) * spacing - gapH - slotSize / 2f;
        float hotbarStartX = -hotbarWidth / 2f + slotSize / 2f;

        CreateLabel("TituloCinturon", "CINTURON", _root,
            new Vector2(0f, hotbarY + slotSize / 2f + 10f),
            new Vector2(contentWidth, 20f), 13, TextAnchor.MiddleCenter,
            new Color(1f, 1f, 1f, 0.6f));

        for (int i = 0; i < hotbar; i++)
        {
            Vector2 pos = new Vector2(hotbarStartX + i * (slotSize + spacing), hotbarY);
            CreateSlot(i, pos, hotbarSlotColor, (i + 1).ToString());
        }
    }

    private void CreateSlot(int slotIndex, Vector2 position, Color background, string keyLabel)
    {
        GameObject slotGO = new GameObject("Slot" + slotIndex, typeof(RectTransform), typeof(Image));
        RectTransform rt = slotGO.GetComponent<RectTransform>();
        rt.SetParent(_root, false);
        rt.sizeDelta = new Vector2(slotSize, slotSize);
        rt.anchoredPosition = position;

        Image bg = slotGO.GetComponent<Image>();
        bg.color = background;
        bg.raycastTarget = true;          // necesario para detectar el clic
        _bgs[slotIndex] = bg;

        var button = slotGO.AddComponent<InventorySlotButton>();
        button.index = slotIndex;
        button.onClick = OnSlotClicked;

        // Icono
        GameObject iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        RectTransform irt = iconGO.GetComponent<RectTransform>();
        irt.SetParent(rt, false);
        irt.sizeDelta = new Vector2(slotSize * 0.8f, slotSize * 0.8f);
        Image icon = iconGO.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;       // que el clic llegue al fondo del hueco
        icon.enabled = false;
        _icons[slotIndex] = icon;

        // Cantidad
        _counts[slotIndex] = CreateLabel("Count", "", rt, Vector2.zero,
            new Vector2(slotSize - 4f, slotSize - 4f), 16, TextAnchor.LowerRight, Color.white);

        // Numero de tecla (solo cinturon)
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

    private void BuildHeldIcon()
    {
        GameObject go = new GameObject("ObjetoEnMano", typeof(RectTransform), typeof(Image));
        _heldRoot = go.GetComponent<RectTransform>();
        _heldRoot.SetParent(canvas.transform, false);
        _heldRoot.sizeDelta = new Vector2(slotSize * 0.8f, slotSize * 0.8f);

        _heldIcon = go.GetComponent<Image>();
        _heldIcon.preserveAspect = true;
        _heldIcon.raycastTarget = false;

        _heldCountText = CreateLabel("Count", "", _heldRoot, Vector2.zero,
            new Vector2(slotSize * 0.8f, slotSize * 0.8f), 16, TextAnchor.LowerRight, Color.white);

        _heldRoot.gameObject.SetActive(false);
    }

    // ---------- Mover objetos ----------

    private void OnSlotClicked(int index)
    {
        Inventory.Slot slot = inventory.slots[index];

        if (_heldItem == null)
        {
            // Mano vacia: cogemos lo que haya
            if (slot.IsEmpty) return;

            _heldItem = slot.item;
            _heldCount = slot.count;
            _heldFrom = index;
            slot.Clear();
        }
        else if (slot.IsEmpty)
        {
            // Soltar en hueco vacio
            slot.item = _heldItem;
            slot.count = _heldCount;
            ClearHeld();
        }
        else if (slot.item == _heldItem && slot.count < slot.item.maxStack)
        {
            // Mismo objeto: apilar lo que quepa
            int space = slot.item.maxStack - slot.count;
            int moved = Mathf.Min(space, _heldCount);
            slot.count += moved;
            _heldCount -= moved;
            if (_heldCount <= 0) ClearHeld();
        }
        else
        {
            // Objeto distinto: intercambiar
            ItemData tmpItem = slot.item;
            int tmpCount = slot.count;
            slot.item = _heldItem;
            slot.count = _heldCount;
            _heldItem = tmpItem;
            _heldCount = tmpCount;
            _heldFrom = index;   // si lo devolvemos, que vuelva aqui
        }

        inventory.NotifyChanged();
    }

    private void ClearHeld()
    {
        _heldItem = null;
        _heldCount = 0;
        _heldFrom = -1;
    }

    // Devuelve el objeto que llevamos en la mano SIN perderlo nunca:
    // primero a su hueco de origen, si no a cualquier hueco libre, y si
    // tampoco cabe se queda en la mano (reaparece al volver a abrir).
    private void ReturnHeldToInventory()
    {
        if (_heldItem == null) return;

        if (_heldFrom >= 0 && _heldFrom < inventory.slots.Length)
        {
            Inventory.Slot origin = inventory.slots[_heldFrom];

            if (origin.IsEmpty)
            {
                origin.item = _heldItem;
                origin.count = _heldCount;
                ClearHeld();
                inventory.NotifyChanged();
                return;
            }

            // El hueco original ahora tiene lo mismo: intentamos apilar ahi
            if (origin.item == _heldItem && origin.count < origin.item.maxStack)
            {
                int space = origin.item.maxStack - origin.count;
                int moved = Mathf.Min(space, _heldCount);
                origin.count += moved;
                _heldCount -= moved;
                if (_heldCount <= 0)
                {
                    ClearHeld();
                    inventory.NotifyChanged();
                    return;
                }
            }
        }

        // Cualquier otro hueco libre
        int leftover = inventory.AddItem(_heldItem, _heldCount);

        if (leftover <= 0)
        {
            ClearHeld();
        }
        else
        {
            // No cabe: lo conservamos en la mano en lugar de tirarlo
            _heldCount = leftover;
            Debug.LogWarning("Inventario lleno: '" + _heldItem.itemName +
                             "' se queda en la mano hasta que hagas hueco.");
        }

        inventory.NotifyChanged();
    }

    private void Refresh()
    {
        if (_bgs == null) return;

        for (int i = 0; i < _bgs.Length; i++)
        {
            bool isHotbar = inventory.IsHotbarSlot(i);
            bool isSelected = isHotbar && i == inventory.selectedIndex;
            _bgs[i].color = isSelected ? selectedColor : (isHotbar ? hotbarSlotColor : slotColor);

            Inventory.Slot slot = inventory.slots[i];
            if (slot != null && !slot.IsEmpty)
            {
                _icons[i].enabled = slot.item.icon != null;
                _icons[i].sprite = slot.item.icon;
                _counts[i].text = slot.count > 1 ? slot.count.ToString() : "";
            }
            else
            {
                _icons[i].enabled = false;
                _counts[i].text = "";
            }
        }

        // Objeto que llevamos en la mano
        bool holding = _heldItem != null;
        _heldRoot.gameObject.SetActive(holding);
        if (holding)
        {
            _heldIcon.enabled = _heldItem.icon != null;
            _heldIcon.sprite = _heldItem.icon;
            _heldCountText.text = _heldCount > 1 ? _heldCount.ToString() : "";
            _heldRoot.SetAsLastSibling();
        }
    }
}
