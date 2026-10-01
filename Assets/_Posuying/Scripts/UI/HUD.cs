using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HUD : MonoBehaviour
{
    public TextMeshProUGUI healthText;
    public Character player;

    [Header("Estamina")]
    public Image staminaBar;

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
