using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Fotografia el mundo mientras somos host, y lo reconstruye si nos toca serlo
// tras una migracion.
//
// Se pone en el objeto NetworkManager de la escena.
public class WorldRestorer : MonoBehaviour
{
    public static WorldRestorer Instance { get; private set; }

    [Tooltip("Segundos de espera antes de reconstruir, para que la red se asiente")]
    public float restoreDelay = 1f;

    private WorldState _pending;                       // estado recibido, aun por aplicar
    private readonly Dictionary<string, WorldState.PlayerState> _pendingPlayers =
        new Dictionary<string, WorldState.PlayerState>();

    void Awake()
    {
        Instance = this;
    }

    // ---------- Fotografiar (se ejecuta en el host actual) ----------

    public WorldState Capture()
    {
        var state = new WorldState();

        // Jugadores: vida e inventario, identificados por su id estable
        foreach (var player in NetworkPlayer.AllPlayers)
        {
            if (player == null) continue;

            var idComponent = player.GetComponent<PersistentPlayerId>();
            var inventory = player.GetComponent<Inventory>();
            if (idComponent == null || string.IsNullOrEmpty(idComponent.Id)) continue;

            state.players.Add(new WorldState.PlayerState
            {
                persistentId = idComponent.Id,
                health = player.GetHealth(),
                slots = inventory != null ? inventory.ExportSlots() : new List<Vector2Int>()
            });
        }

        // Enemigos vivos
        var director = FindFirstObjectByType<HordeDirector>();
        if (director != null)
        {
            foreach (var enemy in director.AliveEnemies)
            {
                if (enemy == null || enemy.prefabIndex < 0) continue;

                state.enemies.Add(new WorldState.EnemyState
                {
                    prefabIndex = enemy.prefabIndex,
                    position = enemy.transform.position,
                    yaw = enemy.transform.eulerAngles.y,
                    health = enemy.GetHealth(),
                    maxHealth = enemy.GetMaxHealth()
                });
            }
        }

        // Recogibles del escenario: solo si estan cogidos o no
        foreach (var heal in FindObjectsByType<HealPickup>(FindObjectsSortMode.None))
            state.pickups.Add(new WorldState.PickupState
            {
                sceneObjectId = SceneObjectKey(heal.gameObject),
                taken = heal.IsTaken
            });

        foreach (var item in FindObjectsByType<ItemPickup>(FindObjectsSortMode.None))
            state.pickups.Add(new WorldState.PickupState
            {
                sceneObjectId = SceneObjectKey(item.gameObject),
                taken = item.IsTaken
            });

        return state;
    }

    // ---------- Reconstruir (se ejecuta en el host nuevo) ----------

    public void ApplySnapshot(WorldState state)
    {
        _pending = state;

        _pendingPlayers.Clear();
        foreach (var p in state.players)
            if (!string.IsNullOrEmpty(p.persistentId))
                _pendingPlayers[p.persistentId] = p;

        StartCoroutine(RestoreWorldWhenReady());
    }

    private IEnumerator RestoreWorldWhenReady()
    {
        // Esperar a ser servidor de verdad y a que la red respire
        float timeout = 20f;
        while (timeout > 0f &&
               (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer))
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("No somos el nuevo host: no hay nada que reconstruir.");
            yield break;
        }

        yield return new WaitForSeconds(restoreDelay);

        RestoreEnemies();
        RestorePickups();

        Debug.Log("Mundo reconstruido tras la migracion de host.");
        _pending = null;
    }

    private void RestoreEnemies()
    {
        if (_pending == null) return;

        var director = FindFirstObjectByType<HordeDirector>();
        if (director == null) return;

        foreach (var e in _pending.enemies)
            director.RestoreEnemy(e.prefabIndex, e.position, e.yaw, e.health, e.maxHealth);
    }

    private void RestorePickups()
    {
        if (_pending == null) return;

        var taken = new Dictionary<uint, bool>();
        foreach (var p in _pending.pickups)
            taken[p.sceneObjectId] = p.taken;

        foreach (var heal in FindObjectsByType<HealPickup>(FindObjectsSortMode.None))
            if (taken.TryGetValue(SceneObjectKey(heal.gameObject), out bool wasTaken))
                heal.SetTaken(wasTaken);

        foreach (var item in FindObjectsByType<ItemPickup>(FindObjectsSortMode.None))
            if (taken.TryGetValue(SceneObjectKey(item.gameObject), out bool wasTaken))
                item.SetTaken(wasTaken);
    }

    // Lo llama PersistentPlayerId cuando el servidor descubre quien es cada jugador
    public void RestorePlayer(string persistentId, Character character, Inventory inventory)
    {
        if (!_pendingPlayers.TryGetValue(persistentId, out var saved)) return;

        if (character != null)
            character.SetHealth(saved.health);

        if (inventory != null)
            inventory.ImportSlots(saved.slots);

        _pendingPlayers.Remove(persistentId);   // solo se restaura una vez
        Debug.Log("Restaurado el estado del jugador " + persistentId);
    }

    // Identificador estable de un objeto colocado en la escena.
    // Usamos nombre + posicion redondeada: igual en todas las maquinas y entre
    // ejecuciones, mientras no se muevan los objetos en el editor.
    private static uint SceneObjectKey(GameObject go)
    {
        Vector3 p = go.transform.position;
        string key = go.name + "|" +
                     Mathf.RoundToInt(p.x * 100f) + "|" +
                     Mathf.RoundToInt(p.y * 100f) + "|" +
                     Mathf.RoundToInt(p.z * 100f);

        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in key)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }
}
