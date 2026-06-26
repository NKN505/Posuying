using UnityEngine;
using TMPro;

public class HUD : MonoBehaviour
{
    public TextMeshProUGUI healthText;
    public Character player;

    void Update()
    {
        if (player != null)
            healthText.text = "Vida: " + Mathf.Max(0, player.GetHealth());
    }
}
