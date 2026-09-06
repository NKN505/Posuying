using UnityEngine;
using UnityEngine.UI;

// Aviso de si estás escondido o no.
//
// POR QUÉ HACE FALTA: sin esto, esconderse es un acto de fe. El escondite exige
// estarse quieto, pero "quieto" es un umbral invisible — no hay forma de saber
// si te has parado lo suficiente. El jugador acabaría creyendo que la mecánica
// no funciona.
//
// Muestra TRES estados, no dos. El intermedio ("te estás moviendo") es el que
// enseña la regla: te dice que el sitio vale y que el problema eres tú.
//
// El estado lo decide el servidor y llega replicado, así que lo que ves aquí es
// literalmente lo que usa la IA para decidir si te dispara.
//
// Se pone en el Canvas; se construye solo (igual que TeamHUD).
public class StealthHUD : MonoBehaviour
{
    [Header("Referencias")]
    public Canvas canvas;

    [Header("Colocacion")]
    [Tooltip("Desde abajo al centro. Por encima de la barra rapida.")]
    public Vector2 anchorPosition = new Vector2(0f, 165f);
    public Vector2 size = new Vector2(320f, 34f);

    [Header("Estilo")]
    public Color hiddenColor = new Color(0.35f, 1f, 0.5f, 1f);
    public Color exposedColor = new Color(1f, 0.75f, 0.2f, 1f);
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);
    public int fontSize = 17;

    [Header("Textos")]
    public string hiddenText = "ESCONDIDO";
    public string movingText = "TE VEN: QUEDATE QUIETO";

    private RectTransform _panel;
    private Image _fondo;
    private Text _texto;
    private Font _font;
    private bool _built;

    void Update()
    {
        if (!_built) { TryBuild(); return; }

        var estado = EstadoActual();

        bool visible = estado != PlayerNoise.Sigilo.Descubierto;
        if (_panel.gameObject.activeSelf != visible) _panel.gameObject.SetActive(visible);
        if (!visible) return;

        bool escondido = estado == PlayerNoise.Sigilo.Escondido;

        _texto.text = escondido ? hiddenText : movingText;
        _texto.color = escondido ? hiddenColor : exposedColor;

        // El borde del panel acompaña al color para que se lea de reojo, sin
        // tener que leer el texto en mitad de una persecución.
        var c = escondido ? hiddenColor : exposedColor;
        _fondo.color = new Color(c.r * 0.25f, c.g * 0.25f, c.b * 0.25f, backgroundColor.a);
    }

    private PlayerNoise.Sigilo EstadoActual()
    {
        var local = NetworkPlayer.LocalPlayer;
        if (local == null) return PlayerNoise.Sigilo.Descubierto;

        var ruido = local.GetComponent<PlayerNoise>();
        if (ruido == null) return PlayerNoise.Sigilo.Descubierto;

        return ruido.EstadoSigilo;
    }

    private void TryBuild()
    {
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var go = new GameObject("AvisoSigilo", typeof(RectTransform), typeof(Image));
        _panel = go.GetComponent<RectTransform>();
        _panel.SetParent(canvas.transform, false);
        _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0f);
        _panel.pivot = new Vector2(0.5f, 0f);
        _panel.anchoredPosition = anchorPosition;
        _panel.sizeDelta = size;
        _panel.SetAsFirstSibling();   // nunca por encima de un menu

        _fondo = go.GetComponent<Image>();
        _fondo.color = backgroundColor;
        _fondo.raycastTarget = false;

        var textoGO = new GameObject("Texto", typeof(RectTransform));
        var rt = textoGO.GetComponent<RectTransform>();
        rt.SetParent(_panel, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _texto = textoGO.AddComponent<Text>();
        _texto.font = _font;
        _texto.fontSize = fontSize;
        _texto.alignment = TextAnchor.MiddleCenter;
        _texto.raycastTarget = false;

        var contorno = textoGO.AddComponent<Outline>();
        contorno.effectColor = new Color(0f, 0f, 0f, 0.9f);
        contorno.effectDistance = new Vector2(1.2f, -1.2f);

        _panel.gameObject.SetActive(false);
        _built = true;
    }
}
