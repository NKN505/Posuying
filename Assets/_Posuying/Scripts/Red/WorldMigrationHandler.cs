using Unity.Services.Multiplayer;
using UnityEngine;

// Puente entre nuestro guardado del mundo y la migracion de host de Unity.
//
// El SDK llama a Generate() cada pocos segundos en el host actual y guarda esos
// bytes; si el host se cae, llama a Apply() en el jugador elegido como host nuevo.
//
// Netcode for GameObjects no trae migracion hecha (solo la version para Entities),
// asi que el contenido de esos bytes lo generamos y lo aplicamos nosotros.
public class WorldMigrationHandler : IMigrationDataHandler
{
    public byte[] Generate()
    {
        if (WorldRestorer.Instance == null)
            return new byte[0];

        try
        {
            return WorldRestorer.Instance.Capture().ToBytes();
        }
        catch (System.Exception e)
        {
            Debug.LogError("Fallo al guardar el estado de la partida: " + e.Message);
            return new byte[0];
        }
    }

    public void Apply(byte[] migrationData)
    {
        Debug.Log("Migracion de host: recibido el estado de la partida (" +
                  (migrationData != null ? migrationData.Length : 0) + " bytes)");

        if (WorldRestorer.Instance == null)
        {
            Debug.LogError("Falta el componente WorldRestorer: no se puede reconstruir la partida.");
            return;
        }

        try
        {
            WorldRestorer.Instance.ApplySnapshot(WorldState.FromBytes(migrationData));
        }
        catch (System.Exception e)
        {
            Debug.LogError("Fallo al reconstruir la partida: " + e.Message);
        }
    }
}
