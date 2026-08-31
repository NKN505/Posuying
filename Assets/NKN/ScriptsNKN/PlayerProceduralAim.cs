using UnityEngine;

/// <summary>
/// Movimiento procedural del torso, encima de lo que escriba el Animator.
///
///   AIM   -> el cuerpo acompaña el pitch de la camara al mirar arriba o abajo.
///   LEAN  -> el cuerpo se inclina hacia el lado al girar o al desplazarse en lateral.
///
/// Van los dos en el MISMO componente a proposito: ambos escriben sobre los
/// huesos de la columna en la misma fase del frame. En scripts separados se
/// pisarian el uno al otro segun el orden de ejecucion.
///
/// Va en el mismo GameObject que el Animator (la raiz del Player).
///
/// Se aplica en LateUpdate, DESPUES de que el Animator haya escrito la pose.
/// Si se hiciera antes, el Animator lo sobrescribiria en el mismo frame.
///
/// Rig verificado: genSWAT es Humanoid con Spine, Chest y Head mapeados, y
/// optimizeGameObjects a 0, asi que GetBoneTransform devuelve Transforms reales.
/// </summary>
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public class PlayerProceduralAim : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Camara del jugador. Si se deja vacia se busca en PlayerController o entre los hijos.")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Animator animator;

    [Header("Reparto del giro entre huesos")]
    [Tooltip("Suman 1. Repartir evita el doblez brusco de rotar un solo hueso.")]
    [Range(0f, 1f)] [SerializeField] private float pesoSpine = 0.30f;
    [Range(0f, 1f)] [SerializeField] private float pesoChest = 0.40f;
    [Range(0f, 1f)] [SerializeField] private float pesoHead  = 0.30f;

    [Header("Aim (pitch de la camara)")]
    [SerializeField] private bool aimActivo = true;
    [Tooltip("Tope en grados. El torso humano no llega a 90.")]
    [SerializeField] private float anguloMaximo = 60f;
    [Tooltip("Suavizado. Mas alto = mas pegado a la camara, mas brusco.")]
    [SerializeField] private float velocidadSuavizado = 12f;
    [Tooltip("Si el cuerpo se inclina al reves, marcar esto.")]
    [SerializeField] private bool invertir = false;

    [Header("Lean (inclinacion lateral)")]
    [SerializeField] private bool leanActivo = true;
    [Tooltip("Grados de inclinacion por cada 100 grados/segundo de giro de camara.")]
    [SerializeField] private float leanPorGiro = 6f;
    [Tooltip("Grados de inclinacion al desplazarse en lateral a tope. 0 lo desactiva.")]
    [SerializeField] private float leanPorStrafe = 3f;
    [Tooltip("Tope de inclinacion en grados.")]
    [SerializeField] private float leanMaximo = 10f;
    [Tooltip("Suavizado del lean. Mas bajo = mas perezoso, se nota mas la inercia.")]
    [SerializeField] private float leanSuavizado = 6f;
    [Tooltip("Si se inclina hacia el lado contrario, marcar esto.")]
    [SerializeField] private bool leanInvertido = false;

    // La cabeza no se inclina con el lean: en la practica queda raro, la gente
    // mantiene la cabeza mas o menos vertical al girar.
    [Range(0f, 1f)] [SerializeField] private float leanPesoHead = 0f;

    private Transform _spine, _chest, _head;
    private float _anguloAim;
    private float _anguloLean;
    private float _yawAnterior;
    private bool _listo;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();

        if (playerCamera == null)
        {
            var controller = GetComponent<PlayerController>();
            if (controller != null) playerCamera = controller.playerCamera;
        }
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>(true);

        if (animator == null || !animator.isHuman)
        {
            Debug.LogWarning("[PlayerProceduralAim] Hace falta un Animator humanoide.", this);
            return;
        }

        _spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        _head  = animator.GetBoneTransform(HumanBodyBones.Head);

        _listo = playerCamera != null && (_spine != null || _chest != null || _head != null);
        _yawAnterior = transform.eulerAngles.y;

        if (!_listo)
            Debug.LogWarning("[PlayerProceduralAim] Falta la camara o los huesos de la columna.", this);
    }

    private void LateUpdate()
    {
        if (!_listo) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        CalcularAim(dt);
        CalcularLean(dt);

        // ORDEN: primero el pitch, luego la inclinacion lateral. Al reves el lean
        // se aplicaria sobre un eje ya inclinado y el resultado se retuerce.
        if (aimActivo && !Mathf.Approximately(_anguloAim, 0f))
        {
            Vector3 eje = transform.right;
            Rotar(_spine, _anguloAim * pesoSpine, eje);
            Rotar(_chest, _anguloAim * pesoChest, eje);
            Rotar(_head,  _anguloAim * pesoHead,  eje);
        }

        if (leanActivo && !Mathf.Approximately(_anguloLean, 0f))
        {
            Vector3 eje = transform.forward;
            Rotar(_spine, _anguloLean * pesoSpine, eje);
            Rotar(_chest, _anguloLean * pesoChest, eje);
            Rotar(_head,  _anguloLean * leanPesoHead, eje);
        }
    }

    private void CalcularAim(float dt)
    {
        // El pitch vive en la rotacion local de la camara (PlayerController la
        // escribe cada frame). Se normaliza a -180..180 porque localEulerAngles
        // devuelve 0..360 y 350 grados serian en realidad -10.
        float pitch = playerCamera.transform.localEulerAngles.x;
        if (pitch > 180f) pitch -= 360f;

        float objetivo = Mathf.Clamp(pitch, -anguloMaximo, anguloMaximo);
        if (invertir) objetivo = -objetivo;

        _anguloAim = Mathf.Lerp(_anguloAim, objetivo, Factor(velocidadSuavizado, dt));
    }

    private void CalcularLean(float dt)
    {
        // Giro: cuanto ha rotado la raiz este frame. El raton rota la raiz entera,
        // asi que esto captura el giro de camara en horizontal.
        float yaw = transform.eulerAngles.y;
        float delta = Mathf.DeltaAngle(_yawAnterior, yaw);
        _yawAnterior = yaw;

        float gradosPorSegundo = delta / dt;
        float objetivo = -(gradosPorSegundo / 100f) * leanPorGiro;

        // Strafe: inclinacion adicional segun el desplazamiento lateral, leida del
        // mismo parametro que alimenta el blend tree para no depender del input.
        if (!Mathf.Approximately(leanPorStrafe, 0f))
        {
            float moveX = animator.GetFloat("MoveX");
            objetivo += -Mathf.Clamp(moveX / 5f, -1f, 1f) * leanPorStrafe;
        }

        objetivo = Mathf.Clamp(objetivo, -leanMaximo, leanMaximo);
        if (leanInvertido) objetivo = -objetivo;

        _anguloLean = Mathf.Lerp(_anguloLean, objetivo, Factor(leanSuavizado, dt));
    }

    /// <summary>Suavizado independiente de los FPS.</summary>
    private static float Factor(float velocidad, float dt)
    {
        return 1f - Mathf.Exp(-velocidad * dt);
    }

    private static void Rotar(Transform hueso, float grados, Vector3 eje)
    {
        if (hueso == null || Mathf.Approximately(grados, 0f)) return;
        hueso.rotation = Quaternion.AngleAxis(grados, eje) * hueso.rotation;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Aviso, no correccion: normalizar por detras confunde al ajustar valores.
        float suma = pesoSpine + pesoChest + pesoHead;
        if (Mathf.Abs(suma - 1f) > 0.01f)
            Debug.LogWarning("[PlayerProceduralAim] Los pesos suman " + suma.ToString("F2") + ", no 1.", this);
    }
#endif
}
