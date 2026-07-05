public interface IPassiveRegenerator
{
    float RegenDelay { get; }
    float RegenAmountPerSecond { get; }

    // Condicion especifica de cada personaje (ej: jugador agachado y quieto)
    bool CanRegenerate();
}
