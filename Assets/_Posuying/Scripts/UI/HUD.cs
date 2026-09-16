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
    }
}
