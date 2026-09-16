using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Minimapa de puntos, abajo a la izquierda.
//
// NO usa una segunda camara ni una RenderTexture: se toman las posiciones del
// mundo y se pintan como puntos en el Canvas. Asi no cuesta render (que ya nos
// dio un susto con la GPU al 100%), no hay que preparar capas ni assets, y
// sobre un mapa greybox se lee mucho mejor que una vista cenital real.
//
// Azul = jugadores, verde = NPCs, rojo = enemigos.
//
// Se pone en el Canvas; se construye solo (igual que TeamHUD).
public class Minimap : MonoBehaviour
{
    [Header("Referencias")]
    public Canvas canvas;

    [Header("Colocacion")]
    public Vector2 anchorPosition = new Vector2(20f, 20f);   // desde abajo a la izquierda
    public float diameter = 180f;
    public float borderWidth = 3f;

    [Header("Puntos")]
    public float dotSize = 7f;
    public float playerDotSize = 9f;
    public float selfDotSize = 10f;

    [Header("Colores")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.5f);
    public Color borderColor = new Color(1f, 1f, 1f, 0.25f);
    public Color playerColor = new Color(0.35f, 0.6f, 1f, 1f);
    public Color npcColor = new Color(0.4f, 1f, 0.5f, 1f);
    public Color enemyColor = new Color(1f, 0.3f, 0.25f, 1f);
    public Color selfColor = new Color(1f, 0.95f, 0.6f, 1f);

    [Header("Comportamiento")]
    [Tooltip("El mapa gira contigo: arriba es siempre hacia donde miras")]
    public bool rotateWithPlayer = true;

    private RectTransform _container;
    private RectTransform _dotsRoot;
    private readonly List<Image> _dots = new List<Image>();
    private Sprite _circle;
    private bool _built;

    void Update()
    {
        if (!_built)
        {
            TryBuild();
            return;
        }

        var local = NetworkPlayer.LocalPlayer;
        bool visible = GameSettings.MinimapEnabled && local != null;

        _container.gameObject.SetActive(visible);
        if (!visible) return;

        Vector3 center = local.transform.position;
        float heading = rotateWithPlayer ? HeadingOf(local) : 0f;

        float range = Mathf.Max(5f, GameSettings.MinimapRange);
        float pixelsPerMeter = (diameter * 0.5f) / range;

        int used = 0;

        // Tu punto siempre en el centro
        PlaceDot(ref used, Vector2.zero, selfDotSize, selfColor);

        var players = NetworkPlayer.AllPlayers;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null || p == local) continue;

