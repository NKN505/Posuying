using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    public EnemyBehaviour enemy;
    public Image fillBar;
    public Image background;
    public float maxHealth = 1000f;

    private Transform _cam;
    private float _fullWidth;

    void Start()
    {
        _fullWidth = background != null
            ? background.rectTransform.sizeDelta.x
            : fillBar.rectTransform.sizeDelta.x;
    }

    void Update()
    {
        if (enemy == null || !enemy.gameObject.activeSelf) return;

        // La camara local no existe hasta que aparece nuestro jugador
        if (_cam == null)
        {
            if (Camera.main == null) return;
            _cam = Camera.main.transform;
        }

        transform.LookAt(_cam);
        transform.Rotate(0, 180f, 0);

        if (fillBar != null)
        {
            // La vida maxima la manda el servidor (el director de hordas la fija al crear el enemigo)
            maxHealth = enemy.GetMaxHealth();
            float ratio = maxHealth > 0f ? Mathf.Clamp01(enemy.GetHealth() / maxHealth) : 0f;
            Vector2 size = fillBar.rectTransform.sizeDelta;
            size.x = _fullWidth * ratio;
            fillBar.rectTransform.sizeDelta = size;
            fillBar.color = Color.Lerp(Color.red, Color.green, ratio);
        }
    }
}
