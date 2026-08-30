using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Lista de jugadores de la partida con su nombre y su vida.
// Se ve siempre, aunque estes lejos de tus companeros.
//
// Se pone en el Canvas; se construye sola.
public class TeamHUD : MonoBehaviour
{
    [Header("Referencias")]
    public Canvas canvas;

    [Header("Colocacion")]
    public Vector2 anchorPosition = new Vector2(20f, -50f);   // desde arriba a la izquierda
    public float rowWidth = 220f;
    public float rowHeight = 34f;

    [Header("Estilo")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.45f);
    public Color barBackColor = new Color(0f, 0f, 0f, 0.6f);
    public Color ownColor = new Color(1f, 0.95f, 0.6f, 1f);
    public Color otherColor = Color.white;
    [Tooltip("Mismo verde que las etiquetas de nombre, para reconocerlos igual")]
    public Color npcColor = new Color(0.7f, 1f, 0.75f, 1f);

    private class Row
    {
        public GameObject root;
        public Text label;
        public RectTransform fill;
        public float fullWidth;
    }

    private RectTransform _container;
    private readonly List<Row> _rows = new List<Row>();
    private Font _font;
    private bool _built;

    void Update()
    {
        if (!_built)
        {
            TryBuild();
            return;
        }

        var players = NetworkPlayer.AllPlayers;
        var npcs = NpcSurvivor.All;

        // Fuera de partida no hay nada que mostrar
        _container.gameObject.SetActive(players.Count > 0);
        if (players.Count == 0) return;

        EnsureRows(players.Count + npcs.Count);

        // Primero las personas y despues los NPC, para que la lista no baile
        // cuando un NPC muere y vuelve a salir.
        int filled = 0;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerController player = players[i];
            if (player == null) continue;

            var nameComponent = player.GetComponent<PlayerName>();
            string playerName = nameComponent != null ? nameComponent.Name : "Jugador";

            _rows[filled].root.SetActive(true);
            UpdateRow(_rows[filled], playerName, player.GetHealth(), player.GetMaxHealth(),
                      player == NetworkPlayer.LocalPlayer, false);
            filled++;
        }

        for (int i = 0; i < npcs.Count; i++)
        {
            NpcSurvivor npc = npcs[i];
            if (npc == null) continue;

            _rows[filled].root.SetActive(true);
            UpdateRow(_rows[filled], npc.Name, npc.GetHealth(), npc.GetMaxHealth(), false, true);
            filled++;
        }

        for (int i = filled; i < _rows.Count; i++)
            _rows[i].root.SetActive(false);
    }

    private void TryBuild()
    {
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();

        if (canvas == null) return;

        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject go = new GameObject("ListaJugadores", typeof(RectTransform));
        _container = go.GetComponent<RectTransform>();
        _container.SetParent(canvas.transform, false);
        _container.anchorMin = _container.anchorMax = new Vector2(0f, 1f);
        _container.pivot = new Vector2(0f, 1f);
        _container.anchoredPosition = anchorPosition;
        _container.sizeDelta = new Vector2(rowWidth, rowHeight);

        _built = true;
    }

    private void EnsureRows(int needed)
    {
        while (_rows.Count < needed)
            _rows.Add(CreateRow(_rows.Count));
    }

    private Row CreateRow(int index)
    {
        GameObject rowGO = new GameObject("Jugador" + index, typeof(RectTransform), typeof(Image));
        RectTransform rt = rowGO.GetComponent<RectTransform>();
        rt.SetParent(_container, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(rowWidth, rowHeight);
        rt.anchoredPosition = new Vector2(0f, -index * (rowHeight + 4f));
        rowGO.GetComponent<Image>().color = backgroundColor;

        // Nombre
        Text label = NewText("Nombre", rowGO.transform,
            new Vector2(8f, -2f), new Vector2(rowWidth - 16f, 18f),
            14, TextAnchor.MiddleLeft, Color.white);

        // Fondo de la barra
        GameObject backGO = new GameObject("BarraFondo", typeof(RectTransform), typeof(Image));
        RectTransform back = backGO.GetComponent<RectTransform>();
        back.SetParent(rt, false);
        back.anchorMin = back.anchorMax = new Vector2(0f, 1f);
        back.pivot = new Vector2(0f, 1f);
        back.sizeDelta = new Vector2(rowWidth - 16f, 8f);
        back.anchoredPosition = new Vector2(8f, -22f);
        backGO.GetComponent<Image>().color = barBackColor;

        // Relleno de la barra
        GameObject fillGO = new GameObject("BarraVida", typeof(RectTransform), typeof(Image));
        RectTransform fill = fillGO.GetComponent<RectTransform>();
        fill.SetParent(back, false);
        fill.anchorMin = fill.anchorMax = new Vector2(0f, 0.5f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.sizeDelta = new Vector2(rowWidth - 16f, 8f);
        fill.anchoredPosition = Vector2.zero;
        fillGO.GetComponent<Image>().color = Color.green;

        return new Row
        {
            root = rowGO,
            label = label,
            fill = fill,
            fullWidth = rowWidth - 16f
        };
    }

    private void UpdateRow(Row row, string label, float health, float maxHealth,
                           bool isLocal, bool isNpc)
    {
        float ratio = maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;

        row.label.text = (isLocal ? "> " : "") + label + "   " + Mathf.RoundToInt(health);
        row.label.color = isLocal ? ownColor : (isNpc ? npcColor : otherColor);

        Vector2 size = row.fill.sizeDelta;
        size.x = row.fullWidth * ratio;
        row.fill.sizeDelta = size;

        row.fill.GetComponent<Image>().color = Color.Lerp(Color.red, Color.green, ratio);
    }

    private Text NewText(string name, Transform parent, Vector2 position, Vector2 size,
        int fontSize, TextAnchor anchor, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;

        Text text = go.AddComponent<Text>();
        text.font = _font;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }
}