            // Los companeros nunca desaparecen: si estan lejos se pegan al borde.
            // Perder de vista a una persona en el mapa es peor que un punto impreciso.
            if (TryProject(p.transform.position, center, heading, pixelsPerMeter,
                           true, playerDotSize, out Vector2 at))
                PlaceDot(ref used, at, playerDotSize, playerColor);
        }

        var npcs = NpcSurvivor.All;
        for (int i = 0; i < npcs.Count; i++)
        {
            var npc = npcs[i];
            if (npc == null) continue;

            // Los NPC cuentan como companeros: tampoco se pierden de vista
            if (TryProject(npc.transform.position, center, heading, pixelsPerMeter,
                           true, dotSize, out Vector2 at))
                PlaceDot(ref used, at, dotSize, npcColor);
        }

        var enemies = EnemyBehaviour.All;
        for (int i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (enemy == null) continue;

            // Los enemigos NO se clavan al borde: con una horda de 30 el minimapa
            // seria un anillo rojo permanente que no dice nada.
            if (TryProject(enemy.transform.position, center, heading, pixelsPerMeter,
                           false, dotSize, out Vector2 at))
                PlaceDot(ref used, at, dotSize, enemyColor);
        }

        for (int i = used; i < _dots.Count; i++)
            _dots[i].gameObject.SetActive(false);
    }

    // Hacia donde mira el jugador. Se usa la camara porque en primera persona
    // es la que manda: el cuerpo puede ir un paso por detras.
    private float HeadingOf(PlayerController local)
    {
        Camera cam = local.GetComponentInChildren<Camera>(true);
        Transform reference = cam != null ? cam.transform : local.transform;

        return reference.eulerAngles.y;
    }

    // Pasa una posicion del mundo a coordenadas del minimapa.
    // Devuelve false si esta fuera de alcance y no hay que pegarla al borde.
    private bool TryProject(Vector3 world, Vector3 center, float heading,
                            float pixelsPerMeter, bool clampToEdge, float size,
                            out Vector2 result)
    {
        Vector3 delta = world - center;
        Vector2 flat = new Vector2(delta.x, delta.z);   // el minimapa ignora la altura

        // OJO CON EL SIGNO. En este plano (X a la derecha, Z arriba) tu vector
        // "hacia delante" es (sin h, cos h). Al girarlo un angulo t queda en
        // (sin(h-t), cos(h-t)); para que "delante" caiga arriba, o sea en (0,1),
        // hace falta h-t = 0, es decir t = +h.
        //
        // Con -h no se corrige el giro: se DUPLICA, y queda (sin 2h, cos 2h).
        // Mirando al norte parece correcto y el mapa gira, asi que engana; pero
        // mirando al este todo sale espejado (izquierda y derecha cambiadas).
        if (heading != 0f)
            flat = Rotate(flat, heading);

        result = flat * pixelsPerMeter;

        // Se descuenta medio punto para que quede pegado por dentro del borde
        // y no se salga a medias del circulo.
        float radius = diameter * 0.5f - size * 0.5f;

        if (result.magnitude <= radius) return true;

        if (!clampToEdge)
        {
            result = Vector2.zero;
            return false;
        }

        result = result.normalized * radius;
        return true;
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    private void PlaceDot(ref int used, Vector2 position, float size, Color color)
    {
        EnsureDots(used + 1);

        Image dot = _dots[used];
        dot.gameObject.SetActive(true);
        dot.color = color;

        RectTransform rt = dot.rectTransform;
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = position;

        used++;
    }

    // ---------- Construccion ----------

    private void TryBuild()
    {
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();

        if (canvas == null) return;

        _circle = MakeCircleSprite(64);

        // Borde: un circulo un poco mas grande por detras del fondo
        GameObject borderGO = new GameObject("Minimapa", typeof(RectTransform), typeof(Image));
        _container = borderGO.GetComponent<RectTransform>();
        _container.SetParent(canvas.transform, false);
        _container.anchorMin = _container.anchorMax = new Vector2(0f, 0f);
        _container.pivot = new Vector2(0f, 0f);
        _container.anchoredPosition = anchorPosition;
        _container.sizeDelta = new Vector2(diameter + borderWidth * 2f, diameter + borderWidth * 2f);
        _container.SetAsFirstSibling();   // nunca por encima de un menu

        Image border = borderGO.GetComponent<Image>();
        border.sprite = _circle;
        border.color = borderColor;
        border.raycastTarget = false;

        // Fondo
        GameObject backGO = new GameObject("Fondo", typeof(RectTransform), typeof(Image));
        RectTransform back = backGO.GetComponent<RectTransform>();
        back.SetParent(_container, false);
        back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f);
        back.pivot = new Vector2(0.5f, 0.5f);
        back.sizeDelta = new Vector2(diameter, diameter);
        back.anchoredPosition = Vector2.zero;

        Image background = backGO.GetComponent<Image>();
        background.sprite = _circle;
        background.color = backgroundColor;
        background.raycastTarget = false;

        // Los puntos cuelgan de aqui, centrados
        GameObject dotsGO = new GameObject("Puntos", typeof(RectTransform));
        _dotsRoot = dotsGO.GetComponent<RectTransform>();
        _dotsRoot.SetParent(_container, false);
        _dotsRoot.anchorMin = _dotsRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _dotsRoot.pivot = new Vector2(0.5f, 0.5f);
        _dotsRoot.sizeDelta = new Vector2(diameter, diameter);
        _dotsRoot.anchoredPosition = Vector2.zero;

        _built = true;
    }

    private void EnsureDots(int needed)
    {
        while (_dots.Count < needed)
        {
            GameObject go = new GameObject("Punto" + _dots.Count, typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(_dotsRoot, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            Image image = go.GetComponent<Image>();
            image.sprite = _circle;
            image.raycastTarget = false;

            _dots.Add(image);
        }
    }

    // Un circulo blanco hecho a mano. Asi el minimapa no depende de que importes
    // ninguna imagen al proyecto.
    private static Sprite MakeCircleSprite(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        float radius = size * 0.5f;
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - radius + 0.5f;
                float dy = y - radius + 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                // Medio pixel de transicion para que el borde no salga dentado
                float alpha = Mathf.Clamp01(radius - distance);

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
    }
}
