using UnityEngine;

/// <summary>
/// Acustica de interiores para Wwise (Game-Defined Auxiliary Sends).
///
/// Decide si un punto esta FUERA (sin techo encima) o DENTRO de una sala
/// pequena, mediana o grande, y manda el emisor al Aux Bus que toca:
/// AB_Room_Small / AB_Room_Medium / AB_Room_Large. Fuera no se envia a ninguno.
///
/// La sala se mide sola con rayos (techo hacia arriba, paredes en 8
/// direcciones), asi que cada edificio coge su acustica sin colocar nada
/// a mano en la escena.
///
/// En Wwise, el sonido tiene que tener marcado "Use game-defined auxiliary
/// sends" (pestana General Settings); si no, estos envios no le afectan.
/// </summary>
public enum WwiseRoomSize { Outside, Small, Medium, Large }

public static class WwiseRoomAcoustics
{
    // Nombres de los Aux Bus en el proyecto de Wwise
    public static string AuxBusSmall = "AB_Room_Small";
    public static string AuxBusMedium = "AB_Room_Medium";
    public static string AuxBusLarge = "AB_Room_Large";

    // Ancho medio de la sala (m): por debajo de Small es pequena, por debajo
    // de Medium es mediana, y por encima grande
    public static float SmallRoomWidth = 6.0f;
    public static float MediumRoomWidth = 14.0f;

    // Hasta donde se buscan techo y paredes
    public static float DefaultRoofCheckDistance = 12.0f;
    public static float WallCheckDistance = 30.0f;

    // Un solo array para todos: se rellena justo antes de cada envio
    private static AkAuxSendArray sends;

    /// <summary>
    /// Sala alrededor de un punto. "self" es quien emite (se ignoran sus
    /// colliders); "alsoIgnore" es opcional (p. ej. el enemigo alcanzado).
    /// </summary>
    public static WwiseRoomSize GetRoom(Vector3 position, Transform self,
                                        Transform alsoIgnore = null,
                                        float roofCheckDistance = -1.0f)
    {
        if (roofCheckDistance <= 0.0f)
        {
            roofCheckDistance = DefaultRoofCheckDistance;
        }

        Vector3 origin = position + Vector3.up * 0.1f;

        // Sin techo encima: calle
        if (Nearest(origin, Vector3.up, roofCheckDistance, self, alsoIgnore, false) >= roofCheckDistance)
        {
            return WwiseRoomSize.Outside;
        }

        // Ancho medio en 4 ejes (8 rayos horizontales)
        float totalWidth = 0.0f;

        for (int i = 0; i < 4; i++)
        {
            Vector3 dir = Quaternion.Euler(0.0f, i * 45.0f, 0.0f) * Vector3.forward;
            totalWidth += Nearest(origin, dir, WallCheckDistance, self, alsoIgnore, true)
                        + Nearest(origin, -dir, WallCheckDistance, self, alsoIgnore, true);
        }

        float averageWidth = totalWidth / 4.0f;

        if (averageWidth < SmallRoomWidth) return WwiseRoomSize.Small;
        if (averageWidth < MediumRoomWidth) return WwiseRoomSize.Medium;
        return WwiseRoomSize.Large;
    }

    /// <summary>
    /// Envia el emisor al Aux Bus de su sala (o a ninguno si esta fuera).
    /// Llamar justo antes de Post().
    /// </summary>
    public static void ApplyReverb(GameObject emitter, WwiseRoomSize room)
    {
        if (emitter == null)
        {
            return;
        }

        if (sends == null)
        {
            sends = new AkAuxSendArray();
        }

        sends.Reset();

        string bus = null;
        switch (room)
        {
            case WwiseRoomSize.Small: bus = AuxBusSmall; break;
            case WwiseRoomSize.Medium: bus = AuxBusMedium; break;
            case WwiseRoomSize.Large: bus = AuxBusLarge; break;
        }

        if (!string.IsNullOrEmpty(bus))
        {
            sends.Add(AkUnitySoundEngine.GetIDFromString(bus), 1.0f);
        }

        AkUnitySoundEngine.SetGameObjectAuxSendValues(emitter, sends, (uint)sends.Count());
    }

    // Distancia al primer collider valido en esa direccion (o maxDistance si
    // no hay ninguno). Se saltan los triggers, el propio emisor, "alsoIgnore"
    // y, al medir paredes, los enemigos (un zombi no es una pared).
    private static float Nearest(Vector3 origin, Vector3 direction, float maxDistance,
                                 Transform self, Transform alsoIgnore, bool skipEnemies)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            maxDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        Transform selfRoot = self != null ? self.root : null;
        float nearest = maxDistance;

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].distance >= nearest)
            {
                continue;
            }

            Transform t = hits[i].collider.transform;

            if (selfRoot != null && t.IsChildOf(selfRoot)) continue;
            if (alsoIgnore != null && t.IsChildOf(alsoIgnore)) continue;
            if (skipEnemies && hits[i].collider.GetComponentInParent<EnemyBehaviour>() != null) continue;

            nearest = hits[i].distance;
        }

        return nearest;
    }
}
