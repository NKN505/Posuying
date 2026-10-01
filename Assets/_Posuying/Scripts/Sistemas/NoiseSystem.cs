using System.Collections.Generic;
using UnityEngine;

/// <summary>Un ruido puntual: dónde sonó, cuánto se oye y quién lo hizo.</summary>
public struct Noise
{
    public Vector3 position;
    /// <summary>Hasta dónde llega, en metros. Es "lo fuerte que sonó".</summary>
    public float radius;
    public float expiresAt;
    public Transform source;
}

/// <summary>
/// Pizarra de ruidos recientes. El jugador escribe en ella y los enemigos con
/// oído la consultan.
///
/// EL MODELO: un ruido tiene un RADIO (lo fuerte que fue) y cada enemigo una
/// AGUDEZA (multiplicador de su oído). Un enemigo lo oye si está dentro de
/// radio × agudeza. Así un disparo se oye desde lejos y un paso agachado casi
/// no suena, y encima el Garrador puede tener mejor oído que los demás sin
/// tocar nada del emisor.
///
/// Los ruidos son instantáneos, no fuentes continuas: duran unas décimas y se
/// borran. Lo que persiste no es el ruido, es la decisión del enemigo de ir a
/// investigar esa posición.
///
/// EN RED: esto vive SOLO en el servidor, porque es quien ejecuta la IA. Los
/// clientes no necesitan saber nada.
/// </summary>
public static class NoiseSystem
{
    public const float DefaultLifetime = 0.5f;

    private static readonly List<Noise> _activos = new List<Noise>(32);

    /// <summary>Ruidos vivos ahora mismo. Solo lectura: útil para depurar.</summary>
    public static IReadOnlyList<Noise> Active => _activos;

    // El estado estático sobrevive entre partidas en el editor cuando la recarga
    // de dominio está desactivada. Sin esto, la primera partida tras darle a Play
    // arrancaría con los ruidos de la anterior.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Reiniciar() => _activos.Clear();

    public static void Clear() => _activos.Clear();

    /// <summary>Registra un ruido. El radio es en metros.</summary>
    public static void Emit(Vector3 position, float radius,
                            Transform source = null, float lifetime = DefaultLifetime)
    {
        if (radius <= 0f) return;

        Limpiar();

        _activos.Add(new Noise
        {
            position = position,
            radius = radius,
            expiresAt = Time.time + lifetime,
            source = source
        });
    }

    /// <summary>
    /// El ruido más audible desde una posición, o false si no se oye nada.
    ///
    /// Con varios ruidos a la vez gana el que "más se oye aquí": no el más
    /// cercano ni el más fuerte, sino el que deja más margen entre su alcance y
    /// la distancia. Un disparo lejano gana a un paso al lado, que es lo que
    /// haría cualquiera.
    /// </summary>
    public static bool TryHear(Vector3 listener, float acuity, out Noise heard)
    {
        Limpiar();

        heard = default(Noise);
        float mejorMargen = 0f;
        bool encontrado = false;

        for (int i = 0; i < _activos.Count; i++)
        {
            Noise n = _activos[i];

            float alcance = n.radius * acuity;
            float distancia = Vector3.Distance(listener, n.position);
            float margen = alcance - distancia;

            if (margen <= 0f) continue;          // queda fuera de su alcance
            if (margen <= mejorMargen) continue;

            mejorMargen = margen;
            heard = n;
            encontrado = true;
        }

        return encontrado;
    }

    private static void Limpiar()
    {
        for (int i = _activos.Count - 1; i >= 0; i--)
            if (_activos[i].expiresAt <= Time.time)
                _activos.RemoveAt(i);
    }
}
