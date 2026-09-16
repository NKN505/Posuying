using UnityEngine;

// Marcador de punto de aparicion. Se coloca en un GameObject vacio.
// La flecha del gizmo indica hacia donde mirara el jugador al reaparecer.
public class SpawnPoint : MonoBehaviour
{
    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.5f);
    }
}
