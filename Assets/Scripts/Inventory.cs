using Unity.Netcode;
using UnityEngine;

// Inventario del jugador, sincronizado por red.
//
// El SERVIDOR es el dueno del contenido (igual que la vida): los clientes piden
// cambios con RPCs y el servidor decide. Asi los dos jugadores ven lo mismo,
// nadie puede duplicar objetos, y el inventario sobrevive a un cambio de host.
//
// Los huecos son un unico array: los primeros 'hotbarSize' son el cinturon,
// el resto la mochila.
public class Inventory : NetworkBehaviour
{
    // Lo que viaja por red de cada hueco: un id de objeto y una cantidad
    public struct SlotData : INetworkSerializable, System.IEquatable<SlotData>
    {
        public int itemId;
        public int count;

        public bool IsEmpty => itemId < 0 || count <= 0;

        public static SlotData Empty => new SlotData { itemId = ItemDatabase.EmptyId, count = 0 };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref itemId);
            serializer.SerializeValue(ref count);
        }

        public bool Equals(SlotData other) => itemId == other.itemId && count == other.count;
    }

    [Header("Catalogo")]
    [Tooltip("Necesario para traducir ids de red a objetos")]
    public ItemDatabase database;

    [Header("Configuracion")]
    [Tooltip("Huecos del cinturon (los que se ven abajo y se eligen con 1..N). " +
             "Son 4 para que encajen con la cruceta del mando.")]
    public int hotbarSize = 4;
    [Tooltip("Huecos de la mochila (se ven al abrir el inventario con TAB)")]
    public int backpackSize = 24;

    [Header("Teclas")]
    public KeyCode useKey = KeyCode.F;

    // Contenido: solo lo escribe el servidor
    private readonly NetworkList<SlotData> netSlots = new NetworkList<SlotData>();

    // Hueco elegido del cinturon: lo escribe su dueno (no hace falta molestar al servidor)
    private readonly NetworkVariable<int> netSelected = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Aviso para que la interfaz se refresque
    public System.Action OnInventoryChanged;

    private Character _character;

    public int TotalSlots => hotbarSize + backpackSize;
    public bool IsHotbarSlot(int index) => index >= 0 && index < hotbarSize;
    public int SelectedIndex => netSelected.Value;
    public int SlotCount => netSlots.Count;

    void Awake()
    {
        _character = GetComponent<Character>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // El servidor crea los huecos vacios una sola vez
            for (int i = netSlots.Count; i < TotalSlots; i++)
                netSlots.Add(SlotData.Empty);
        }

        netSlots.OnListChanged += OnSlotsChanged;
        netSelected.OnValueChanged += OnSelectedChanged;

        OnInventoryChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        netSlots.OnListChanged -= OnSlotsChanged;
        netSelected.OnValueChanged -= OnSelectedChanged;
    }

    private void OnSlotsChanged(NetworkListEvent<SlotData> _) => OnInventoryChanged?.Invoke();
    private void OnSelectedChanged(int _, int __) => OnInventoryChanged?.Invoke();

    void Update()
    {
        // Solo el dueno maneja su inventario, y no mientras hay una ventana abierta
        if (!IsOwner || UIState.BlocksGameplay) return;

        for (int i = 0; i < hotbarSize; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                SelectSlot(i);
        }

        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f) SelectSlot(SelectedIndex - 1);
        else if (scroll < 0f) SelectSlot(SelectedIndex + 1);

        if (Input.GetKeyDown(useKey))
            UseSelectedServerRpc();
    }

    // ---------- Lectura (para la interfaz) ----------

    public SlotData GetSlot(int index)
    {
        if (index < 0 || index >= netSlots.Count) return SlotData.Empty;
        return netSlots[index];
    }

    public ItemData GetItemAt(int index)
    {
        if (database == null) return null;
        return database.GetItem(GetSlot(index).itemId);
    }

    // ---------- Acciones del jugador ----------

    public void SelectSlot(int index)
    {
        if (!IsOwner) return;
        netSelected.Value = ((index % hotbarSize) + hotbarSize) % hotbarSize;
    }

    // Mover o intercambiar dos huecos (lo pide la interfaz, lo hace el servidor)
    [ServerRpc]
    public void MoveSlotServerRpc(int from, int to)
    {
        if (from == to) return;
        if (from < 0 || from >= netSlots.Count || to < 0 || to >= netSlots.Count) return;

        SlotData origin = netSlots[from];
        SlotData target = netSlots[to];

        if (origin.IsEmpty) return;

        if (target.IsEmpty)
        {
            netSlots[to] = origin;
            netSlots[from] = SlotData.Empty;
            return;
        }

        // Mismo objeto: apilar lo que quepa
        if (target.itemId == origin.itemId && database != null)
        {
            ItemData item = database.GetItem(target.itemId);
            int maxStack = item != null ? Mathf.Max(1, item.maxStack) : 1;

            if (target.count < maxStack)
            {
                int moved = Mathf.Min(maxStack - target.count, origin.count);
                target.count += moved;
                origin.count -= moved;

                netSlots[to] = target;
                netSlots[from] = origin.count > 0 ? origin : SlotData.Empty;
                return;
            }
        }

        // Objetos distintos (o pila llena): intercambiar
        netSlots[to] = origin;
        netSlots[from] = target;
    }

    [ServerRpc]
    public void UseSelectedServerRpc()
    {
        UseSlot(SelectedIndex);
    }

    // ---------- Logica del servidor ----------

    // Anade objetos. Solo servidor. Devuelve cuantos NO cupieron.
    public int AddItem(ItemData item, int amount = 1)
    {
        if (!IsServer || item == null || amount <= 0) return amount;
        if (database == null)
        {
            Debug.LogError("El inventario no tiene catalogo (ItemDatabase) asignado.");
            return amount;
        }

        int id = database.GetId(item);
        if (id < 0) return amount;

        int maxStack = Mathf.Max(1, item.maxStack);

        // 1) Rellenar pilas existentes del mismo objeto
        for (int i = 0; i < netSlots.Count && amount > 0; i++)
        {
            SlotData slot = netSlots[i];
            if (slot.IsEmpty || slot.itemId != id || slot.count >= maxStack) continue;

            int toAdd = Mathf.Min(maxStack - slot.count, amount);
            slot.count += toAdd;
            netSlots[i] = slot;
            amount -= toAdd;
        }

        // 2) Ocupar huecos vacios (primero el cinturon, que va antes en la lista)
        for (int i = 0; i < netSlots.Count && amount > 0; i++)
        {
            if (!netSlots[i].IsEmpty) continue;

            int toAdd = Mathf.Min(maxStack, amount);
            netSlots[i] = new SlotData { itemId = id, count = toAdd };
            amount -= toAdd;
        }

        return amount;
    }

    // ---------- Guardado / restauracion (migracion de host) ----------

    public System.Collections.Generic.List<Vector2Int> ExportSlots()
    {
        var list = new System.Collections.Generic.List<Vector2Int>();
        for (int i = 0; i < netSlots.Count; i++)
            list.Add(new Vector2Int(netSlots[i].itemId, netSlots[i].count));
        return list;
    }

    public void ImportSlots(System.Collections.Generic.List<Vector2Int> list)
    {
        if (!IsServer || list == null) return;

        for (int i = 0; i < netSlots.Count && i < list.Count; i++)
            netSlots[i] = new SlotData { itemId = list[i].x, count = list[i].y };
    }

    private void UseSlot(int index)
    {
        if (!IsServer) return;

        SlotData slot = GetSlot(index);
        if (slot.IsEmpty || database == null) return;

        ItemData item = database.GetItem(slot.itemId);
        if (item == null) return;

        switch (item.type)
        {
            case ItemType.Consumable:
                if (_character != null && item.healAmount > 0f)
                {
                    _character.Heal(item.healAmount);   // ya estamos en el servidor

                    slot.count--;
                    netSlots[index] = slot.count > 0 ? slot : SlotData.Empty;
                }
                break;

            case ItemType.Weapon:
                Debug.Log("Arma seleccionada: " + item.itemName);
                break;

            case ItemType.Generic:
                break;
        }
    }
}
