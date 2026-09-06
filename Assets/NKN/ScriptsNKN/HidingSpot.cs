using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Un escondite: matorrales, un hueco entre contenedores, debajo de una
/// escalera. Se pone en un objeto vacío con un Collider marcado como trigger.
///
/// ESCONDERSE EXIGE ESTARSE QUIETO. No basta con meterse dentro: si te mueves
/// por el escondite, no cuentas como escondido. Es la misma idea que en el
/// sistema de ruido — la seguridad se paga con inmovilidad, y por eso genera
/// tensión: estás a salvo pero ciego y clavado en el sitio.
///
/// Se mide con la velocidad que ya calcula el servidor en PlayerNoise, no con
/// el estado de agachado, porque ese es local de cada cliente y el servidor no
/// lo ve en los jugadores remotos.
///
/// QUIÉN LO RESPETA: el Hunter y el Psycho-Killer no pueden atacarte mientras
/// estés escondido. El Alertador SÍ te encuentra: esa es justamente su función,
/// sacarte de ahí avisando a los demás.
/// </summary>
[RequireComponent(typeof(Collider))]
public class HidingSpot : MonoBehaviour
{
    private static readonly List<HidingSpot> _todos = new List<HidingSpot>();
    public static IReadOnlyList<HidingSpot> All { get { return _todos; } }

    [Tooltip("Velocidad máxima para que cuente como escondido. Por encima de " +
             "esto se te ve moverte entre la maleza.")]
    public float maxSpeedToHide = 1.2f;

    private Collider _col;

    void Awake()
    {
        _col = GetComponent<Collider>();
        _col.isTrigger = true;

        // Como las zonas del Tanque: una caja trigger grande interceptaría los
        // disparos y las líneas de visión si estuviera en una capa normal.
        gameObject.layer = 2;   // Ignore Raycast
    }

    void OnEnable() { if (!_todos.Contains(this)) _todos.Add(this); }
    void OnDisable() { _todos.Remove(this); }

    public bool Contains(Vector3 punto)
    {
        if (_col == null) return false;
        return (_col.ClosestPoint(punto) - punto).sqrMagnitude < 0.0001f;
    }

    /// <summary>¿Este jugador está escondido ahora mismo?</summary>
    public static bool IsHidden(PlayerController jugador)
    {
        if (jugador == null) return false;

        // Un abatido no está escondido: está tirado en el suelo
        var abatido = jugador.GetComponent<PlayerDownedState>();
        if (abatido != null && !abatido.CanAct) return false;

        Vector3 p = jugador.transform.position;

        for (int i = 0; i < _todos.Count; i++)
        {
            var h = _todos[i];
            if (h == null || !h.Contains(p)) continue;

            var ruido = jugador.GetComponent<PlayerNoise>();
            float velocidad = ruido != null ? ruido.ObservedSpeed : 0f;

            if (velocidad <= h.maxSpeedToHide) return true;
        }

        return false;
    }

    void OnDrawGizmos()
    {
        var c = GetComponent<Collider>();
        if (c == null) return;

        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.15f);
        Gizmos.DrawCube(c.bounds.center, c.bounds.size);
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.8f);
        Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
    }
}
