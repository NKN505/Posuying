using System.Collections.Generic;
using UnityEngine;

/// <summary>Un aviso: alguien ha visto al jugador aquí.</summary>
public struct Alert
{
    public Vector3 position;
    public float expiresAt;
    public Transform target;
}

/// <summary>
/// Los enemigos se avisan entre ellos. Lo escribe el Alertador y lo leen los
/// Hunters del tercer tercio.
///
/// Es el mismo patrón que NoiseSystem, y a propósito: una pizarra que se
/// consulta, no eventos con suscripciones. Con eventos habría que acordarse de
/// dar de baja a cada enemigo al morir, y en un juego que destruye enemigos
/// constantemente eso es una fuga de memoria esperando a pasar.
///
/// La diferencia con el ruido es que un aviso DURA. Un ruido son décimas; un
/// aviso son varios segundos, porque describe una intención ("id ahí"), no un
/// instante.
///
/// EN RED: solo servidor, como toda la IA.
/// </summary>
public static class AlertSystem
{
    public const float DefaultLifetime = 12f;

    private static readonly List<Alert> _activos = new List<Alert>(8);
    public static IReadOnlyList<Alert> Active { get { return _activos; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Reiniciar() { _activos.Clear(); }

    public static void Clear() { _activos.Clear(); }

    /// <summary>
    /// Avisa de una posición. Si ya había un aviso sobre el mismo objetivo se
    /// actualiza en vez de acumularse: si no, un Alertador siguiéndote dejaría
    /// un rastro de decenas de avisos y los Hunters irían al más viejo.
    /// </summary>
    public static void Broadcast(Vector3 position, Transform target = null,
                                 float lifetime = DefaultLifetime)
    {
        Limpiar();

        if (target != null)
        {
            for (int i = 0; i < _activos.Count; i++)
            {
                if (_activos[i].target != target) continue;

                var actualizado = _activos[i];
                actualizado.position = position;
                actualizado.expiresAt = Time.time + lifetime;
                _activos[i] = actualizado;
                return;
            }
        }

        _activos.Add(new Alert
        {
            position = position,
            expiresAt = Time.time + lifetime,
            target = target
        });
    }

    /// <summary>El aviso más cercano a quien pregunta, dentro de su alcance.</summary>
    public static bool TryGetNearest(Vector3 desde, float alcance, out Alert aviso)
    {
        Limpiar();

        aviso = default(Alert);
        float mejor = float.MaxValue;
        bool hay = false;

        for (int i = 0; i < _activos.Count; i++)
        {
            float d = Vector3.Distance(desde, _activos[i].position);
            if (d > alcance || d >= mejor) continue;

            mejor = d;
            aviso = _activos[i];
            hay = true;
        }

        return hay;
    }

    private static void Limpiar()
    {
        for (int i = _activos.Count - 1; i >= 0; i--)
            if (_activos[i].expiresAt <= Time.time)
                _activos.RemoveAt(i);
    }
}
