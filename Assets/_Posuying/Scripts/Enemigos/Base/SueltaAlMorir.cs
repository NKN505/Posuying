using System.Reflection;
using Unity.Netcode;
using UnityEngine;

// Al morir, el enemigo puede dejar municion o un botiquin en el suelo.
//
// Va en el prefab del enemigo, al lado de su script de IA. No toca la muerte en
// si (EnemyBehaviour.Die y el ragdoll): solo escucha, en el servidor, el momento
// en que su vida pasa a cero.
//
// Por que se escucha el cambio de la vida y no se comprueba despues: un enemigo
// sin ragdoll se destruye EN EL MISMO FRAME en que muere, y cuando llega el aviso
// de despawn Netcode ya ha vaciado sus variables (la vida vale 0 y la maxima
// tambien), asi que "estaba muerto" no se puede saber a posteriori. Medido: con
// esa comprobacion tardia ninguno de 40 enemigos solto nada.
public class SueltaAlMorir : NetworkBehaviour
{
    [Header("Municion")]
    [Range(0f, 1f)] public float probabilidadMunicion = 0.30f;

    [Header("Botiquin")]
    [Range(0f, 1f)] public float probabilidadBotiquin = 0.08f;

    [Tooltip("Si sale botiquin no sale municion, y al reves: como mucho un objeto por enemigo")]
    public bool soloUnObjeto = true;

    private Character _personaje;
    // Guardada como 'object' A PROPOSITO: si el campo fuera de tipo NetworkVariable,
    // Netcode lo tomaria por una variable de red de ESTE componente y exigiria que
    // estuviera inicializada ("_vidaRed cannot be null"). Es la vida del Character.
    private object _vidaRed;
    private bool _hecho;

    void Awake()
    {
        _personaje = GetComponent<Character>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer || _personaje == null) return;

        // La vida es privada en Character: se lee por su nombre para no tener que
        // modificar Character (su muerte y su ragdoll no se tocan desde aqui).
        var campo = typeof(Character).GetField("netHealth", BindingFlags.NonPublic | BindingFlags.Instance);
        _vidaRed = campo != null ? campo.GetValue(_personaje) as NetworkVariable<float> : null;

        if (_vidaRed != null) ((NetworkVariable<float>)_vidaRed).OnValueChanged += AlCambiarVida;
        else Debug.LogWarning("SueltaAlMorir: no encuentro 'netHealth' en Character; solo soltara " +
                              "objetos a los enemigos con ragdoll (los que no desaparecen al instante).");
    }

    public override void OnNetworkDespawn()
    {
        if (_vidaRed != null) ((NetworkVariable<float>)_vidaRed).OnValueChanged -= AlCambiarVida;
        _vidaRed = null;
    }

    private void AlCambiarVida(float antes, float ahora)
    {
        if (!_hecho && antes > 0f && ahora <= 0f) Soltar();
    }

    // Red de seguridad por si no se pudo escuchar la vida: sirve para los
    // enemigos que se quedan unos segundos en el suelo como cadaver.
    void Update()
    {
        if (_vidaRed != null || !IsServer || _hecho || !IsSpawned || _personaje == null) return;
        if (_personaje.GetMaxHealth() > 0f && _personaje.GetHealth() <= 0f) Soltar();
    }

    private void Soltar()
    {
        _hecho = true;

        var catalogo = CatalogoObjetosSoltados.Instance;
        if (catalogo == null || NetworkManager == null || NetworkManager.ShutdownInProgress) return;

        Vector3 donde = transform.position;

        if (Random.value < probabilidadBotiquin)
        {
            catalogo.Soltar(catalogo.botiquin, donde);
            if (soloUnObjeto) return;
            donde += transform.right * 0.6f;   // que no salgan uno dentro del otro
        }

        if (Random.value < probabilidadMunicion)
            catalogo.Soltar(catalogo.municion, donde);
    }
}
