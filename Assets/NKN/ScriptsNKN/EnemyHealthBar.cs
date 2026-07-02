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
        _cam = Camera.main.transform;
        maxHealth = enemy.GetHealth();
        _fullWidth = background != null
            ? background.rectTransform.sizeDelta.x
            : fillBar.rectTransform.sizeDelta.x;
    }

    void Update()
    {
        if (enemy == null || !enemy.gameObject.activeSelf) return;

        transform.LookAt(_cam);
        transform.Rotate(0, 180f, 0);

        if (fillBar != null)
        {
            float ratio = Mathf.Clamp01(enemy.GetHealth() / maxHealth);
            Vector2 size = fillBar.rectTransform.sizeDelta;
            size.x = _fullWidth * ratio;
            fillBar.rectTransform.sizeDelta = size;
            fillBar.color = Color.Lerp(Color.red, Color.green, ratio);
        }
    }
}
