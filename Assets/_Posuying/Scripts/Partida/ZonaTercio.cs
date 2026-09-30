using System.Collections.Generic;
using UnityEngine;

// Volumen que marca que parte del mapa pertenece a un tercio.
//
// Se usa para saber hasta donde ha llegado el equipo (ProgresoTercios) y, con
// eso, en que tercio reaparece quien gasta una vida.
//
// No es un collider a proposito: un trigger enorme se comeria los raycast de
// los disparos y las lineas de vision de los enemigos. Aqui solo se comprueba
// si una posicion cae dentro de la caja, en el servidor y dos veces por segundo.
public class ZonaTercio : MonoBehaviour
{
    public static readonly List<ZonaTercio> Todas = new List<ZonaTercio>();

    [Tooltip("Numero de tercio: 1, 2 o 3")]
    public int tercio = 1;

    [Tooltip("Tamano de la caja en metros, en el espacio local de este objeto")]
    public Vector3 tamano = new Vector3(100f, 40f, 100f);

    void OnEnable() => Todas.Add(this);
    void OnDisable() => Todas.Remove(this);

    public bool Contiene(Vector3 posicion)
    {
        Vector3 local = transform.InverseTransformPoint(posicion);
        return Mathf.Abs(local.x) <= tamano.x * 0.5f
            && Mathf.Abs(local.y) <= tamano.y * 0.5f
            && Mathf.Abs(local.z) <= tamano.z * 0.5f;
    }

    // El tercio que contiene esa posicion, o 0 si no esta en ninguno
    public static int TercioEn(Vector3 posicion)
    {
        int mejor = 0;
        foreach (var zona in Todas)
            if (zona != null && zona.Contiene(posicion) && zona.tercio > mejor)
                mejor = zona.tercio;
        return mejor;
    }

    void OnDrawGizmos()
    {
        Color[] colores = { Color.white, new Color(1f, 0.35f, 0.3f), new Color(1f, 0.85f, 0.2f), new Color(0.3f, 0.75f, 1f) };
        Gizmos.color = colores[Mathf.Clamp(tercio, 0, 3)];
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, tamano);
    }
}
