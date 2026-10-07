using UnityEngine;

// Nombre que el jugador elige para si mismo, guardado en el equipo
// para no tener que escribirlo en cada partida.
public static class PlayerProfile
{
    private const string Key = "player_name";
    public const int MaxLength = 16;

    // Color del uniforme elegido en la sala de espera (indice de AspectoJugador.Opciones)
    public static int ColorIndex
    {
        get { return PlayerPrefs.GetInt("player_color", 0); }
        set { PlayerPrefs.SetInt("player_color", value); PlayerPrefs.Save(); }
    }

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
