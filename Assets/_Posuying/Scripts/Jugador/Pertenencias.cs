using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Lo que lleva encima el jugador cuando cae, y la municion que recoge.
//
// Al gastar una vida o quedar eliminado, el inventario y la municion se quedan en
// una mochila en el suelo, donde cayo, para que vuelva a por ella o la use otro.
//
// Por que hay ida y vuelta con el dueno: el inventario vive en el servidor, pero
// la municion vive SOLO en la maquina de cada jugador (Weapon no la sincroniza).
// El servidor no sabe cuantas balas llevas, asi que te las pide, tu se las dices
// y se meten en la mochila.
//
// Va en el prefab del jugador.
public class Pertenencias : NetworkBehaviour
{
    [Tooltip("Balas de reserva con las que reapareces tras gastar una vida (el cargador vuelve lleno)")]
    public float reservaAlReaparecer = 30f;

    [Tooltip("Tope de balas de reserva que puedes llevar")]
    public float reservaMaxima = 150f;

    // La mochila que acabo de soltar, esperando a que el dueno diga sus balas (solo servidor)
    private ObjetoSoltado _mochilaPendiente;

    // ---------- Servidor ----------

    // Deja en el suelo lo que lleva. 'reaparece' decide con que municion se queda:
    // la de reaparecer si gasta una vida, ninguna si queda eliminado.
    public void SoltarEnElSuelo(bool reaparece)
    {
        if (!IsServer) return;

        var catalogo = CatalogoObjetosSoltados.Instance;
        var inventario = GetComponent<Inventory>();

        var objetos = new List<Vector2Int>();
        if (inventario != null)
        {
            objetos = inventario.ExportSlots();

            // Vaciar el inventario: todos los huecos a "nada"
            var vacio = new List<Vector2Int>();
            for (int i = 0; i < objetos.Count; i++) vacio.Add(new Vector2Int(ItemDatabase.EmptyId, 0));
            inventario.ImportSlots(vacio);
        }

        _mochilaPendiente = null;
        if (catalogo != null && catalogo.mochila != null)
        {
            var nombre = GetComponent<PlayerName>();
            _mochilaPendiente = catalogo.Soltar(catalogo.mochila, transform.position);
            if (_mochilaPendiente != null)
                _mochilaPendiente.PrepararMochila(OwnerClientId, nombre != null ? nombre.Name : "un companero", objetos);
        }

        // Pedirle al dueno sus balas (y que se quede con las de reaparecer)
        EntregarMunicionClientRpc(reaparece);
    }

    [ServerRpc]
    private void InformarMunicionServerRpc(float balas)
    {
        if (_mochilaPendiente != null && _mochilaPendiente.IsSpawned)
            _mochilaPendiente.AnadirMunicion(balas);   // si queda vacia, desaparece sola
        _mochilaPendiente = null;
    }

    // Al reiniciar la partida todos vuelven con el equipo de salida
    public void RestablecerEquipo()
    {
        if (IsServer) RestablecerMunicionClientRpc();
    }

    // ---------- Dueno ----------

    [ClientRpc]
    private void EntregarMunicionClientRpc(bool reaparece)
    {
        if (!IsOwner) return;

        float total = 0f;
        foreach (var arma in GetComponentsInChildren<Weapon>(true))
        {
            total += arma.GetCurrentAmmo() + arma.GetReserveAmmo();
            if (reaparece) Recargar(arma, arma.GetCapacity(), reservaAlReaparecer);
            else Recargar(arma, 0f, 0f);
        }

        InformarMunicionServerRpc(total);
    }

    [ClientRpc]
    private void RestablecerMunicionClientRpc()
    {
        if (!IsOwner) return;
        foreach (var arma in GetComponentsInChildren<Weapon>(true))
            Recargar(arma, arma.GetCapacity(), reservaAlReaparecer);
    }

    // Municion recogida del suelo: va al arma que llevas en la mano
    [ClientRpc]
    public void RecibirMunicionClientRpc(float balas)
    {
        if (!IsOwner) return;

        Weapon arma = GetComponentInChildren<Weapon>(false);
        if (arma == null) arma = GetComponentInChildren<Weapon>(true);
        if (arma == null) return;

        Recargar(arma, arma.GetCurrentAmmo(), Mathf.Min(reservaMaxima, arma.GetReserveAmmo() + balas));
    }

    [ClientRpc]
    public void AvisarClientRpc(string mensaje)
    {
        if (IsOwner) Notifications.Show(mensaje);
    }

    // Deja cargador y reserva en esos valores y actualiza los estados que usa el
    // arma para decidir si puede disparar o recargar.
    private static void Recargar(Weapon arma, float cargador, float reserva)
    {
        arma.SetCurrentAmmo(cargador);
        arma.SetReserveAmmo(reserva);
        arma.SetIsEmpty(cargador <= 0f);
        arma.SetIsFull(cargador >= arma.GetCapacity());
        arma.SetNoAmmo(reserva <= 0f);
    }
}
