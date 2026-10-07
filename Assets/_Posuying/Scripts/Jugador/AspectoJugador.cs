using Unity.Netcode;
using UnityEngine;

// El aspecto que elige cada jugador en la sala de espera: de momento, el color
// del uniforme. Lo escribe su dueno y lo ven todos.
//
// Se tinta el material del cuerpo con un MaterialPropertyBlock: no crea
// materiales nuevos ni toca el que comparten todos los jugadores.
//
// Va en el prefab del jugador. La eleccion se guarda en el equipo (PlayerProfile).
public class AspectoJugador : NetworkBehaviour
{
    public struct Opcion
    {
        public string nombre;
        public Color color;
        public Opcion(string n, Color c) { nombre = n; color = c; }
    }

    // El primero deja el uniforme tal cual. Tonos apagados: se multiplican por la
    // textura de camuflaje, y un color puro quedaria de juguete.
    public static readonly Opcion[] Opciones =
    {
        new Opcion("Original", Color.white),
        new Opcion("Rojo",     new Color(0.95f, 0.45f, 0.40f)),
        new Opcion("Azul",     new Color(0.45f, 0.62f, 0.98f)),
        new Opcion("Verde",    new Color(0.50f, 0.90f, 0.50f)),
        new Opcion("Amarillo", new Color(0.98f, 0.88f, 0.42f)),
        new Opcion("Morado",   new Color(0.74f, 0.52f, 0.95f)),
        new Opcion("Naranja",  new Color(1.00f, 0.66f, 0.36f)),
        new Opcion("Negro",    new Color(0.35f, 0.35f, 0.38f)),
    };

    private readonly NetworkVariable<int> netColor = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public int Indice => Mathf.Clamp(netColor.Value, 0, Opciones.Length - 1);
    public Color ColorActual => Opciones[Indice].color;

    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock _bloque;

    public override void OnNetworkSpawn()
    {
        netColor.OnValueChanged += AlCambiar;
        if (IsOwner) netColor.Value = Mathf.Clamp(PlayerProfile.ColorIndex, 0, Opciones.Length - 1);
        Aplicar();
    }

    public override void OnNetworkDespawn()
    {
        netColor.OnValueChanged -= AlCambiar;
    }

    private void AlCambiar(int antes, int ahora) => Aplicar();

    /// <summary>Lo llama la sala de espera cuando el jugador local elige otro color.</summary>
    public void Elegir(int indice)
    {
        if (!IsOwner) return;
        indice = Mathf.Clamp(indice, 0, Opciones.Length - 1);
        PlayerProfile.ColorIndex = indice;
        netColor.Value = indice;
    }

    private void Aplicar()
    {
        if (_bloque == null) _bloque = new MaterialPropertyBlock();
        Camera camara = GetComponentInChildren<Camera>(true);

        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            // Solo el cuerpo: lo que cuelga de la camara son las manos de primera persona
            if (camara != null && r.transform.IsChildOf(camara.transform)) continue;

            r.GetPropertyBlock(_bloque);
            _bloque.SetColor(BaseColor, ColorActual);
            r.SetPropertyBlock(_bloque);
        }
    }
}
