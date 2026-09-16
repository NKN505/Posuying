using UnityEngine;

/// <summary>
/// Foot IK: adapta al terreno el pie que la ANIMACION tiene apoyado, y deja
/// libre el que va por el aire. Baja la cadera cuando hace falta para alcanzar
/// un pie que queda mas abajo (escalones, rampas).
///
/// Va en el mismo GameObject que el Animator (la raiz del Player), junto a
/// PlayerProceduralAim.
///
/// REQUISITO: "IK Pass" activado en la capa del Animator Controller.
///
/// -------------------------------------------------------------------------
/// COMO DECIDE SI UN PIE ESTA APOYADO
/// -------------------------------------------------------------------------
/// El criterio NO puede ser la distancia al suelo. Un pie en un escalon mas
/// bajo y un pie en mitad de la zancada estan los dos por encima del suelo que
/// detecta el rayo: mirando solo eso, o se sueltan los dos (se pierde la
/// adaptacion a escalones) o se pegan los dos (el personaje anda agachado y
/// arrastrando). Las dos cosas nos han pasado.
///
/// El criterio bueno es la altura que el CLIP le da al pie respecto a la base
/// del personaje:
///
///   pie bajo en la animacion  -> apoyado -> se lleva al suelo, suba o baje
///   pie alto en la animacion  -> zancada -> libre
///
/// Asi el pie que baja un escalon sigue pegado (en la animacion esta apoyado),
/// y el que va por el aire se suelta (en la animacion esta levantado).
/// </summary>
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public class PlayerProceduralFeet : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private Animator animator;

    [Header("Suelo")]
    [Tooltip("Que capas cuentan como suelo. Excluye la del propio jugador.")]
    [SerializeField] private LayerMask capasSuelo = ~0;
    [Tooltip("Cuanto se busca hacia arriba desde el tobillo antes de lanzar el rayo.")]
    [SerializeField] private float alturaOrigenRayo = 0.7f;
    [Tooltip("Longitud del rayo desde ese origen. Debe cubrir el escalon mas alto.")]
    [SerializeField] private float longitudRayo = 1.6f;

    [Header("Que cuenta como pie apoyado")]
    [Tooltip("Altura del pie sobre la base del personaje por debajo de la cual se " +
             "considera APOYADO del todo. Subelo si en escalones se despega.")]
    [SerializeField] private float alturaApoyo = 0.12f;
    [Tooltip("Margen por encima de la anterior en el que el peso se desvanece. " +
             "Bajalo si al andar en plano los pies se arrastran.")]
    [SerializeField] private float margenZancada = 0.16f;

    [Header("Ajuste")]
    [Tooltip("Distancia del hueso del tobillo a la planta del pie. Si el pie se hunde, subir.")]
    [SerializeField] private float offsetTobillo = 0.12f;
    [Tooltip("Cuanto puede alejarse el pie de donde lo pone el clip. Evita estirones absurdos.")]
    [SerializeField] private float alcanceMaximo = 0.6f;
    [Tooltip("Cuanto puede bajar la cadera para alcanzar el pie apoyado mas bajo.")]
    [SerializeField] private float bajadaMaximaCadera = 0.4f;
    [Tooltip("0 desactiva el IK, 1 lo aplica entero.")]
    [Range(0f, 1f)] [SerializeField] private float peso = 1f;
    [Tooltip("Suavizado. Mas alto = mas pegado al terreno, mas nervioso.")]
    [SerializeField] private float velocidadSuavizado = 15f;

    [Header("Opciones")]
    [Tooltip("Alinear la rotacion del pie con la inclinacion del suelo.")]
    [SerializeField] private bool rotarPies = true;
    [Tooltip("Rayos en la Scene view. Verde = apoyado, amarillo = a medias, rojo = zancada.")]
    [SerializeField] private bool depurar = false;

    private Transform _pieIzq, _pieDer;
    private float _pesoIzq, _pesoDer;
    private Quaternion _rotIzq, _rotDer;
    private float _bajadaCadera;
    private bool _listo;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();

        if (animator == null || !animator.isHuman)
        {
            Debug.LogWarning("[PlayerProceduralFeet] Hace falta un Animator humanoide.", this);
            return;
        }

        _pieIzq = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        _pieDer = animator.GetBoneTransform(HumanBodyBones.RightFoot);

        _listo = _pieIzq != null && _pieDer != null;
        _rotIzq = _rotDer = Quaternion.identity;

        if (!_listo)
            Debug.LogWarning("[PlayerProceduralFeet] No encuentro los huesos de los pies.", this);
    }

    private void OnAnimatorIK(int capa)
    {
        if (!_listo || peso <= 0f) return;

        bool enSuelo = animator.GetBool("IsGrounded");
        float dt = Time.deltaTime;
        float baseY = transform.position.y;   // la raiz del Player esta a los pies

        float alturaIzq, alturaDer;
        Quaternion rotIzq, rotDer;
        bool hayIzq = Sondear(_pieIzq, baseY, out alturaIzq, out rotIzq);
        bool hayDer = Sondear(_pieDer, baseY, out alturaDer, out rotDer);

        // Peso segun lo APOYADO que este el pie en el clip, no segun el suelo.
        float apoyoIzq = Apoyo(_pieIzq.position.y - baseY);
        float apoyoDer = Apoyo(_pieDer.position.y - baseY);

        float objIzq = (enSuelo && hayIzq) ? apoyoIzq * peso : 0f;
        float objDer = (enSuelo && hayDer) ? apoyoDer * peso : 0f;

        _pesoIzq = Mathf.Lerp(_pesoIzq, objIzq, Factor(velocidadSuavizado, dt));
        _pesoDer = Mathf.Lerp(_pesoDer, objDer, Factor(velocidadSuavizado, dt));

        // Cadera: la baja el pie apoyado cuyo suelo quede por debajo de donde el
        // clip pone el pie. En plano sale 0 y la cadera no se toca.
        float bajadaObjetivo = 0f;
        if (enSuelo)
        {
            if (objIzq > 0.5f) bajadaObjetivo = Mathf.Min(bajadaObjetivo, alturaIzq - _pieIzq.position.y);
            if (objDer > 0.5f) bajadaObjetivo = Mathf.Min(bajadaObjetivo, alturaDer - _pieDer.position.y);
            bajadaObjetivo = Mathf.Clamp(bajadaObjetivo, -bajadaMaximaCadera, 0f);
        }
        _bajadaCadera = Mathf.Lerp(_bajadaCadera, bajadaObjetivo, Factor(velocidadSuavizado, dt));

        if (Mathf.Abs(_bajadaCadera) > 0.001f)
            animator.bodyPosition += Vector3.up * _bajadaCadera;

        AplicarPie(AvatarIKGoal.LeftFoot, _pieIzq, _pesoIzq, alturaIzq, rotIzq, ref _rotIzq, dt);
        AplicarPie(AvatarIKGoal.RightFoot, _pieDer, _pesoDer, alturaDer, rotDer, ref _rotDer, dt);
    }

    /// <summary>
    /// 1 si el clip tiene el pie abajo (apoyado), 0 si lo tiene levantado
    /// (zancada), con un desvanecido entre medias.
    /// </summary>
    private float Apoyo(float alturaSobreBase)
    {
        if (alturaSobreBase <= alturaApoyo) return 1f;
        if (margenZancada <= 0f) return 0f;
        return Mathf.Clamp01(1f - (alturaSobreBase - alturaApoyo) / margenZancada);
    }

    private bool Sondear(Transform pie, float baseY, out float alturaTobillo, out Quaternion rotacion)
    {
        alturaTobillo = pie.position.y;
        rotacion = Quaternion.identity;

        Vector3 origen = pie.position + Vector3.up * alturaOrigenRayo;

        RaycastHit hit;
        bool tocado = Physics.Raycast(origen, Vector3.down, out hit, longitudRayo, capasSuelo,
                                      QueryTriggerInteraction.Ignore);

        if (depurar)
        {
            float a = Apoyo(pie.position.y - baseY);
            Color c = !tocado ? Color.gray : (a > 0.9f ? Color.green : (a > 0.1f ? Color.yellow : Color.red));
            Debug.DrawRay(origen, Vector3.down * longitudRayo, c);
        }

        if (!tocado) return false;

        // Se limita cuanto puede alejarse el pie de donde lo pone el clip, para
        // que un rayo que se cuela por un hueco no estire la pierna al vacio.
        float deseada = hit.point.y + offsetTobillo;
        alturaTobillo = Mathf.Clamp(deseada,
                                    pie.position.y - alcanceMaximo,
                                    pie.position.y + alcanceMaximo);

        rotacion = Quaternion.FromToRotation(Vector3.up, hit.normal);
        return true;
    }

    private void AplicarPie(AvatarIKGoal meta, Transform pie, float pesoPie,
                            float alturaObjetivo, Quaternion rotObjetivo,
                            ref Quaternion rotSuave, float dt)
    {
        animator.SetIKPositionWeight(meta, pesoPie);

        if (pesoPie <= 0.001f)
        {
            animator.SetIKRotationWeight(meta, 0f);
            return;
        }

        Vector3 destino = pie.position;
        destino.y = alturaObjetivo;
        animator.SetIKPosition(meta, destino);

        if (!rotarPies)
        {
            animator.SetIKRotationWeight(meta, 0f);
            return;
        }

        rotSuave = Quaternion.Slerp(rotSuave, rotObjetivo, Factor(velocidadSuavizado, dt));
        animator.SetIKRotationWeight(meta, pesoPie);
        animator.SetIKRotation(meta, rotSuave * animator.GetIKRotation(meta));
    }

    /// <summary>Suavizado independiente de los FPS.</summary>
    private static float Factor(float velocidad, float dt)
    {
        return dt <= 0f ? 1f : 1f - Mathf.Exp(-velocidad * dt);
    }
}
