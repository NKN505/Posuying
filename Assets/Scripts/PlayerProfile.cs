using UnityEngine;

// Nombre que el jugador elige para si mismo, guardado en el equipo
// para no tener que escribirlo en cada partida.
public static class PlayerProfile
{
    private const string Key = "player_name";
    public const int MaxLength = 16;

    public static string Name
    {
        get
        {
            string saved = PlayerPrefs.GetString(Key, "");
            return string.IsNullOrWhiteSpace(saved) ? DefaultName() : saved;
        }
        set
        {
            PlayerPrefs.SetString(Key, Sanitize(value));
            PlayerPrefs.Save();
        }
    }

    // Si nunca ha puesto nombre, proponemos el del equipo
    private static string DefaultName()
    {
        string device = SystemInfo.deviceName;
        return string.IsNullOrWhiteSpace(device) ? "Jugador" : Sanitize(device);
    }

    public static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Jugador";

        raw = raw.Trim();
        if (raw.Length > MaxLength) raw = raw.Substring(0, MaxLength);
        return raw;
    }
}
