using UnityEngine;

/// <summary>
/// Ponlo en la camara de espectador y en la de killcam.
/// Ajusta su culling mask para que vea el cuerpo de tercera persona de TODOS los
/// jugadores (incluido el local) y nunca las manos de primera persona.
/// </summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public class SpectatorCameraVisual : MonoBehaviour
{
    private Camera _camera;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    // En OnEnable y no en Awake: si la camara se activa mas tarde (al morir),
    // las capas ya estan resueltas y el mask se aplica en ese momento.
    private void OnEnable()
    {
        PlayerVisual.ConfigureSpectatorCamera(_camera);
    }
}
