using UnityEngine;

// Estela visual de un disparo. Va del canon al punto de impacto y se
// adelgaza hasta desaparecer. No tiene ninguna logica de juego: es solo
// presentacion.

[RequireComponent(typeof(LineRenderer))]
public class BulletTracer : MonoBehaviour
{
    [SerializeField] private float duration = 0.05f;
    [SerializeField] private float width = 0.02f;

    private LineRenderer line;
    private float timer;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();

        line.positionCount = 2;

        // Los dos puntos son coordenadas de MUNDO, no relativas al objeto.
        line.useWorldSpace = true;
    }

    public void Show(Vector3 from, Vector3 to)
    {
        line.SetPosition(0, from);
        line.SetPosition(1, to);

        timer = duration;
    }

    private void Update()
    {
        timer -= Time.deltaTime;

        // El adelgazamiento progresivo es lo que vende el efecto: una linea
        // que aparece y desaparece de golpe se lee como un parpadeo.
        float t = Mathf.Clamp01(timer / duration);

        line.widthMultiplier = width * t;

        if (timer <= 0.0f)
        {
            Destroy(gameObject);
        }
    }
}
