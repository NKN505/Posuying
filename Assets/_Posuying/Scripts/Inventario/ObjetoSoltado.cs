using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum TipoSoltado { Municion, Botiquin, Mochila }

// Algo que ha quedado tirado en el suelo y se recoge pasando por encima:
//   - Municion: balas de reserva para el arma que llevas en la mano.
//   - Botiquin: vida. Si la tienes al maximo se queda en el suelo para otro.
//   - Mochila: lo que llevaba un jugador al caer (inventario y municion).
//
// A diferencia de HealPickup/ItemPickup, estos se CREAN durante la partida
// (los suelta un enemigo o un jugador), asi que si se destruyen al recogerlos.
//
// Necesita en el prefab: NetworkObject y un Collider con IsTrigger, en la capa
// Ignore Raycast para que no pare disparos ni lineas de vision.
public class ObjetoSoltado : NetworkBehaviour
{
    public TipoSoltado tipo = TipoSoltado.Municion;

    [Tooltip("Balas (municion) o puntos de vida (botiquin). En la mochila no se usa.")]
    public float cantidad = 15f;

    [Tooltip("Botiquin: cuantos botiquines hay aqui (se guardan en el inventario)")]
    public int unidades = 1;

    [Tooltip("Segundos hasta que desaparece si nadie lo coge (0 = nunca)")]
    public float segundosDeVida = 90f;

    [Tooltip("Segundos desde que aparece hasta que se puede recoger. Sin esto, lo que suelta " +
             "un zombi que muere pegado a ti se recogia al instante y nunca llegabas a verlo.")]
    public float segundosAntesDeRecoger = 0.6f;

    [Header("Aspecto")]
    [Tooltip("Hijo con el modelo: gira y flota para que se vea desde lejos")]
    public Transform visual;
    public float velocidadGiro = 90f;
    public float alturaFlote = 0.08f;

    // ---- Solo servidor: contenido de una mochila ----
    private readonly List<Vector2Int> _objetos = new List<Vector2Int>();   // (id, cantidad)
    private float _municionMochila;
    private ulong _dueno;
    private float _duenoPuedeCogerDesde;
    private string _nombreDueno = "";

    private float _nacio;
    private Vector3 _visualBase;

    public override void OnNetworkSpawn()
    {
        _nacio = Time.time;
        if (visual != null) _visualBase = visual.localPosition;
    }

    void Update()
    {
        // Aspecto: en todas las maquinas, no viaja por red
        if (visual != null)
        {
            visual.Rotate(0f, velocidadGiro * Time.deltaTime, 0f, Space.World);
            visual.localPosition = _visualBase + Vector3.up * (Mathf.Sin(Time.time * 2f) * alturaFlote);
        }

        if (IsServer && IsSpawned && segundosDeVida > 0f && Time.time - _nacio > segundosDeVida)
            Retirar();
    }

    // ---------- Mochila (servidor) ----------

    public void PrepararMochila(ulong dueno, string nombreDueno, List<Vector2Int> objetos)
    {
        if (!IsServer) return;
        _dueno = dueno;
        _nombreDueno = nombreDueno;
        // El propio jugador cae encima de su mochila; que no la recoja sin querer
        // antes de que le teletransporte el respawn.
        _duenoPuedeCogerDesde = Time.time + 3f;
        _objetos.Clear();
        foreach (var o in objetos)
            if (o.x >= 0 && o.y > 0) _objetos.Add(o);
    }

    // Para lo que suelta un jugador a proposito desde el inventario
    public void BloquearPara(ulong dueno, float segundos)
    {
        if (!IsServer) return;
        _dueno = dueno;
        _duenoPuedeCogerDesde = Time.time + segundos;
    }

    public void AnadirMunicion(float balas)
    {
        if (!IsServer) return;
        _municionMochila += Mathf.Max(0f, balas);
        if (EstaVacia) Retirar();
    }

    private bool EstaVacia => _objetos.Count == 0 && _municionMochila <= 0f;

    // ---------- Recogida (servidor) ----------

