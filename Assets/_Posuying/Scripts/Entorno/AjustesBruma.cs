using UnityEngine;

// Los valores de la bruma (ver Bruma.cs). Es un asset para poder tocarlos desde el
// Inspector sin abrir la escena: Assets/_Posuying/Resources/Bruma/AjustesBruma.
// Los cambios se ven al momento, tambien con el juego en marcha.
//
// Lo que elige cada jugador (calidad y densidad) esta en Opciones > Graficos.
[CreateAssetMenu(menuName = "Posuying/Ajustes de bruma", fileName = "AjustesBruma")]
public class AjustesBruma : ScriptableObject
{
    [Tooltip("Apaga toda la bruma y deja la niebla que tenga guardada la escena")]
    public bool activa = true;
    [Tooltip("Escenas en las que hay bruma")]
    public string[] escenas = { "Mapa" };

    [Header("Hasta donde se ve")]
    [Tooltip("Metros a los que las cosas se pierden del todo cuando la bruma esta mas abierta")]
    public float visibilidadMaxima = 42f;
    [Tooltip("Metros a los que se pierden cuando se cierra")]
    public float visibilidadMinima = 24f;
    [Tooltip("Segundos, mas o menos, que tarda en pasar de abierta a cerrada")]
    public float segundosDeCambio = 45f;

    [Header("Color")]
    public Color color = new Color(0.115f, 0.13f, 0.155f);
    [Tooltip("El cielo se tapa con el color de la bruma. Sin esto se veria la luna " +
             "nitida detras de edificios que ya se han perdido")]
    public bool taparCielo = true;

    [Header("Volumen (la luz se ve en el aire)")]
    [Tooltip("Cuanta niebla hay para que la luz se vea en ella")]
    [Range(0f, 1f)] public float densidadVolumen = 0.35f;
    [Tooltip("Cuanto brilla en la bruma el haz de linternas y demas focos")]
    [Range(0f, 16f)] public float brilloFocos = 0.1f;
    [Tooltip("Cuanto brillan en la bruma farolas, bombillas y demas luces puntuales")]
    [Range(0f, 16f)] public float brilloPuntuales = 0.15f;
    [Tooltip("0 = el haz brilla igual desde cualquier lado. Cerca de 1 = solo " +
             "brilla mirando hacia la luz")]
    [Range(0f, 0.9f)] public float concentracion = 0.3f;
    [Tooltip("Cuanto ilumina la luna la bruma (con sombras: rayos entre edificios). 0 = nada")]
    [Range(0f, 1f)] public float luzDeLuna = 0.12f;
    [Tooltip("Metros por encima de la camara a los que el volumen se queda sin niebla")]
    public float alturaVolumen = 25f;

    [Header("Rendimiento")]
    [Tooltip("No dibujar lo que queda mas alla de la bruma (de todas formas no se ve)")]
    public bool recortarDistancia = true;
    [Tooltip("Pasos por pixel en calidad Baja, Media y Alta (Opciones > Graficos)")]
    public Vector3Int pasos = new Vector3Int(24, 48, 96);
}
