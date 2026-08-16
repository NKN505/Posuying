using UnityEngine;
using UnityEngine.UI;

// Contador de fotogramas por segundo, arriba a la derecha.
// Se enciende y se apaga desde Opciones > Graficos.
//
// Se pone en el Canvas; se construye solo.
public class FpsCounter : MonoBehaviour
{
    [Header("Referencias")]
    public Canvas canvas;

    [Header("Colocacion")]
    [Tooltip("Separacion desde la esquina superior derecha")]
    public Vector2 anchorPosition = new Vector2(-20f, -20f);

    [Header("Comportamiento")]
    [Tooltip("Cada cuanto se refresca el numero (para que no parpadee)")]
    public float refreshInterval = 0.25f;

    private Text _text;
    private float _accumulated;
    private int _frames;
    private float _timer;
    private bool _built;

    void Update()
    {
        if (!_built)
        {
            TryBuild();
            return;
        }

        bool show = GameSettings.ShowFps;
        if (_text.gameObject.activeSelf != show)
            _text.gameObject.SetActive(show);

        if (!show) return;

        _accumulated += Time.unscaledDeltaTime;
        _frames++;
        _timer += Time.unscaledDeltaTime;

        if (_timer < refreshInterval) return;

        float fps = _frames / _accumulated;
        _text.text = Mathf.RoundToInt(fps) + " FPS";

        // Verde si va fino, amarillo si regular, rojo si va mal
        _text.color = fps >= 55f ? new Color(0.5f, 1f, 0.5f)
                    : fps >= 30f ? new Color(1f, 0.9f, 0.4f)
                                 : new Color(1f, 0.5f, 0.5f);

        _accumulated = 0f;
        _frames = 0;
        _timer = 0f;
    }

    private void TryBuild()
    {
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();

        if (canvas == null) return;

        GameObject go = new GameObject("ContadorFPS", typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = anchorPosition;
        rt.sizeDelta = new Vector2(140f, 28f);

        _text = go.AddComponent<Text>();
        _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _text.fontSize = 18;
        _text.fontStyle = FontStyle.Bold;
        _text.alignment = TextAnchor.UpperRight;
        _text.raycastTarget = false;
        _text.text = "";

        Shadow shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        _built = true;
    }
}
