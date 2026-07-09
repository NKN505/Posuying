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
        if (player == null) return;

        healthText.text = "Vida: " + Mathf.Max(0, Mathf.RoundToInt(player.GetHealth()));

        if (staminaBar != null)
            staminaBar.fillAmount = player.GetStamina() / player.GetMaxStamina();
    }
}
