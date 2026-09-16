using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Fotografia completa de la partida, convertida a bytes para poder mandarsela
// al nuevo host cuando el anterior se cae.
//
// Formato (version 1):
//   version
//   jugadores:  id, vida, huecos de inventario (idItem, cantidad)
//   enemigos:   prefab, posicion, giro, vida, vida maxima
//   recogibles: identificador del objeto de escena, recogido si/no
public class WorldState
{
    public const int Version = 1;

    public class PlayerState
    {
        public string persistentId;
        public float health;
        public List<Vector2Int> slots = new List<Vector2Int>();   // x = idItem, y = cantidad
    }

    public class EnemyState
    {
        public int prefabIndex;
        public Vector3 position;
        public float yaw;
        public float health;
        public float maxHealth;
    }

    public class PickupState
    {
        public uint sceneObjectId;
        public bool taken;
    }

    public List<PlayerState> players = new List<PlayerState>();
    public List<EnemyState> enemies = new List<EnemyState>();
    public List<PickupState> pickups = new List<PickupState>();

    public byte[] ToBytes()
    {
        using (var stream = new MemoryStream())
        using (var w = new BinaryWriter(stream))
        {
            w.Write(Version);

            w.Write(players.Count);
            foreach (var p in players)
            {
                w.Write(p.persistentId ?? "");
                w.Write(p.health);
                w.Write(p.slots.Count);
                foreach (var s in p.slots)
                {
                    w.Write(s.x);
                    w.Write(s.y);
                }
            }

            w.Write(enemies.Count);
            foreach (var e in enemies)
            {
                w.Write(e.prefabIndex);
                w.Write(e.position.x); w.Write(e.position.y); w.Write(e.position.z);
                w.Write(e.yaw);
                w.Write(e.health);
                w.Write(e.maxHealth);
            }

            w.Write(pickups.Count);
            foreach (var k in pickups)
            {
                w.Write(k.sceneObjectId);
                w.Write(k.taken);
            }

            w.Flush();
            return stream.ToArray();
        }
    }

    public static WorldState FromBytes(byte[] data)
    {
        var state = new WorldState();
        if (data == null || data.Length == 0) return state;

        try
        {
            using (var stream = new MemoryStream(data))
            using (var r = new BinaryReader(stream))
            {
                int version = r.ReadInt32();
                if (version != Version)
                {
                    Debug.LogWarning("Estado de partida de otra version (" + version +
                                     "): se ignora para no corromper nada.");
                    return state;
                }

                int playerCount = r.ReadInt32();
                for (int i = 0; i < playerCount; i++)
                {
                    var p = new PlayerState
                    {
                        persistentId = r.ReadString(),
                        health = r.ReadSingle()
                    };

                    int slotCount = r.ReadInt32();
                    for (int s = 0; s < slotCount; s++)
                        p.slots.Add(new Vector2Int(r.ReadInt32(), r.ReadInt32()));

                    state.players.Add(p);
                }

                int enemyCount = r.ReadInt32();
                for (int i = 0; i < enemyCount; i++)
                {
                    state.enemies.Add(new EnemyState
                    {
                        prefabIndex = r.ReadInt32(),
                        position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                        yaw = r.ReadSingle(),
                        health = r.ReadSingle(),
                        maxHealth = r.ReadSingle()
                    });
                }

                int pickupCount = r.ReadInt32();
                for (int i = 0; i < pickupCount; i++)
                {
                    state.pickups.Add(new PickupState
                    {
                        sceneObjectId = r.ReadUInt32(),
                        taken = r.ReadBoolean()
                    });
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("No se pudo leer el estado de la partida: " + e.Message);
        }

        return state;
    }
}
