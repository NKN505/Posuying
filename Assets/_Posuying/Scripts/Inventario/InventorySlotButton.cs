using UnityEngine;
using UnityEngine.EventSystems;

// Se anade a cada hueco del inventario para saber en cual se ha hecho clic.
// Lo crea InventoryPanel por codigo; no hay que ponerlo a mano.
public class InventorySlotButton : MonoBehaviour, IPointerClickHandler
{
    public int index;
    public System.Action<int> onClick;
    public System.Action<int> onRightClick;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right) onRightClick?.Invoke(index);
        else onClick?.Invoke(index);
    }
}