    // Stay y no Enter: si caes encima mientras no puedes cogerlo (abatido, o tu
    // propia mochila recien soltada), al poder hacerlo se recoge sin salir y volver a entrar.
    void OnTriggerStay(Collider other)
    {
        if (!IsServer || !IsSpawned) return;
        if (Time.time - _nacio < segundosAntesDeRecoger) return;

        // Solo jugadores de verdad: los NPC tambien llevan el tag Player
        var jugador = other.GetComponentInParent<NetworkPlayer>();
        if (jugador == null) return;

        var estado = jugador.GetComponent<PlayerDownedState>();
        if (estado != null && !estado.CanAct) return;

        var pertenencias = jugador.GetComponent<Pertenencias>();

        // Quien acaba de soltar algo no lo recoge al instante: lo tiene a los pies
        if (jugador.OwnerClientId == _dueno && Time.time < _duenoPuedeCogerDesde) return;

        switch (tipo)
        {
            case TipoSoltado.Municion:
                if (pertenencias == null) return;
                pertenencias.RecibirMunicionClientRpc(cantidad);
                pertenencias.AvisarClientRpc("+" + Mathf.RoundToInt(cantidad) + " balas");
                Retirar();
                break;

            case TipoSoltado.Botiquin:
                // Al inventario: se usa cuando haga falta, no al pisarlo
                var catalogo = CatalogoObjetosSoltados.Instance;
                var inventario = jugador.GetComponent<Inventory>();
                if (catalogo != null && catalogo.itemBotiquin != null && inventario != null)
                {
                    int sobran = inventario.AddItem(catalogo.itemBotiquin, Mathf.Max(1, unidades));
                    if (sobran >= Mathf.Max(1, unidades)) return;   // no le cabe: se queda para otro

                    if (pertenencias != null)
                        pertenencias.AvisarClientRpc("Botiquin guardado (" +
                            GameSettings.KeyLabel(GameSettings.UseKey) + " para usarlo)");
                    unidades = sobran;
                    if (sobran <= 0) Retirar();
                    break;
                }

                // Sin inventario configurado: cura al momento, como antes
                var personaje = jugador.GetComponent<Character>();
                if (personaje == null || personaje.GetHealth() >= personaje.GetMaxHealth()) return;
                personaje.Heal(cantidad);
                if (pertenencias != null) pertenencias.AvisarClientRpc("+" + Mathf.RoundToInt(cantidad) + " de vida");
                Retirar();
                break;

            case TipoSoltado.Mochila:
                RecogerMochila(jugador, pertenencias);
                break;
        }
    }

    private void RecogerMochila(NetworkPlayer jugador, Pertenencias pertenencias)
    {
        var inventario = jugador.GetComponent<Inventory>();
        int objetosCogidos = 0;

        if (inventario != null && inventario.database != null)
        {
            for (int i = _objetos.Count - 1; i >= 0; i--)
            {
                var item = inventario.database.GetItem(_objetos[i].x);
                if (item == null) { _objetos.RemoveAt(i); continue; }

                int sobran = inventario.AddItem(item, _objetos[i].y);
                objetosCogidos += _objetos[i].y - sobran;
                if (sobran <= 0) _objetos.RemoveAt(i);
                else _objetos[i] = new Vector2Int(_objetos[i].x, sobran);   // lo que no cabe se queda
            }
        }

        float balas = _municionMochila;
        if (balas > 0f && pertenencias != null)
        {
            pertenencias.RecibirMunicionClientRpc(balas);
            _municionMochila = 0f;
        }

        if (pertenencias != null && (objetosCogidos > 0 || balas > 0f))
        {
            string de = jugador.OwnerClientId == _dueno ? "tu mochila" : "la mochila de " + _nombreDueno;
            pertenencias.AvisarClientRpc("Recoges " + de + ": " + objetosCogidos + " objeto(s), " +
                                         Mathf.RoundToInt(balas) + " balas");
        }

        if (EstaVacia) Retirar();
    }

    private void Retirar()
    {
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
    }
}
