// Estado global de las interfaces que "roban" el raton al juego.
// Sirve para que el menu de red y el inventario no se peleen por el cursor
// y para que el jugador no se mueva mientras hay una ventana abierta.
public static class UIState
{
    public static bool NetMenuOpen;
    public static bool InventoryOpen;
    // Escribiendo en el chat: las teclas son letras, no ordenes del juego
    public static bool ChatOpen;
    // En la sala de espera (o esperando a que el anfitrion te deje entrar): el
    // personaje esta quieto y el raton libre para los botones
    public static bool SalaAbierta;

    // True si alguna ventana esta abierta: el jugador no debe moverse
    // y el raton debe estar libre.
    public static bool BlocksGameplay => NetMenuOpen || InventoryOpen || ChatOpen || SalaAbierta;
}
