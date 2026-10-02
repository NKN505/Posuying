using System.Collections.Generic;
using UnityEngine;

// Los dos sitios de la mision de la bomba (ver MisionBomba):
//   - Recoger: donde esta la bomba al empezar. Se coge pasando por encima.
//   - Colocar: donde hay que ponerla. Se coloca con una tecla estando dentro.
//
// Se pueden mover libremente por la escena. No llevan NetworkObject: lo que se
// ve en cada momento sale de la fase de la mision, que ya viaja por red.
//
// El de Recoger necesita un Collider con IsTrigger en la capa Ignore Raycast.
public class PuntoBomba : MonoBehaviour
{
    public enum Tipo { Recoger, Colocar }

    public static readonly List<PuntoBomba> Todos = new List<PuntoBomba>();

    public Tipo tipo = Tipo.Recoger;

    [Tooltip("Colocar: a que distancia del punto se puede poner la bomba")]
    public float radio = 3f;

    [Header("Lo que se ve segun la fase")]
    [Tooltip("Recoger: la bomba en el suelo. Colocar: la marca de 'ponla aqui'")]
    public GameObject antes;
    [Tooltip("Solo Colocar: la bomba ya puesta, durante la cuenta atras")]
    public GameObject despues;

    private Vector3 _sitioInicial;

    void Awake()
    {
        _sitioInicial = transform.position;
    }

    void OnEnable() => Todos.Add(this);
    void OnDisable() => Todos.Remove(this);

    public bool EstaCerca(Vector3 posicion, float margen = 0f)
    {
        Vector3 d = posicion - transform.position;
        // Mas tolerante en vertical: el punto puede estar a ras de suelo o sobre una mesa
        return new Vector2(d.x, d.z).magnitude <= radio + margen && Mathf.Abs(d.y) <= 3f;
    }

    void Update()
    {
        var mision = MisionBomba.Instance;
        bool enPartida = mision != null && mision.IsSpawned;
        FaseBomba fase = enPartida ? mision.Fase : FaseBomba.BuscarBomba;

        // La bomba esta en su sitio de la escena o, si se le cayo a alguien, donde cayo
        if (tipo == Tipo.Recoger)
        {
            Vector3 sitio = enPartida && mision.BombaCaida ? mision.DondeCayo : _sitioInicial;
            if ((transform.position - sitio).sqrMagnitude > 0.0001f) transform.position = sitio;
        }

        bool verAntes = tipo == Tipo.Recoger ? fase == FaseBomba.BuscarBomba
                                             : fase == FaseBomba.LlevarBomba;
        bool verDespues = tipo == Tipo.Colocar && fase == FaseBomba.Escapar;

        if (antes != null && antes.activeSelf != verAntes) antes.SetActive(verAntes);
        if (despues != null && despues.activeSelf != verDespues) despues.SetActive(verDespues);
    }

    // Stay y no Enter: si alguien esta encima abatido, la coge al levantarse
    void OnTriggerStay(Collider other)
    {
        if (tipo != Tipo.Recoger) return;

        var mision = MisionBomba.Instance;
        if (mision == null || !mision.IsServer || mision.Fase != FaseBomba.BuscarBomba) return;

        // Solo jugadores de verdad: los NPC tambien llevan el tag Player
        var jugador = other.GetComponentInParent<NetworkPlayer>();
        if (jugador == null) return;

        var estado = jugador.GetComponent<PlayerDownedState>();
        if (estado != null && !estado.CanAct) return;

        mision.RecogerBomba(jugador);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = tipo == Tipo.Recoger ? new Color(1f, 0.8f, 0.1f) : new Color(1f, 0.25f, 0.2f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, tipo == Tipo.Colocar ? radio : 1f);
    }
}
