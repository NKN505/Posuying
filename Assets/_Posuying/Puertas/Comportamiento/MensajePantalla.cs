using UnityEngine;
using UnityEngine.UI;

// Mensaje grande en el centro de la pantalla que se desvanece solo.
// No necesita nada en la escena: crea su propio Canvas la primera vez.
// Uso: MensajePantalla.Mostrar("texto");
public class MensajePantalla : MonoBehaviour
{
    const float Duracion = 3.5f;
    const float Fundido = 0.8f;

    static MensajePantalla _inst;
    Text _texto;
    float _desde = -999f;

    public static void Mostrar(string mensaje)
    {
        if (string.IsNullOrEmpty(mensaje)) return;
        if (_inst == null) Crear();
        _inst._texto.text = mensaje;
        _inst._desde = Time.unscaledTime;
        _inst.Aplicar(1f);
    }

    static void Crear()
    {
        var go = new GameObject("MensajePantalla");
        DontDestroyOnLoad(go);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;   // por encima del HUD
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var t = new GameObject("Texto", typeof(RectTransform));
        var rt = t.GetComponent<RectTransform>();
        rt.SetParent(go.transform, false);
        rt.anchorMin = new Vector2(0.1f, 0.30f);
        rt.anchorMax = new Vector2(0.9f, 0.40f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var txt = t.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 36;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.raycastTarget = false;
        txt.color = Color.white;
        var o = t.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.9f);
        o.effectDistance = new Vector2(2f, -2f);

        _inst = go.AddComponent<MensajePantalla>();
        _inst._texto = txt;
        _inst.Aplicar(0f);
    }

    void Update()
    {
        float edad = Time.unscaledTime - _desde;
        float resto = Duracion - edad;
        Aplicar(resto <= 0f ? 0f : (resto < Fundido ? resto / Fundido : 1f));
    }

    void Aplicar(float a)
    {
        var c = _texto.color; c.a = a; _texto.color = c;
        var o = _texto.GetComponent<Outline>();
        if (o != null) { var e = o.effectColor; e.a = 0.9f * a; o.effectColor = e; }
    }
}
