using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// Mantiene un grupo de supervivientes NPC por el mapa.
//
// EN RED: solo se ejecuta en el SERVIDOR, igual que el director de hordas. Los
// NPCs se crean como objetos de red para que todos los vean.
//
// Se puede encender y apagar en cualquier momento desde las opciones: al
// apagarlo desaparecen todos, al encenderlo vuelven a salir.
public class NpcDirector : MonoBehaviour
{
    public static NpcDirector Instance { get; private set; }

    [Header("Prefab")]
    [Tooltip("Prefab del NPC (con NetworkObject, NavMeshAgent y NpcSurvivor)")]
    public NpcSurvivor npcPrefab;

    [Header("Poblacion")]
    [Tooltip("Cada cuanto comprueba si falta alguno (segundos)")]
    public float checkInterval = 3f;

    [Header("Aparicion")]
    [Tooltip("Cuanto se separan del punto de aparicion elegido")]
    public float spawnSpread = 6f;
    public float navSampleRadius = 8f;
    public int placementTries = 10;
    [Tooltip("Compensa el pivote del modelo. El del jugador tiene el pivote en los " +
             "pies, asi que 0 es lo correcto. Subelo solo si ves al NPC enterrado.")]
    public float agentBaseOffset = 0f;

    private float _timer;

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        // Sin jugadores no hay partida todavia
        if (NetworkPlayer.AllPlayers.Count == 0) return;

        if (!GameSettings.NpcsEnabled)
        {
            if (NpcSurvivor.All.Count > 0) RemoveAll();
            return;
        }

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = checkInterval;

        // El numero sale de las opciones, no del Inspector: asi se puede subir
        // y bajar en mitad de la partida.
        int target = Mathf.Max(0, GameSettings.NpcCount);

        while (NpcSurvivor.All.Count > target)
            RemoveOne();

        while (NpcSurvivor.All.Count < target)
        {
            if (!SpawnOne()) break;   // sin sitio valido: se reintenta luego
        }
    }

    // ---------- Crear y quitar ----------

    public bool SpawnOne()
    {
        if (npcPrefab == null)
        {
            Debug.LogWarning("NpcDirector: falta asignar el prefab del NPC.");
            return false;
        }

        Vector3 position;
        if (!FindSpawnPosition(out position)) return false;

        NpcSurvivor npc = Instantiate(npcPrefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

        // El modelo se hunde en el suelo si el agente no compensa el pivote
        var agent = npc.GetComponent<NavMeshAgent>();
        if (agent != null) agent.baseOffset = agentBaseOffset;

        var netObject = npc.GetComponent<NetworkObject>();
        if (netObject == null)
        {
            Debug.LogError("NpcDirector: el prefab del NPC no tiene NetworkObject.");
            Destroy(npc.gameObject);
            return false;
        }

        netObject.Spawn();
        npc.SetMaxHealth(PlayerMaxHealth());   // despues de Spawn: antes no existe en la red
        return true;
    }

    // Los NPC aguantan lo mismo que un jugador. Se copia en vez de fijarlo en el
    // Inspector para que no se quede desfasado si algun dia cambia la del jugador.
    private float PlayerMaxHealth()
    {
        var players = NetworkPlayer.AllPlayers;

        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null) continue;

            float max = player.GetMaxHealth();
            if (max > 0f) return max;
        }

        return 1000f;   // sin jugadores todavia: el valor de siempre
    }

    private void RemoveOne()
    {
        for (int i = NpcSurvivor.All.Count - 1; i >= 0; i--)
        {
            var npc = NpcSurvivor.All[i];
            if (npc == null) continue;

            Destroy(npc.gameObject);
            return;
        }
    }

    public void RemoveAll()
    {
        for (int i = NpcSurvivor.All.Count - 1; i >= 0; i--)
        {
            var npc = NpcSurvivor.All[i];
            if (npc != null) Destroy(npc.gameObject);
        }
    }

    // Un punto valido del NavMesh cerca de un punto de aparicion del mapa
    private bool FindSpawnPosition(out Vector3 position)
    {
        position = Vector3.zero;

        for (int i = 0; i < placementTries; i++)
        {
            Vector3 origin;

            if (SpawnManager.Instance != null)
            {
                Transform point = SpawnManager.Instance.GetSpawnPoint();
                if (point == null) return false;
                origin = point.position;
            }
            else
            {
                // Sin puntos de aparicion, alrededor de un jugador
                var players = NetworkPlayer.AllPlayers;
                var reference = players[Random.Range(0, players.Count)];
                if (reference == null) continue;
                origin = reference.transform.position;
            }

            Vector2 offset = Random.insideUnitCircle * spawnSpread;
            Vector3 candidate = origin + new Vector3(offset.x, 0f, offset.y);

            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, navSampleRadius, NavMesh.AllAreas))
            {
                position = hit.position;
                return true;
            }
        }

        return false;
    }
}
