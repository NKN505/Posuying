using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Etiqueta con el nombre de cada companero flotando sobre su cabeza.
//
// Se ve A TRAVES DE LAS PAREDES a proposito: no se dibuja en el mundo 3D, sino
// que se proyecta la posicion de la cabeza a coordenadas de pantalla y se pinta
// en el Canvas. Como la UI va siempre por encima de la escena, no hay nada que
// la pueda tapar. De paso evitamos tener que tocar materiales ni shaders.
//
// Se pone en el Canvas; se construye solo (igual que TeamHUD).
public class PlayerNameTags : MonoBehaviour
{
    [Header("Referencias")]
    public Canvas canvas;

    [Header("Colocacion")]
    [Tooltip("Altura extra sobre la cabeza, en metros")]
    public float heightOffset = 0.45f;

    [Header("Distancia")]
    [Tooltip("A partir de aqui ya no se muestra el nombre")]
    public float maxDistance = 40f;
    [Tooltip("Hasta esta distancia el nombre se ve a tamano completo")]
    public float fullSizeDistance = 8f;

    [Header("Estilo")]
    public int maxFontSize = 20;
    public int minFontSize = 11;
    public Color nameColor = new Color(0.85f, 0.95f, 1f, 1f);
    public Color downedColor = new Color(1f, 0.45f, 0.35f, 1f);
    [Tooltip("Texto que se anade al nombre de un companero abatido")]
    public string downedSuffix = " (abatido)";

    private class Tag
    {
        public GameObject root;
        public RectTransform rect;
        public Text label;
    }

    private RectTransform _canvasRect;
    private readonly List<Tag> _tags = new List<Tag>();
    private Font _font;
    private bool _built;

    void Update()
    {
        if (!_built)
        {
            TryBuild();
            return;
        }

        Camera cam = GetLocalCamera();
        var players = NetworkPlayer.AllPlayers;

        int used = 0;

        if (cam != null)
        {
            for (int i = 0; i < players.Count; i++)
            {
                PlayerController player = players[i];
                if (player == null) continue;

                // Tu propio nombre no se dibuja: ya sabes quien eres
                if (player == NetworkPlayer.LocalPlayer) continue;

                EnsureTags(used + 1);
                if (ShowTag(_tags[used], player, cam)) used++;
            }
        }

        // Las etiquetas sobrantes (companeros lejos, de espaldas o desconectados)
        for (int i = used; i < _tags.Count; i++)
            _tags[i].root.SetActive(false);
    }

    // La camara del jugador local es la unica encendida: las de los demas las
    // apaga NetworkPlayer al aparecer.
    private Camera GetLocalCamera()
    {
        var local = NetworkPlayer.LocalPlayer;
        if (local != null)
        {
            Camera own = local.GetComponentInChildren<Camera>(true);
            if (own != null && own.enabled && own.gameObject.activeInHierarchy) return own;
        }
        return Camera.main;
    }

    // Devuelve false si esta etiqueta no toca pintarla ahora
    private bool ShowTag(Tag tag, PlayerController player, Camera cam)
    {
        Vector3 head = HeadPosition(player);

        float distance = Vector3.Distance(cam.transform.position, head);
        if (distance > maxDistance)
        {
            tag.root.SetActive(false);
            return false;
        }

        Vector3 screen = cam.WorldToScreenPoint(head);

        // z negativo = lo tenemos detras; si no, la etiqueta saldria reflejada
        if (screen.z <= 0f)
        {
            tag.root.SetActive(false);
            return false;
        }

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, screen, CanvasCamera(), out local))
        {
            tag.root.SetActive(false);
            return false;
        }

        tag.root.SetActive(true);
        tag.rect.anchoredPosition = local;

        // De cerca se lee grande, de lejos pequeno y algo transparente
        float t = Mathf.InverseLerp(maxDistance, fullSizeDistance, distance);
        tag.label.fontSize = Mathf.RoundToInt(Mathf.Lerp(minFontSize, maxFontSize, t));

        var nameComponent = player.GetComponent<PlayerName>();
        string playerName = nameComponent != null ? nameComponent.Name : "Jugador";

        var downed = player.GetComponent<PlayerDownedState>();
        bool isDown = downed != null && !downed.CanAct;

        Color color = isDown ? downedColor : nameColor;
        color.a = Mathf.Lerp(0.45f, 1f, t);

        tag.label.text = isDown ? playerName + downedSuffix : playerName;
        tag.label.color = color;
        return true;
    }

    // Alto real del personaje: asi la etiqueta no se clava en el pecho de los
    // modelos altos ni flota demasiado en los bajos.
    private Vector3 HeadPosition(PlayerController player)
    {
        Vector3 position = player.transform.position;

        var cc = player.GetComponent<CharacterController>();
        if (cc != null)
            position = player.transform.TransformPoint(cc.center) + Vector3.up * (cc.height * 0.5f);

        return position + Vector3.up * heightOffset;
    }

    private Camera CanvasCamera()
    {
        // En modo Overlay hay que pasar null, no la camara de la escena
        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    private void TryBuild()
    {
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();

        if (canvas == null) return;

        _canvasRect = canvas.transform as RectTransform;
        if (_canvasRect == null) return;

        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _built = true;
    }

    private void EnsureTags(int needed)
    {
        while (_tags.Count < needed)
            _tags.Add(CreateTag(_tags.Count));
    }

    private Tag CreateTag(int index)
    {
        GameObject go = new GameObject("Nombre" + index, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(_canvasRect, false);

        // Anclado al centro para que la posicion proyectada valga tal cual
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(300f, 30f);

        // Debajo del resto del HUD: los nombres nunca deben tapar un menu
        rt.SetAsFirstSibling();

        Text text = go.AddComponent<Text>();
        text.font = _font;
        text.fontSize = maxFontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = nameColor;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        // Sombra dura: sin esto el nombre se pierde sobre paredes claras
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        go.SetActive(false);

        return new Tag { root = go, rect = rt, label = text };
    }
}
