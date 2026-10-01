using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HUD : MonoBehaviour
{
    public TextMeshProUGUI healthText;
    public Character player;

    [Header("Estamina")]
    public Image staminaBar;

    [Header("Reticula")]
    [Tooltip("Circulo que marca hasta donde puede desviarse la bala al disparar sin apuntar. " +
             "Al apuntar (clic derecho) desaparece: ahi ya se usan las miras del arma.")]
    public bool mostrarReticula = true;
    public Color colorReticula = new Color(1f, 1f, 1f, 0.75f);
    [Tooltip("Radio minimo en pixeles, para que no se cierre hasta ser un punto")]
    public float radioMinimoReticula = 6f;

    void Update()
    {
        // En red el jugador no esta en la escena: lo crea el NetworkManager al conectar
        if (player == null)
            player = NetworkPlayer.LocalPlayer;

        if (player == null) return;

        // La vida se ve en la lista de jugadores; este texto es opcional
        if (healthText != null)
            healthText.text = "Vida: " + Mathf.Max(0, Mathf.RoundToInt(player.GetHealth()));

        if (staminaBar != null)
            staminaBar.fillAmount = player.GetStamina() / player.GetMaxStamina();

        // El arma cambia al cambiar de arma o reaparecer: se busca la activa
        if (_arma == null || !_arma.isActiveAndEnabled)
            _arma = player.GetComponentInChildren<Weapon>(false);
    }

    // ---------- Municion ----------
    // Abajo a la derecha (el minimapa ocupa la izquierda): balas en el cargador
    // en grande y, al lado, las que quedan en reserva. Con OnGUI, como MatchHUD,
    // para no tener que montar objetos de interfaz en la escena.

    private const float ReferenceHeight = 1080f;
    private Weapon _arma;

    void OnGUI()
    {
        if (player == null || _arma == null || !_arma.isActiveAndEnabled) return;
        if (UIState.BlocksGameplay) return;

        var estado = player.GetComponent<PlayerDownedState>();
        if (estado != null && !estado.CanAct) return;

        DibujarReticula();

        Matrix4x4 previous = GUI.matrix;
        float scale = Screen.height / ReferenceHeight;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        float refWidth = ReferenceHeight * ((float)Screen.width / Screen.height);

        int cargador = Mathf.Max(0, Mathf.RoundToInt(_arma.GetCurrentAmmo()));
        int reserva = Mathf.Max(0, Mathf.RoundToInt(_arma.GetReserveAmmo()));
        float capacidad = _arma.GetCapacity();

        // Rojo sin balas, amarillo cuando queda un cuarto del cargador o menos
        string color = cargador <= 0 ? "#ff6666"
                     : (capacidad > 0f && cargador <= capacidad * 0.25f) ? "#ffcc55" : "#ffffff";
        string texto = "<color=" + color + "><b><size=54>" + cargador + "</size></b></color>" +
                       "<color=#c8ccd2><size=28> / " + reserva + "</size></color>";

        var estilo = new GUIStyle(GUI.skin.label)
        {
            richText = true, fontSize = 28, alignment = TextAnchor.LowerRight
        };
        Rect zona = new Rect(refWidth - 340f, ReferenceHeight - 110f, 300f, 70f);
        Sombreado(zona, texto, estilo);

        if (_arma.GetIsReloading())
            Sombreado(new Rect(zona.x, zona.y - 30f, zona.width, 30f),
                      "<size=20><color=#ffcc55>RECARGANDO...</color></size>", estilo);
        else if (cargador <= 0)
            Sombreado(new Rect(zona.x, zona.y - 30f, zona.width, 30f),
                      "<size=20><color=#ff6666>" + (reserva > 0 ? "RECARGA" : "SIN MUNICION") + "</color></size>", estilo);

        GUI.matrix = previous;
    }

    // ---------- Reticula ----------
    // El radio no es decorativo: sale de la dispersion real del arma (grados) y del
    // campo de vision de la camara, asi que las balas caen DENTRO del circulo.

    private Camera _camara;
    private static Texture2D _anillo;
    private float _radioSuave;

    private void DibujarReticula()
    {
        if (!mostrarReticula || Event.current.type != EventType.Repaint) return;
        if (_arma.GetIsAiming()) return;

        if (_camara == null) _camara = _arma.GetComponentInParent<Camera>();
        if (_camara == null) return;

        float mitadFov = _camara.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float radio = Mathf.Tan(_arma.GetCurrentSpread() * Mathf.Deg2Rad) / Mathf.Tan(mitadFov) * (Screen.height * 0.5f);
        radio = Mathf.Max(radio, radioMinimoReticula);

        // Suavizado: al agacharse o al disparar seguido el circulo se cierra y se
        // abre de forma continua en vez de dar saltos
        _radioSuave = _radioSuave <= 0f ? radio
                    : Mathf.Lerp(_radioSuave, radio, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
        radio = _radioSuave;

        if (_anillo == null) _anillo = CrearAnillo(256, 5f);

        Vector2 centro = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Color antes = GUI.color;

        // Sombra oscura un pixel mas grande: se distingue tambien sobre fondos claros
        GUI.color = new Color(0f, 0f, 0f, colorReticula.a * 0.5f);
        GUI.DrawTexture(new Rect(centro.x - radio - 1f, centro.y - radio - 1f, radio * 2f + 2f, radio * 2f + 2f), _anillo);
        GUI.color = colorReticula;
        GUI.DrawTexture(new Rect(centro.x - radio, centro.y - radio, radio * 2f, radio * 2f), _anillo);

        // Punto central: el centro exacto de la pantalla
        float punto = Mathf.Max(2f, Screen.height / 360f);
        GUI.DrawTexture(new Rect(centro.x - punto * 0.5f, centro.y - punto * 0.5f, punto, punto), Texture2D.whiteTexture);
        GUI.color = antes;
    }

    // Anillo blanco con borde suavizado; se pinta una vez y se estira al radio que toque
    private static Texture2D CrearAnillo(int lado, float grosor)
    {
        var tex = new Texture2D(lado, lado, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float c = (lado - 1) * 0.5f;
        float exterior = lado * 0.5f - 1f;
        var pixeles = new Color32[lado * lado];
        for (int y = 0; y < lado; y++)
            for (int x = 0; x < lado; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                // 1 dentro del grosor del anillo, cayendo a 0 en un pixel por cada lado
                float a = Mathf.Clamp01(exterior - d + 0.5f) * Mathf.Clamp01(d - (exterior - grosor) + 0.5f);
                pixeles[y * lado + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(pixeles);
        tex.Apply();
        return tex;
    }

    // Texto con sombra negra debajo: se lee igual sobre cielo claro que a oscuras
    private static void Sombreado(Rect zona, string texto, GUIStyle estilo)
    {
        Color antes = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        // La sombra no puede llevar los colores del texto: se le quitan las etiquetas de color
        string plano = System.Text.RegularExpressions.Regex.Replace(texto, "</?color[^>]*>", "");
        GUI.Label(new Rect(zona.x + 2f, zona.y + 2f, zona.width, zona.height), plano, estilo);
        GUI.color = antes;
        GUI.Label(zona, texto, estilo);
    }
}
