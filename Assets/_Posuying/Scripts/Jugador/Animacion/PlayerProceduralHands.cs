using UnityEngine;

/// <summary>
/// Hand IK: fija las manos del cuerpo de tercera persona a los puntos de agarre
/// del arma equipada.
///
/// Va en el mismo GameObject que el Animator (la raiz del Player).
///
/// El arma activa se busca sola bajo el Weapon Holder, y de ella se leen los
/// puntos de agarre de su componente WeaponIKTargets. Cada arma lleva los suyos,
/// asi que cambiar de arma cambia el agarre sin tocar este script.
///
/// NO choca con los otros dos componentes procedurales:
///   - Este escribe manos en OnAnimatorIK.
///   - PlayerProceduralFeet escribe pies y cadera, tambien en OnAnimatorIK.
///   - PlayerProceduralAim escribe la columna en LateUpdate.
/// Unity aplica el IK de cada extremidad por separado, asi que no se pisan.
///
/// REQUISITO: "IK Pass" activado en la capa del Animator Controller. Ya lo esta.
/// </summary>
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public class PlayerProceduralHands : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private Animator animator;

    [Tooltip("Weapon Holder. Si se deja vacio se busca en 'Main Camera/Weapon Holder'.")]
    [SerializeField] private Transform weaponHolder;

    [Header("Ajuste")]
    [Tooltip("0 desactiva el IK de manos sin quitar el componente.")]
    [Range(0f, 1f)] [SerializeField] private float peso = 1f;

    [Tooltip("Cuanto se fuerza la ROTACION de la mano. Bajalo si las muñecas se retuercen.")]
    [Range(0f, 1f)] [SerializeField] private float pesoRotacion = 1f;

    [Tooltip("Suavizado al cambiar de arma o al soltar el agarre.")]
    [SerializeField] private float velocidadSuavizado = 12f;

    private WeaponIKTargets _agarreActual;
    private float _pesoIzq, _pesoDer;
    private bool _listo;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();

        if (weaponHolder == null)
        {
            var t = transform.Find("Main Camera/Weapon Holder");
            if (t != null) weaponHolder = t;
        }

        if (animator == null || !animator.isHuman)
        {
            Debug.LogWarning("[PlayerProceduralHands] Hace falta un Animator humanoide.", this);
            return;
        }

        _listo = weaponHolder != null;

        if (!_listo)
            Debug.LogWarning("[PlayerProceduralHands] No encuentro el Weapon Holder.", this);
    }

    private void OnAnimatorIK(int capa)
    {
        if (!_listo) return;

        _agarreActual = BuscarAgarreActivo();
        float dt = Time.deltaTime;

        float pesoArma = (_agarreActual != null) ? _agarreActual.peso * peso : 0f;

        bool hayIzq = _agarreActual != null && _agarreActual.manoIzquierda != null;
        bool hayDer = _agarreActual != null && _agarreActual.manoDerecha != null;

        // El peso se suaviza en vez de saltar de 0 a 1: al cambiar de arma la
        // mano viaja al nuevo agarre en vez de teletransportarse.
        _pesoIzq = Mathf.Lerp(_pesoIzq, hayIzq ? pesoArma : 0f, Factor(velocidadSuavizado, dt));
        _pesoDer = Mathf.Lerp(_pesoDer, hayDer ? pesoArma : 0f, Factor(velocidadSuavizado, dt));

        Aplicar(AvatarIKGoal.LeftHand, hayIzq ? _agarreActual.manoIzquierda : null, _pesoIzq);
        Aplicar(AvatarIKGoal.RightHand, hayDer ? _agarreActual.manoDerecha : null, _pesoDer);
    }

    private void Aplicar(AvatarIKGoal meta, Transform objetivo, float pesoMano)
    {
        if (pesoMano <= 0.001f || objetivo == null)
        {
            animator.SetIKPositionWeight(meta, 0f);
            animator.SetIKRotationWeight(meta, 0f);
            return;
        }

        animator.SetIKPositionWeight(meta, pesoMano);
        animator.SetIKPosition(meta, objetivo.position);

        animator.SetIKRotationWeight(meta, pesoMano * pesoRotacion);
        animator.SetIKRotation(meta, objetivo.rotation);
    }

    /// <summary>
    /// Solo cuenta el arma activa en jerarquia: al cambiar de arma se desactiva
    /// una y se activa otra. Mismo criterio que usa PlayerAnimation.
    /// </summary>
    private WeaponIKTargets BuscarAgarreActivo()
    {
        var candidatos = weaponHolder.GetComponentsInChildren<WeaponIKTargets>(false);
        for (int i = 0; i < candidatos.Length; i++)
        {
            if (candidatos[i].isActiveAndEnabled) return candidatos[i];
        }
        return null;
    }

    /// <summary>Suavizado independiente de los FPS.</summary>
    private static float Factor(float velocidad, float dt)
    {
        return dt <= 0f ? 1f : 1f - Mathf.Exp(-velocidad * dt);
    }
}
