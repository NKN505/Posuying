using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Final del juego: cuando se completa la linea de misiones, todos los jugadores
// salen de la partida y pasan a la escena del final, que reproduce el video.
//
// COMO LLAMARLO desde el sistema de misiones (en el servidor):
//     FinDelJuego.Instance.CompletarJuego();
//
// Mientras no exista ese sistema, en el editor y en builds de desarrollo el
// anfitrion puede forzarlo con F10 para probar.
//
// Va en el mismo objeto que MatchManager (tiene NetworkObject).
public class FinDelJuego : NetworkBehaviour
{
    public static FinDelJuego Instance { get; private set; }

    [Tooltip("Nombre de la escena del final (tiene que estar en Build Settings)")]
    public string escenaFinal = "Final";

    [Tooltip("Segundos que espera el anfitrion antes de cerrar la partida, para que el aviso llegue a todos")]
    public float esperaAnfitrion = 1f;

    [Tooltip("Tecla para forzar el final al probar (solo editor y builds de desarrollo)")]
    public KeyCode teclaPrueba = KeyCode.F10;

    private bool _terminado;

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (IsServer && (Application.isEditor || Debug.isDebugBuild) && Input.GetKeyDown(teclaPrueba))
            CompletarJuego();
    }

    // Solo servidor. Llamarlo mas de una vez no hace nada.
    public void CompletarJuego()
    {
        if (!IsServer || _terminado) return;
        _terminado = true;
        IrAlFinalClientRpc();
    }

    [ClientRpc]
    private void IrAlFinalClientRpc()
    {
        // El anfitrion espera un poco: si cerrase la partida al instante, el aviso
        // podria no salir hacia los demas y se quedarian dentro.
        if (IsServer) StartCoroutine(SalirTrasEspera(esperaAnfitrion));
        else Salir();
    }

    private IEnumerator SalirTrasEspera(float segundos)
    {
        yield return new WaitForSecondsRealtime(segundos);
        Salir();
    }

    private void Salir()
    {
        // Salir de la sesion online si la hay (libera el lobby) o apagar la red.
        // El NetworkManager sobrevive al cambio de escena; lo retira EscenaFinal
        // al volver al menu, cuando la salida ya ha terminado.
        var sesion = FindFirstObjectByType<OnlineSession>();
        if (sesion != null && sesion.HasSession) sesion.LeaveOnlineGame();
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        SceneManager.LoadScene(escenaFinal);
    }
}
