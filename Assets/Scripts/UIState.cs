// Estado global de las interfaces que "roban" el raton al juego.
// Sirve para que el menu de red y el inventario no se peleen por el cursor
// y para que el jugador no se mueva mientras hay una ventana abierta.
public static class UIState
{
    public static bool NetMenuOpen;
    public static bool InventoryOpen;

    // True si alguna ventana esta abierta: el jugador no debe moverse
    // y el raton debe estar libre.
    public static bool BlocksGameplay => NetMenuOpen || InventoryOpen;
}
