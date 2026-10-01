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

    private readonly List<SpawnPoint> _candidatos = new List<SpawnPoint>();

    public Transform GetSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
            return null;

        // Solo los puntos del tercio al que ha llegado el equipo. Si ese tercio
        // no tiene puntos, los del tercio anterior mas cercano; y si no hay
        // ninguno marcado, todos (escenas antiguas como TestMap).
        int tercio = ProgresoTercios.Instance != null ? ProgresoTercios.Instance.TercioAlcanzado : 0;
        _candidatos.Clear();
        for (int t = tercio; t >= 1 && _candidatos.Count == 0; t--)
            foreach (var sp in spawnPoints)
                if (sp != null && sp.tercio == t) _candidatos.Add(sp);
        if (_candidatos.Count == 0)
            foreach (var sp in spawnPoints)
                if (sp != null) _candidatos.Add(sp);
        if (_candidatos.Count == 0)
            return null;

        if (!chooseSafest)
            return _candidatos[Random.Range(0, _candidatos.Count)].transform;

        // Elegir el punto cuyo enemigo mas cercano este lo mas lejos posible
        var enemies = FindObjectsByType<EnemyBehaviour>(FindObjectsSortMode.None);
        Transform best = _candidatos[0].transform;
        float bestDistance = -1f;

        foreach (var sp in _candidatos)
        {
            if (sp == null) continue;

            float nearestEnemy = float.MaxValue;
            foreach (var e in enemies)
            {
                if (e == null || !e.gameObject.activeInHierarchy || e.IsDead) continue;
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
