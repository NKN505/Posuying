using System.Collections.Generic;
using UnityEngine;

// Gestiona los puntos de aparicion y elige donde reaparecer.
public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    [Header("Puntos de spawn")]
    public List<SpawnPoint> spawnPoints = new List<SpawnPoint>();

    [Tooltip("Si esta activo, elige el punto mas alejado del enemigo mas cercano. Si no, uno aleatorio.")]
    public bool chooseSafest = true;

    void Awake()
    {
        Instance = this;

        // Si no se asignaron a mano, se recogen automaticamente de la escena
        if (spawnPoints == null || spawnPoints.Count == 0)
            spawnPoints = new List<SpawnPoint>(FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None));
    }

    public Transform GetSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
            return null;

        if (!chooseSafest)
            return spawnPoints[Random.Range(0, spawnPoints.Count)].transform;

        // Elegir el punto cuyo enemigo mas cercano este lo mas lejos posible
        var enemies = FindObjectsByType<EnemyBehaviour>(FindObjectsSortMode.None);
        Transform best = spawnPoints[0].transform;
        float bestDistance = -1f;

        foreach (var sp in spawnPoints)
        {
            if (sp == null) continue;

            float nearestEnemy = float.MaxValue;
            foreach (var e in enemies)
            {
                if (e == null || !e.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(sp.transform.position, e.transform.position);
                if (d < nearestEnemy) nearestEnemy = d;
            }

            if (nearestEnemy > bestDistance)
            {
                bestDistance = nearestEnemy;
                best = sp.transform;
            }
        }

        return best;
    }
}
