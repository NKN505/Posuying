using UnityEngine;

/// <summary>
/// Cambia el set de animaciones segun el arma que porta el jugador y alimenta
/// los parametros de combate del Animator.
///
/// La idea: UN solo Animator Controller con el blend tree, los saltos y la
/// muerte, y un AnimatorOverrideController por tipo de agarre que solo
/// sustituye los clips. Cambiar de arma = cambiar el override.
///
/// Colocar en el mismo GameObject que el Animator (el Player).
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerAnimation : MonoBehaviour
{
    [Header("Animator")]
    [SerializeField] private Animator animator;

    [Header("Sets de animacion por agarre")]
    [Tooltip("Controller base. Es el que define la maquina de estados.")]
    [SerializeField] private RuntimeAnimatorController controllerBase;
    [Tooltip("Manos vacias.")]
    [SerializeField] private AnimatorOverrideController setUnarmed;
    [Tooltip("Pistola: una mano. DEJAR VACIO si el controller base ya es el set " +
             "de pistola, que es el caso por defecto.")]
    [SerializeField] private AnimatorOverrideController setOneHanded;
    [Tooltip("Fusil o escopeta: dos manos.")]
    [SerializeField] private AnimatorOverrideController setTwoHanded;
    [Tooltip("Cuchillo.")]
    [SerializeField] private AnimatorOverrideController setMelee;

    [Header("Donde buscar el arma")]
    [Tooltip("Weapon Holder. Se busca el primer Weapon activo debajo.")]
    [SerializeField] private Transform weaponHolder;

    // Hashes cacheados
    private static readonly int HashIsAiming  = Animator.StringToHash("IsAiming");
    private static readonly int HashShoot     = Animator.StringToHash("Shoot");
    private static readonly int HashReload    = Animator.StringToHash("Reload");
    private static readonly int HashGrip      = Animator.StringToHash("Grip");

    private Weapon armaActual;
    private WeaponGrip agarreActual = (WeaponGrip)(-1);   // fuerza el primer cambio
    private bool disparoAnterior;
    private bool recargaAnterior;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        if (weaponHolder == null)
        {
            var t = transform.Find("Main Camera/Weapon Holder");
            if (t != null) weaponHolder = t;
        }
    }

    private void Update()
    {
        if (animator == null || !animator.isActiveAndEnabled) return;

        Weapon arma = BuscarArmaActiva();

        // --- cambio de set cuando cambia el agarre ---
        WeaponGrip agarre = arma != null ? arma.GetGrip() : WeaponGrip.Unarmed;
        if (agarre != agarreActual)
        {
            AplicarSet(agarre);
            agarreActual = agarre;
        }
        armaActual = arma;

        // --- parametros de combate ---
        bool apuntando  = arma != null && arma.GetIsAiming();
        bool disparando = arma != null && arma.GetIsShooting();
        bool recargando = arma != null && arma.GetIsReloading();

        animator.SetBool(HashIsAiming, apuntando);

        // Flanco de subida: el trigger solo debe dispararse al empezar la accion,
        // no en todos los frames que dure.
        if (disparando && !disparoAnterior) animator.SetTrigger(HashShoot);
        if (recargando && !recargaAnterior) animator.SetTrigger(HashReload);

        disparoAnterior = disparando;
        recargaAnterior = recargando;
    }

    private Weapon BuscarArmaActiva()
    {
        if (weaponHolder == null) return null;

        // Solo el arma activa en jerarquia cuenta: al cambiar de arma se
        // desactiva una y se activa otra.
        var armas = weaponHolder.GetComponentsInChildren<Weapon>(false);
        for (int i = 0; i < armas.Length; i++)
        {
            if (armas[i].isActiveAndEnabled)
                return armas[i];
        }
        return null;
    }

    private void AplicarSet(WeaponGrip agarre)
    {
        RuntimeAnimatorController nuevo = ElegirSet(agarre);
        if (nuevo == null || animator.runtimeAnimatorController == nuevo)
            return;

        // IMPORTANTE: asignar runtimeAnimatorController reinicia la maquina de
        // estados. Sin esto, cambiar de arma corriendo te devolveria a Idle de
        // golpe. Guardamos donde estabamos y lo restauramos.
        var estado = animator.GetCurrentAnimatorStateInfo(0);
        int hash = estado.fullPathHash;
        float t = estado.normalizedTime;

        animator.runtimeAnimatorController = nuevo;
        animator.Play(hash, 0, t);
        animator.Update(0f);

        animator.SetInteger(HashGrip, (int)agarre);
    }

    private RuntimeAnimatorController ElegirSet(WeaponGrip agarre)
    {
        switch (agarre)
        {
            case WeaponGrip.OneHanded: return setOneHanded != null ? setOneHanded : controllerBase;
            case WeaponGrip.TwoHanded: return setTwoHanded != null ? setTwoHanded : controllerBase;
            case WeaponGrip.Melee:     return setMelee     != null ? setMelee     : controllerBase;
            default:                   return setUnarmed   != null ? setUnarmed   : controllerBase;
        }
    }

    /// <summary>Agarre activo, por si otro sistema lo necesita (IK, camara...).</summary>
    public WeaponGrip GetAgarreActual()
    {
        return agarreActual;
    }
}
