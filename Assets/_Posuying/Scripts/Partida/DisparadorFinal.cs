using Unity.Netcode;
using UnityEngine;

// Punto de la escena que lanza el final del juego en cuanto un jugador lo pisa.
//
// DE PRUEBA: sirve para ver la escena final sin tener el sistema de misiones.
// Cuando exista, este objeto se borra de la escena y las misiones llaman a
// FinDelJuego.Instance.CompletarJuego().
//
// Necesita un Collider con IsTrigger, en la capa Ignore Raycast para que no
// pare disparos ni lineas de vision.
public class DisparadorFinal : MonoBehaviour
{
    [Tooltip("Si esta marcado solo funciona en el editor y en builds de desarrollo")]
    public bool soloEnDesarrollo = true;

    void OnTriggerEnter(Collider other)
    {
        if (soloEnDesarrollo && !Application.isEditor && !Debug.isDebugBuild) return;

        // Solo decide el servidor, y solo cuentan jugadores de verdad (los NPC tambien llevan el tag Player)
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (other.GetComponentInParent<NetworkPlayer>() == null) return;

        if (FinDelJuego.Instance != null)
            FinDelJuego.Instance.CompletarJuego();
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.9f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up, 1.5f);
    }
}
