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
    public int slotCount = 5;
    public Slot[] slots;
    public int selectedIndex = 0;

    [Header("Teclas")]
    public KeyCode useKey = KeyCode.F;

    // Aviso para que el HUD se refresque cuando cambia algo
    public System.Action OnInventoryChanged;

    private Character _character;

    void Awake()
    {
        _character = GetComponent<Character>();

        if (slots == null || slots.Length == 0)
        {
            slots = new Slot[slotCount];
            for (int i = 0; i < slotCount; i++)
                slots[i] = new Slot();
        }
        else
        {
            slotCount = slots.Length;
        }
    }

    void Update()
    {
        // Seleccion por teclas numericas 1..N
        for (int i = 0; i < slotCount; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                SelectSlot(i);
        }

        // Seleccion por rueda del raton (mouseScrollDelta no depende del Input Manager)
        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f) SelectSlot((selectedIndex - 1 + slotCount) % slotCount);
        else if (scroll < 0f) SelectSlot((selectedIndex + 1) % slotCount);

        // Usar el objeto seleccionado
        if (Input.GetKeyDown(useKey))
            UseSelected();
    }

    public void SelectSlot(int index)
    {
        selectedIndex = ((index % slotCount) + slotCount) % slotCount;
        OnInventoryChanged?.Invoke();
    }

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
