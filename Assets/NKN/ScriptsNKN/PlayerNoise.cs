using Unity.Netcode;
using UnityEngine;

/// <summary>
/// El ruido que hace un jugador. Va en el prefab del Player.
///
/// LAS PISADAS NO VIAJAN POR LA RED. El servidor ya conoce la posición de todos
/// los jugadores (se la trae NetworkTransform), así que mide su velocidad y
/// deduce las pisadas él solo. Mandar un RPC por cada paso serían unos diez
/// mensajes por segundo con cuatro jugadores, para algo que el servidor puede
/// calcular gratis.
///
/// Además esto hace que el sigilo salga solo: no hay que replicar si vas
/// agachado, porque ir agachado ES ir despacio, y despacio suena menos.
///
/// Los ruidos puntuales (disparo, salto, melé) sí se avisan, porque son eventos
/// que el servidor no puede adivinar mirando la posición. Pero son esporádicos.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerNoise : NetworkBehaviour
{
    [Header("Ruido al moverse (radio en metros)")]
    [Tooltip("Yendo agachado o muy despacio")]
    public float sneakRadius = 4f;
    [Tooltip("Andando a ritmo normal")]
    public float walkRadius = 11f;
    [Tooltip("Esprintando")]
    public float sprintRadius = 20f;

    [Header("Velocidades de referencia (m/s)")]
    [Tooltip("Por debajo de esto no hace ruido al moverse")]
    public float silentSpeed = 0.4f;
    public float sneakSpeed = 2.5f;
    public float sprintSpeed = 9f;

    [Tooltip("Cada cuántos metros recorridos suena una pisada. Al medirlo en " +
             "distancia y no en tiempo, correr genera más pisadas por segundo " +
             "sin tener que ajustar ningún intervalo.")]
    public float strideLength = 1.8f;

    [Header("Ruidos puntuales (radio en metros)")]
    public float shotRadius = 45f;
    public float meleeRadius = 8f;
    public float jumpRadius = 6f;
    public float landRadius = 12f;

    private Vector3 _posicionAnterior;
    private float _distanciaAcumulada;
    private PlayerDownedState _abatido;
    private float _velocidad;

    /// <summary>
    /// Velocidad horizontal medida por el SERVIDOR, suavizada. La usan los
    /// escondites: esconderse exige estarse quieto, y "quieto" tiene que poder
    /// comprobarlo el servidor, no el cliente.
    /// </summary>
    public float ObservedSpeed { get { return _velocidad; } }

    public override void OnNetworkSpawn()
    {
        _posicionAnterior = transform.position;
        _abatido = GetComponent<PlayerDownedState>();
    }

    void Update()
    {
        // Solo el servidor: es quien tiene la IA que va a escuchar esto
        if (!IsServer) return;

        RuidoAlMoverse();
    }

    private void RuidoAlMoverse()
    {
        Vector3 delta = transform.position - _posicionAnterior;
        delta.y = 0f;                      // caer no cuenta como andar
        _posicionAnterior = transform.position;

        if (Time.deltaTime <= 0f) return;

        // Un jugador en el suelo no hace ruido de pasos
        if (_abatido != null && !_abatido.CanAct) return;

        float velocidad = delta.magnitude / Time.deltaTime;
        _velocidad = Mathf.Lerp(_velocidad, velocidad, 10f * Time.deltaTime);

        if (velocidad < silentSpeed) { _distanciaAcumulada = 0f; return; }

        _distanciaAcumulada += delta.magnitude;
        if (_distanciaAcumulada < strideLength) return;

        _distanciaAcumulada = 0f;
        NoiseSystem.Emit(transform.position, RadioSegunVelocidad(velocidad), transform);
    }

    // De sigilo a esprint, interpolado. Ir agachado no es un estado especial:
    // simplemente es ir despacio, y despacio suena poco.
    private float RadioSegunVelocidad(float velocidad)
    {
        if (velocidad <= sneakSpeed)
        {
            float t = Mathf.InverseLerp(silentSpeed, sneakSpeed, velocidad);
            return Mathf.Lerp(sneakRadius * 0.5f, sneakRadius, t);
        }

        float u = Mathf.InverseLerp(sneakSpeed, sprintSpeed, velocidad);
        return Mathf.Lerp(walkRadius, sprintRadius, u);
    }

    // ---------- Ruidos puntuales ----------

    /// <summary>
    /// Hace un ruido aquí y ahora. Se puede llamar desde cualquier máquina: si
    /// no somos el servidor, se le avisa.
    /// </summary>
    public void Make(float radius)
    {
        if (radius <= 0f) return;

        if (IsServer) NoiseSystem.Emit(transform.position, radius, transform);
        else if (IsOwner) MakeServerRpc(radius);
    }

    public void MakeShot()  { Make(shotRadius); }
    public void MakeMelee() { Make(meleeRadius); }
    public void MakeJump()  { Make(jumpRadius); }
    public void MakeLand()  { Make(landRadius); }

    // Solo se manda el radio: la posición la sabe ya el servidor, así que no
    // hace falta enviarla (y de paso no se puede falsear desde el cliente).
    [ServerRpc]
    private void MakeServerRpc(float radius)
    {
        if (_abatido != null && !_abatido.CanAct) return;
        NoiseSystem.Emit(transform.position, radius, transform);
    }

    /// <summary>Atajo para no repetir el GetComponent en cada punto de llamada.</summary>
    public static void MakeAt(GameObject quien, float radius)
    {
        if (quien == null) return;
        var n = quien.GetComponent<PlayerNoise>();
        if (n != null) n.Make(radius);
    }
}
