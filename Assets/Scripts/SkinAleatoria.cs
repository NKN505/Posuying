using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Da a cada personaje un aspecto aleatorio al aparecer.
///
/// El sorteo lo hace SOLO el servidor y se replica por NetworkVariable. Si cada
/// cliente sorteara el suyo, el mismo enemigo se veria distinto en cada pantalla.
///
/// Un conjunto admite dos formas:
///   - UN material: se aplica a todos los renderers. Sirve para el zombi, que
///     lleva el cuerpo entero con un solo material.
///   - VARIOS: se reparten en orden, uno por renderer. Sirve para personajes
///     por piezas (cabeza, botas, chaleco...) como el Survivalist.
/// </summary>
public class SkinAleatoria : NetworkBehaviour
{
    [System.Serializable]
    public class Conjunto
    {
        public string nombre;
        [Tooltip("Un material se aplica a todo; varios se reparten en orden entre los renderers.")]
        public Material[] materiales;
    }

    [Header("Aspectos")]
    [Tooltip("Conjuntos entre los que se sortea al aparecer. Si esta vacio, no hace nada.")]
    public Conjunto[] conjuntos;

    // -1 = sin sortear todavia. Escribe el servidor, lee todo el mundo.
    private readonly NetworkVariable<int> _elegido = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _elegido.OnValueChanged += AlCambiar;

        if (IsServer && conjuntos != null && conjuntos.Length > 0)
            _elegido.Value = Random.Range(0, conjuntos.Length);

        // quien entra tarde recibe el valor ya puesto, hay que aplicarlo aqui
        Aplicar(_elegido.Value);
    }

    public override void OnNetworkDespawn()
    {
        _elegido.OnValueChanged -= AlCambiar;
        base.OnNetworkDespawn();
    }

    private void AlCambiar(int anterior, int nuevo)
    {
        Aplicar(nuevo);
    }

    private void Aplicar(int indice)
    {
        if (conjuntos == null || indice < 0 || indice >= conjuntos.Length) return;
        var mats = conjuntos[indice].materiales;
        if (mats == null || mats.Length == 0) return;

        var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int cursor = 0;
        foreach (var r in renderers)
        {
            // sharedMaterials y no material: asignar 'material' clonaria uno por
            // renderer y por personaje, y con 30 enemigos en pantalla eso son
            // cientos de materiales instanciados para nada.
            var actuales = r.sharedMaterials;
            for (int i = 0; i < actuales.Length; i++)
            {
                actuales[i] = (mats.Length == 1) ? mats[0]
                                                 : mats[Mathf.Min(cursor, mats.Length - 1)];
                cursor++;
            }
            r.sharedMaterials = actuales;
        }
    }
}
