using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Avisos que aparecen arriba en pantalla y se desvanecen solos
// ("Alex se ha unido a la partida", "Alex ha caido"...).
//
// Se pone en el Canvas. Desde cualquier sitio: Notifications.Show("texto");
public class Notifications : MonoBehaviour
{
    public static Notifications Instance { get; private set; }

    [Header("Comportamiento")]
    public int maxMessages = 5;
    public float duration = 5f;
    public float fadeTime = 1f;

    [Header("Estilo")]
    public Canvas canvas;
    public int fontSize = 18;
    public float lineHeight = 26f;
    public float topMargin = 60f;

    private class Message
    {
        public Text text;
        public float bornTime;
    }

    private readonly List<Message> _messages = new List<Message>();
    private RectTransform _container;
    private Font _font;
    private bool _built;

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (!_built)
        {
            TryBuild();
            return;
        }

        // Desvanecer y retirar los caducados
        for (int i = _messages.Count - 1; i >= 0; i--)
        {
            Message m = _messages[i];
            float age = Time.time - m.bornTime;

            if (age >= duration)
            {
                Destroy(m.text.gameObject);
                _messages.RemoveAt(i);
                continue;
            }

            float remaining = duration - age;
            float alpha = remaining < fadeTime ? remaining / fadeTime : 1f;

            Color c = m.text.color;
            c.a = alpha;
            m.text.color = c;
        }

        Reposition();
    }

    private void TryBuild()
    {
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();

        if (canvas == null) return;

        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject go = new GameObject("Avisos", typeof(RectTransform));
        _container = go.GetComponent<RectTransform>();
        _container.SetParent(canvas.transform, false);
        _container.anchorMin = _container.anchorMax = new Vector2(0.5f, 1f);
        _container.pivot = new Vector2(0.5f, 1f);
        _container.anchoredPosition = new Vector2(0f, -topMargin);
        _container.sizeDelta = new Vector2(700f, 200f);

        _built = true;
    }

    private void Reposition()
    {
        for (int i = 0; i < _messages.Count; i++)
        {
            RectTransform rt = _messages[i].text.rectTransform;
            rt.anchoredPosition = new Vector2(0f, -i * lineHeight);
        }
    }

    // ---------- Uso desde cualquier script ----------

    public static void Show(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (Instance == null)
        {
            Debug.Log("[Aviso] " + message);   // sin HUD (por ejemplo en el editor sin Canvas)
            return;
        }

        Instance.Add(message);
    }

    private void Add(string message)
    {
        if (!_built) TryBuild();
        if (!_built) return;

        GameObject go = new GameObject("Aviso", typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(_container, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(700f, lineHeight);

        Text text = go.AddComponent<Text>();
        text.font = _font;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = message;

        // Sombra para que se lea sobre cualquier fondo
        Shadow shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        _messages.Add(new Message { text = text, bornTime = Time.time });

        // Si hay demasiados, quitamos el mas viejo
        while (_messages.Count > maxMessages)
        {
            Destroy(_messages[0].text.gameObject);
            _messages.RemoveAt(0);
        }

        Reposition();
    }
}
