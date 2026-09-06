using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ataque a distancia para enemigos. Lo usan el Cobarde (segundo tercio) y,
/// más adelante, el Hunter y el Psycho-Killer del tercero.
///
/// Es hitscan con AVISO PREVIO: el enemigo apunta durante unos instantes antes
/// de disparar. Ese retardo no es decorativo, es la ventana en la que puedes
/// romper la línea de visión y anular el disparo. Sin él, un enemigo a distancia
/// es daño imposible de evitar, que en un juego de terror se siente injusto.
///
/// EN RED: apunta y dispara solo el servidor. El aviso a los clientes es para
/// que puedan verlo y oírlo.
/// </summary>
public class EnemyRangedAttack : NetworkBehaviour
{
    [Header("Disparo")]
    public float damage = 18f;
    [Tooltip("Alcance máximo del disparo")]
    public float range = 30f;
    [Tooltip("Tiempo entre disparos")]
    public float cooldown = 2.5f;
    [Tooltip("Segundos apuntando antes de disparar. Es la ventana que tiene el " +
             "jugador para cubrirse.")]
    public float windup = 0.8f;

    [Tooltip("Altura desde la que dispara")]
    public float muzzleHeight = 1.5f;
    public LayerMask sightBlockers = ~0;

    /// <summary>Está apuntando ahora mismo. Para animación o un aviso visual.</summary>
    public bool IsAiming { get; private set; }

    private float _reloj;
    private float _apuntando;
    private Transform _objetivo;

    /// <summary>
    /// Se llama cada frame desde el enemigo mientras quiera atacar a alguien.
    /// Dejar de llamarla cancela el apuntado.
    /// </summary>
    public void Aim(Transform objetivo)
    {
        if (!IsServer) return;

        if (_reloj > 0f) { _reloj -= Time.deltaTime; Cancelar(); return; }

        if (objetivo == null || !PuedoDispararle(objetivo)) { Cancelar(); return; }

        // Cambiar de objetivo reinicia el apuntado: no se acumula de uno a otro
        if (_objetivo != objetivo) { _objetivo = objetivo; _apuntando = 0f; }

        IsAiming = true;
        _apuntando += Time.deltaTime;

        if (_apuntando < windup) return;

        Disparar(objetivo);
        _apuntando = 0f;
        _reloj = cooldown;
    }

    /// <summary>Deja de apuntar. Se llama sola si el objetivo se cubre.</summary>
    public void Cancelar()
    {
        IsAiming = false;
        _apuntando = 0f;
        _objetivo = null;
    }

    private bool PuedoDispararle(Transform objetivo)
    {
        Vector3 boca = transform.position + Vector3.up * muzzleHeight;

        Vector3 pecho, cabeza;
        Perception.BodyPoints(objetivo, out pecho, out cabeza);

        if (Vector3.Distance(boca, pecho) > range) return false;

        return Perception.HasLineOfSight(boca, pecho, sightBlockers, transform, objetivo)
            || Perception.HasLineOfSight(boca, cabeza, sightBlockers, transform, objetivo);
    }

    private void Disparar(Transform objetivo)
    {
        Vector3 boca = transform.position + Vector3.up * muzzleHeight;

        Vector3 pecho, cabeza;
        Perception.BodyPoints(objetivo, out pecho, out cabeza);

        var personaje = objetivo.GetComponent<Character>();
        if (personaje != null) personaje.RequestDamage(damage, pecho, (pecho - boca).normalized);

        DisparoClientRpc(boca, pecho);
    }

    // Aquí es donde va el trazador y el sonido cuando los tengáis. De momento
    // solo pinta la línea en la vista de escena, que ya sirve para depurar.
    [ClientRpc]
    private void DisparoClientRpc(Vector3 desde, Vector3 hasta)
    {
        Debug.DrawLine(desde, hasta, Color.red, 0.4f);
    }
}
