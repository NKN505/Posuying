using UnityEngine;

// Marca un objeto como NO escalable: el jugador chocara contra el en vez de trepar.
// Se pone en el objeto (o en su padre) que tenga el Collider.
//
// Por defecto to-do el escenario se puede escalar; esto son las excepciones:
// vallas metalicas, cristales, muros lisos, bordes del mapa...
public class NotClimbable : MonoBehaviour
{
    [Tooltip("Solo informativo: para recordar por que este objeto no se escala")]
    public string motivo = "";

    void OnDrawGizmosSelected()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
