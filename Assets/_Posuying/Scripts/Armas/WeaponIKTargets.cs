using UnityEngine;

/// <summary>
/// Marca en un arma donde tienen que agarrarla las manos.
///
/// Se pone en el GameObject del arma. Debajo de el se crean uno o dos empties
/// colocados y ROTADOS como quieres que quede la mano: la posicion manda donde
/// va la muñeca, y la rotacion como queda girada la palma.
///
/// Para colocarlos: pon el personaje en la pose de idle, arrastra el empty
/// hasta el guardamanos y gíralo hasta que la mano quede natural.
/// </summary>
[DisallowMultipleComponent]
public class WeaponIKTargets : MonoBehaviour
{
    [Tooltip("Donde agarra la mano izquierda. Guardamanos, corredera, empuñadura delantera.")]
    public Transform manoIzquierda;

    [Tooltip("Opcional. Solo si tambien quieres forzar la mano derecha.")]
    public Transform manoDerecha;

    [Tooltip("Cuanto se aplica el IK a esta arma concreta. 0 lo desactiva solo para ella.")]
    [Range(0f, 1f)] public float peso = 1f;
}
