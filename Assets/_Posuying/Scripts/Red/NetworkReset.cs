using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Reinicio total de la capa de red.
//
// Por que hace falta: Netcode arrastra estado interno cuando un mismo proceso
// ha sido HOST y luego intenta arrancar como CLIENTE. Apagarlo y volver a
// encenderlo no siempre basta, y el sintoma es un "Failed to start the network
// manager" que ya no se arregla por muchos reintentos.
//
// En vez de pelear con ese estado, se destruye el NetworkManager y se recarga
// la escena: vuelve a nacer limpio, como si acabaras de abrir el juego.
public static class NetworkReset
{
    public static bool InProgress { get; private set; }

    public static void HardReset(string reason)
    {
        if (InProgress) return;
        InProgress = true;

        Debug.LogWarning("Reinicio completo de la red. Motivo: " + reason);

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            try
            {
                if (nm.IsListening || nm.IsClient || nm.IsServer)
                    nm.Shutdown();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error al apagar Netcode: " + e.Message);
            }

            // El NetworkManager sobrevive a los cambios de escena, asi que hay
            // que quitarlo a mano para que la escena cree uno nuevo.
            Object.Destroy(nm.gameObject);
        }

        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        InProgress = false;
    }

    // Mensaje tipico de Netcode cuando se queda en mal estado
    public static bool LooksLikeBrokenNetwork(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;

        return message.Contains("Failed to start the network manager")
            || message.Contains("already listening");
    }
}
