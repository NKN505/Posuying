using Unity.Netcode;
using UnityEngine;

// Va en el prefab del jugador, junto a NetworkObject.
// Cada maquina recibe una copia de CADA jugador conectado; este script decide
// cual de esas copias controlas tu (la "owner") y apaga el control en las demas.
[RequireComponent(typeof(PlayerController))]
public class NetworkPlayer : NetworkBehaviour
{
    // Referencias al jugador local, para que el HUD las encuentre sin buscarlas en la escena
    public static PlayerController LocalPlayer { get; private set; }
    public static Inventory LocalInventory { get; private set; }

    public override void OnNetworkSpawn()
    {
        var controller = GetComponent<PlayerController>();
        var combat = GetComponent<PlayerCombat>();
        var inventory = GetComponent<Inventory>();
        var flashlight = GetComponentInChildren<Flashlight>(true);
        var cam = GetComponentInChildren<Camera>(true);

        if (IsOwner)
        {
            // Este es MI personaje
            LocalPlayer = controller;
            LocalInventory = inventory;

            gameObject.name = "Player (LOCAL)";

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            MoveToSpawnPoint();
        }
        else
        {
            // Personaje de otro jugador: lo vemos moverse (via NetworkTransform)
            // pero no lo controlamos ni miramos por su camara.
            gameObject.name = "Player (remoto " + OwnerClientId + ")";

            if (controller != null) controller.enabled = false;
            if (combat != null) combat.enabled = false;
            if (inventory != null) inventory.enabled = false;
            if (flashlight != null) flashlight.enabled = false;

            if (cam != null)
            {
                cam.enabled = false;
                var listener = cam.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }
    }

    // NGO crea al jugador en el origen: lo llevamos a un punto de spawn del mapa.
    // Con NetworkTransform en modo Owner, este movimiento se replica al resto.
    private void MoveToSpawnPoint()
    {
        if (SpawnManager.Instance == null) return;

        Transform sp = SpawnManager.Instance.GetSpawnPoint();
        if (sp == null) return;

        // Pequeno desvio para que dos jugadores no caigan exactamente encima
        Vector2 offset = Random.insideUnitCircle * 1.5f;
        Vector3 pos = sp.position + new Vector3(offset.x, 0f, offset.y);

        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, sp.eulerAngles.y, 0f);
        if (cc != null) cc.enabled = true;
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            LocalPlayer = null;
            LocalInventory = null;
        }
    }
}
