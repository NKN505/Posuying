using UnityEngine;

public class Inventory : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        public ItemData item;
        public int count;

        public bool IsEmpty => item == null || count <= 0;
        public void Clear() { item = null; count = 0; }
    }

    [Header("Configuracion")]
    [Tooltip("Huecos del cinturon (los que se ven abajo y se eligen con 1..N)")]
    public int hotbarSize = 5;
    [Tooltip("Huecos de la mochila (se ven al abrir el inventario con TAB)")]
    public int backpackSize = 24;

    public Slot[] slots;
    public int selectedIndex = 0;

    [Header("Teclas")]
    public KeyCode useKey = KeyCode.F;

    // Aviso para que el HUD se refresque cuando cambia algo
    public System.Action OnInventoryChanged;

    private Character _character;

    // Los primeros huecos del array son el cinturon; el resto, la mochila
    public int TotalSlots => hotbarSize + backpackSize;
    public bool IsHotbarSlot(int index) => index >= 0 && index < hotbarSize;

    void Awake()
    {
        _character = GetComponent<Character>();
        EnsureSlots();
    }

    private void EnsureSlots()
    {
        int total = TotalSlots;
        if (slots != null && slots.Length == total) return;

        Slot[] old = slots;
        slots = new Slot[total];
        for (int i = 0; i < total; i++)
            slots[i] = (old != null && i < old.Length && old[i] != null) ? old[i] : new Slot();
    }

    void Update()
    {
        // Con el inventario o el menu de red abiertos, el raton es para la interfaz
        if (UIState.BlocksGameplay) return;

        // Seleccion por teclas numericas 1..N (solo cinturon)
        for (int i = 0; i < hotbarSize; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                SelectSlot(i);
        }

        // Seleccion por rueda del raton (mouseScrollDelta no depende del Input Manager)
        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f) SelectSlot(selectedIndex - 1);
        else if (scroll < 0f) SelectSlot(selectedIndex + 1);

        // Usar el objeto seleccionado
        if (Input.GetKeyDown(useKey))
            UseSelected();
    }

    // La seleccion solo se mueve por el cinturon
    public void SelectSlot(int index)
    {
        selectedIndex = ((index % hotbarSize) + hotbarSize) % hotbarSize;
        OnInventoryChanged?.Invoke();
    }

    // Para que la interfaz avise cuando mueve objetos entre huecos
    public void NotifyChanged() => OnInventoryChanged?.Invoke();

    // Anade objetos al inventario. Devuelve cuantos NO cupieron (0 = todo entro).
    public int AddItem(ItemData item, int amount = 1)
    {
        if (item == null || amount <= 0) return amount;

        // 1) Rellenar stacks existentes del mismo item
        for (int i = 0; i < slots.Length && amount > 0; i++)
        {
            if (!slots[i].IsEmpty && slots[i].item == item && slots[i].count < item.maxStack)
            {
                int space = item.maxStack - slots[i].count;
                int toAdd = Mathf.Min(space, amount);
                slots[i].count += toAdd;
                amount -= toAdd;
            }
        }

        // 2) Ocupar huecos vacios
        for (int i = 0; i < slots.Length && amount > 0; i++)
        {
            if (slots[i].IsEmpty)
            {
                int toAdd = Mathf.Min(item.maxStack, amount);
                slots[i].item = item;
                slots[i].count = toAdd;
                amount -= toAdd;
            }
        }

        OnInventoryChanged?.Invoke();
        return amount; // lo que sobro (inventario lleno)
    }

    public void UseSelected()
    {
        Slot slot = slots[selectedIndex];
        if (slot.IsEmpty) return;

        switch (slot.item.type)
        {
            case ItemType.Consumable:
                if (_character != null && slot.item.healAmount > 0f)
                {
                    _character.RequestHeal(slot.item.healAmount);
                    slot.count--;
                    if (slot.count <= 0) slot.Clear();
                    OnInventoryChanged?.Invoke();
                }
                break;

            case ItemType.Weapon:
                // De momento solo lo anunciamos; equipar el arma se puede ampliar mas adelante
                Debug.Log("Arma seleccionada: " + slot.item.itemName);
                break;

            case ItemType.Generic:
                // Objetos de mision (llaves, piezas): por ahora no hacen nada al usar
                break;
        }
    }

    public Slot GetSelectedSlot() => slots[selectedIndex];
}
