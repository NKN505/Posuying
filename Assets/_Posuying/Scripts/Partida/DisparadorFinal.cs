using Unity.Netcode;
using UnityEngine;

// Punto de la escena que lanza el final del juego en cuanto un jugador lo pisa.
//
// Es la SALIDA de la mision de la bomba (ver MisionBomba): solo se abre cuando
// la bomba ya esta colocada y corre la cuenta atras. Hasta entonces ni se ve
// ni hace nada.
//
// Necesita un Collider con IsTrigger, en la capa Ignore Raycast para que no
// pare disparos ni lineas de vision.
public class DisparadorFinal : MonoBehaviour
{
    [Tooltip("Marcado: solo funciona con la bomba colocada. Desmarcado: siempre (para pruebas)")]
    public bool requiereBombaColocada = true;

    [Tooltip("Lo que marca la salida (pilar, luz). Solo se ve cuando se puede escapar")]
    public GameObject[] marcas;

    private bool Abierta
    {
        get
        {
            if (!requiereBombaColocada) return true;
            var mision = MisionBomba.Instance;
            return mision != null && mision.IsSpawned && mision.Fase == FaseBomba.Escapar;
        }
    }

    void Update()
    {
        bool ver = Abierta;
        if (marcas == null) return;
        foreach (var marca in marcas)
            if (marca != null && marca.activeSelf != ver) marca.SetActive(ver);
    }

    // Stay y no Enter: quien ya este encima cuando se coloca la bomba tambien cuenta
    void OnTriggerStay(Collider other)
    {
        if (!Abierta) return;

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
