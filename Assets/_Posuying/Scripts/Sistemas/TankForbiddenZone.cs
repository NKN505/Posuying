using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zona donde el Tanque no cabe: interiores de edificios, pasillos, sótanos.
///
/// Se pone en un objeto vacío con un Collider (una caja basta) marcado como
/// trigger, cubriendo el interior. El Tanque no entrará ahí ni intentará
/// perseguirte dentro: se quedará fuera esperando.
///
/// POR QUÉ ASÍ Y NO CON EL NAVMESH: lo "correcto" sería un segundo tipo de
/// agente más ancho con su propio horneado, y que los huecos estrechos lo
/// excluyeran solos. Es más elegante, pero obliga a mantener dos NavMesh y a
/// que la geometría esté acabada. Con cajas lo controláis a mano desde hoy,
/// sobre un mapa que todavía es greybox, y se ve dónde está cada límite.
/// Cuando el mapa esté cerrado, migrar al segundo agente es directo.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TankForbiddenZone : MonoBehaviour
{
    private static readonly List<TankForbiddenZone> _todas = new List<TankForbiddenZone>();
    public static IReadOnlyList<TankForbiddenZone> All { get { return _todas; } }

    private Collider _col;

    void Awake()
    {
        _col = GetComponent<Collider>();
        _col.isTrigger = true;
    }

    void OnEnable() { if (!_todas.Contains(this)) _todas.Add(this); }
    void OnDisable() { _todas.Remove(this); }

    public bool Contains(Vector3 punto)
    {
        if (_col == null) return false;

        // ClosestPoint devuelve el propio punto cuando está dentro del collider
        return (_col.ClosestPoint(punto) - punto).sqrMagnitude < 0.0001f;
    }

    /// <summary>¿Está este punto dentro de alguna zona prohibida?</summary>
    public static bool AnyContains(Vector3 punto)
    {
        for (int i = 0; i < _todas.Count; i++)
            if (_todas[i] != null && _todas[i].Contains(punto)) return true;

        return false;
    }

    /// <summary>
    /// Empuja un punto fuera de las zonas prohibidas. Se usa para que el Tanque
    /// se plante en la puerta en vez de intentar entrar.
    /// </summary>
    public static Vector3 PushOut(Vector3 punto, float margen = 1.5f)
    {
        for (int i = 0; i < _todas.Count; i++)
        {
            var z = _todas[i];
            if (z == null || !z.Contains(punto)) continue;

            Vector3 centro = z._col.bounds.center;
            Vector3 fuera = punto - centro;
            fuera.y = 0f;
            if (fuera.sqrMagnitude < 0.01f) fuera = Vector3.forward;

            // El borde de la caja en esa dirección, y un poco más allá
            float alcance = z._col.bounds.extents.magnitude + margen;
            punto = centro + fuera.normalized * alcance;
        }

        return punto;
    }

    void OnDrawGizmos()
    {
        var c = GetComponent<Collider>();
        if (c == null) return;

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.15f);
        Gizmos.DrawCube(c.bounds.center, c.bounds.size);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
    }
}
