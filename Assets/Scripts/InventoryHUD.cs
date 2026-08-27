using UnityEngine;
using UnityEngine.UI;

// Dibuja la hotbar automaticamente bajo un contenedor UI.
// Solo hay que asignar el Inventory y un objeto UI vacio (container).
public class InventoryHUD : MonoBehaviour
{
    public Inventory inventory;
    public RectTransform container;   // objeto UI vacio dentro del Canvas

    [Header("Estilo")]
    public float slotSize = 64f;
    public float spacing = 6f;
    public Color normalColor = new Color(0f, 0f, 0f, 0.5f);
    public Color selectedColor = new Color(1f, 1f, 0.3f, 0.85f);

    private Image[] _bgs;
    private Image[] _icons;
    private Text[] _counts;

    private bool _built = false;

    void Update()
    {
        if (_built) return;

        // En red el inventario llega con el jugador local, que aparece al conectar
        if (inventory == null)
            inventory = NetworkPlayer.LocalInventory;

        if (inventory == null || container == null) return;

        BuildSlots();
        inventory.OnInventoryChanged += Refresh;
        Refresh();
        _built = true;
    }

    void OnDestroy()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= Refresh;
    }

    // El panel del inventario decide que hacer: coger, soltar, apilar o cambiar
    private void OnHotbarSlotClicked(int index)
    {
        if (_panel == null)
            _panel = FindFirstObjectByType<InventoryPanel>();

        if (_panel != null)
            _panel.HandleSlotClick(index);
    }

    private InventoryPanel _panel;

    void BuildSlots()
    {
        int n = inventory.hotbarSize;   // abajo solo se ve el cinturon
        _bgs = new Image[n];
        _icons = new Image[n];
        _counts = new Text[n];

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        float totalWidth = n * slotSize + (n - 1) * spacing;
        float startX = -totalWidth / 2f + slotSize / 2f;

        for (int i = 0; i < n; i++)
        {
            // Fondo del slot
            GameObject slotGO = new GameObject("Slot" + i, typeof(RectTransform), typeof(Image));
            RectTransform rt = slotGO.GetComponent<RectTransform>();
            rt.SetParent(container, false);
            rt.sizeDelta = new Vector2(slotSize, slotSize);
            rt.anchoredPosition = new Vector2(startX + i * (slotSize + spacing), 0f);
            Image bg = slotGO.GetComponent<Image>();
            bg.color = normalColor;
            bg.raycastTarget = true;
            _bgs[i] = bg;

            // Con el inventario abierto, estos huecos tambien se pueden pulsar
            // para mover objetos entre la mochila y el cinturon.
            var button = slotGO.AddComponent<InventorySlotButton>();
            button.index = i;
            button.onClick = OnHotbarSlotClicked;

            // Icono del objeto
            GameObject iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            RectTransform irt = iconGO.GetComponent<RectTransform>();
            irt.SetParent(rt, false);
            irt.sizeDelta = new Vector2(slotSize * 0.8f, slotSize * 0.8f);
            irt.anchoredPosition = Vector2.zero;
            Image icon = iconGO.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;   // que el clic llegue al fondo del hueco
            icon.enabled = false;
            _icons[i] = icon;

            // Contador de cantidad
            GameObject cGO = new GameObject("Count", typeof(RectTransform), typeof(Text));
            RectTransform crt = cGO.GetComponent<RectTransform>();
            crt.SetParent(rt, false);
            crt.sizeDelta = new Vector2(slotSize, slotSize);
            crt.anchoredPosition = Vector2.zero;
            Text count = cGO.GetComponent<Text>();
            count.font = font;
            count.fontSize = 18;
            count.fontStyle = FontStyle.Bold;
            count.alignment = TextAnchor.LowerRight;
            count.color = Color.white;
            count.raycastTarget = false;
            count.text = "";
            _counts[i] = count;

            // Numero de tecla (esquina superior izquierda)
            GameObject kGO = new GameObject("Key", typeof(RectTransform), typeof(Text));
            RectTransform krt = kGO.GetComponent<RectTransform>();
            krt.SetParent(rt, false);
            krt.sizeDelta = new Vector2(slotSize, slotSize);
            krt.anchoredPosition = Vector2.zero;
            Text key = kGO.GetComponent<Text>();
            key.font = font;
            key.fontSize = 14;
            key.alignment = TextAnchor.UpperLeft;
            key.color = new Color(1f, 1f, 1f, 0.6f);
            key.raycastTarget = false;
            key.text = (i + 1).ToString();
        }
    }

    void Refresh()
    {
        for (int i = 0; i < _bgs.Length; i++)
        {
            _bgs[i].color = (i == inventory.SelectedIndex) ? selectedColor : normalColor;

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
    }
}
