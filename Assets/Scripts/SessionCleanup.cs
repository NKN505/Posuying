using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using UnityEngine;

// Borra las partidas "fantasma" que quedan cuando el juego se cierra de golpe.
//
// EL PROBLEMA
// Si cierras el juego con Alt+F4, se cuelga o se va la luz, nunca se llega a
// avisar a Unity de que sales. En sus servidores TU SIGUES DENTRO de esa
// partida. Reiniciar el juego no arregla nada: eso vive en la nube, no aqui.
//
// Al intentar entrar en otra partida, el servidor responde "conflicto: este
// jugador ya esta dentro", y el SDK se rompe con un NullReferenceException
// (revienta dentro de LobbyConflictResolver). El sintoma es que no te puedes
// unir a NINGUNA partida, ni siquiera a las nuevas de otros.
//
// LE PASA SOBRE TODO AL QUE HACE DE ANFITRION: cuando se cae un cliente el
// lobby simplemente lo descarta, pero cuando se cae el anfitrion el lobby no
// muere, migra a otro y sigue vivo... contigo todavia apuntado dentro.
//
// LA SOLUCION
// Preguntar al servidor en que partidas figuramos y salir de todas antes de
// crear o entrar en una nueva.
public static class SessionCleanup
{
    // Se llama cuando NO deberiamos estar en ninguna partida. Todo lo que
    // aparezca aqui es basura de una ejecucion anterior.
    public static async Task PurgeGhostSessionsAsync()
    {
        string playerId = SafePlayerId();
        if (string.IsNullOrEmpty(playerId)) return;

        System.Collections.Generic.List<string> lobbies;

        try
        {
            lobbies = await LobbyService.Instance.GetJoinedLobbiesAsync();
        }
        catch (System.Exception e)
        {
            // Sin conexion o sin sesion iniciada: no es motivo para no seguir
            Debug.LogWarning("No se pudo consultar las partidas antiguas: " + e.Message);
            return;
        }

        if (lobbies == null || lobbies.Count == 0) return;

        Debug.Log("Limpiando " + lobbies.Count + " partida(s) fantasma de una sesion anterior.");

        foreach (string lobbyId in lobbies)
            await LeaveGhostAsync(lobbyId, playerId);
    }

    private static async Task LeaveGhostAsync(string lobbyId, string playerId)
    {
        // Si eramos el anfitrion, lo mejor es borrarla entera: asi desaparece
        // tambien de la lista de partidas que ven los demas.
        try
        {
            await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
            Debug.Log("Borrada la partida fantasma " + lobbyId);
            return;
        }
        catch (System.Exception)
        {
            // No eramos el anfitrion: solo podemos sacarnos a nosotros mismos
        }

        try
        {
            await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
            Debug.Log("Salida de la partida fantasma " + lobbyId);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("No se pudo salir de la partida " + lobbyId + ": " + e.Message);
        }
    }

    // Al cerrar el juego. Es "por si acaso": Unity no espera a que terminen las
    // tareas asincronas al salir, asi que puede quedarse a medias. La limpieza
    // de verdad es PurgeGhostSessionsAsync al volver a entrar.
    public static async Task LeaveEverythingAsync()
    {
        await PurgeGhostSessionsAsync();
    }

    private static string SafePlayerId()
    {
        try
        {
            if (!AuthenticationService.Instance.IsSignedIn) return "";
            return AuthenticationService.Instance.PlayerId;
        }
        catch (System.Exception)
        {
            // Servicios sin arrancar (partida local F1/F2): no hay nada que limpiar
            return "";
        }
    }
}
