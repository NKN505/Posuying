using UnityEngine;

/// <summary>
/// Las dos preguntas de "¿me ve?" que comparten varios enemigos: si algo cae
/// dentro de un cono de visión, y si hay algo por medio que lo tape.
///
/// Está aparte porque lo usan el Acechador (al revés: ¿me ve el jugador?), el
/// Nemesis y, más adelante, el Alertador y el Psycho-Killer. Tenerlo repetido
/// en cada uno sería garantizar que se corrija un fallo en tres sitios y se
/// olvide el cuarto.
/// </summary>
public static class Perception
{
    /// <summary>
    /// ¿El punto está dentro del cono? Solo cuenta el giro horizontal: mirar al
    /// suelo no debería hacerte perder de vista a alguien que tienes delante, y
    /// además el cabeceo de los jugadores remotos no se replica.
    /// </summary>
    public static bool InCone(Vector3 origen, Vector3 mirada, Vector3 objetivo, float anguloTotal)
    {
        Vector3 hacia = objetivo - origen;
        hacia.y = 0f;
        mirada.y = 0f;

        if (hacia.sqrMagnitude < 0.0001f || mirada.sqrMagnitude < 0.0001f) return false;

        return Vector3.Angle(mirada, hacia) <= anguloTotal * 0.5f;
    }

    /// <summary>
    /// ¿Hay vista despejada entre los dos puntos?
    ///
    /// Se usa RaycastAll y no Linecast porque hay que descartar impactos: el
    /// origen suele estar DENTRO de la cápsula del que mira, y el destino es el
    /// cuerpo del que se mira. Con un Linecast normal, el primer impacto sería
    /// uno de esos dos y saldría "tapado" siempre.
    /// </summary>
    public static bool HasLineOfSight(Vector3 desde, Vector3 hasta, LayerMask bloqueadores,
                                      Transform ignorarA = null, Transform ignorarB = null)
    {
        Vector3 direccion = hasta - desde;
        float distancia = direccion.magnitude;
        if (distancia < 0.01f) return true;

        var impactos = Physics.RaycastAll(desde, direccion / distancia, distancia,
                                          bloqueadores, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < impactos.Length; i++)
        {
            Transform t = impactos[i].collider.transform;
            if (ignorarA != null && t.IsChildOf(ignorarA)) continue;
            if (ignorarB != null && t.IsChildOf(ignorarB)) continue;
            return false;
        }

        return true;
    }

    /// <summary>Cono + línea de visión juntos, que es como se usa casi siempre.</summary>
    public static bool CanSee(Vector3 origen, Vector3 mirada, Vector3 objetivo,
                              float alcance, float anguloTotal, LayerMask bloqueadores,
                              Transform ignorarA = null, Transform ignorarB = null)
    {
        if (Vector3.Distance(origen, objetivo) > alcance) return false;
        if (!InCone(origen, mirada, objetivo, anguloTotal)) return false;

        return HasLineOfSight(origen, objetivo, bloqueadores, ignorarA, ignorarB);
    }

    /// <summary>
    /// Pecho y cabeza de un personaje. Se prueban los dos porque con un solo
    /// punto, asomar la cabeza por encima de una caja no contaría como verlo.
    /// </summary>
    public static void BodyPoints(Transform quien, out Vector3 pecho, out Vector3 cabeza)
    {
        var cc = quien.GetComponent<CharacterController>();

        if (cc != null)
        {
            Vector3 pies = quien.TransformPoint(cc.center) - Vector3.up * (cc.height * 0.5f);
            pecho = pies + Vector3.up * (cc.height * 0.55f);
            cabeza = pies + Vector3.up * (cc.height * 0.95f);
            return;
        }

        pecho = quien.position + Vector3.up * 1.0f;
        cabeza = quien.position + Vector3.up * 1.7f;
    }
}
