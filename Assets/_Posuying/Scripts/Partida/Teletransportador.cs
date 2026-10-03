using UnityEngine;

// Zona que teletransporta al jugador que la cruza a otro punto del mapa.
//
// Va en un objeto con un Collider marcado como trigger (capa Ignore Raycast,
// para que no pare disparos ni lineas de vision). 'destino' es un Transform:
// su posicion es donde aparece el jugador y su eje Z azul, hacia donde mira.
//
// El teletransporte lo hace el DUENO del jugador en su propia maquina (la posicion
// la manda el por NetworkTransform). Los jugadores remotos tienen el
// CharacterController apagado, asi que en las demas maquinas el trigger ni salta.
//
// Se monta solo con: Posuying > Partida > Montar teletransporte (telecomunicaciones > 3er tercio)
[RequireComponent(typeof(Collider))]
public class Teletransportador : MonoBehaviour
{
    [Tooltip("Adonde lleva. Su eje Z (flecha azul) es hacia donde mirara el jugador al llegar.")]
    public Transform destino;

    [Tooltip("Segundos antes de que el mismo jugador pueda volver a usarlo")]
    public float espera = 1f;

    private float _proximo;

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (destino == null || Time.time < _proximo) return;

        // Solo jugadores de verdad (los NPC tambien llevan el tag Player) y solo el propio
        if (other.GetComponentInParent<NetworkPlayer>() == null) return;
        var jugador = other.GetComponentInParent<PlayerController>();
        if (jugador == null || !jugador.IsOwner) return;

        _proximo = Time.time + espera;
        jugador.TeleportarA(destino.position, destino.eulerAngles.y);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.75f, 0.2f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up, 1f);
        if (destino == null) return;
        Gizmos.DrawLine(transform.position + Vector3.up, destino.position + Vector3.up);
        Gizmos.DrawWireSphere(destino.position + Vector3.up, 0.5f);
        Gizmos.DrawLine(destino.position + Vector3.up, destino.position + Vector3.up + destino.forward * 1.5f);
    }
}
