using UnityEngine;

// Marcador de punto de aparicion. Se coloca en un GameObject vacio.
// La flecha del gizmo indica hacia donde mirara el jugador al reaparecer.
public class SpawnPoint : MonoBehaviour
{
    [Tooltip("Tercio al que pertenece. Se reaparece en los puntos del tercio " +
             "mas avanzado que haya alcanzado el equipo (ProgresoTercios).")]
    public int tercio = 1;

    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.5f);
    }
}
