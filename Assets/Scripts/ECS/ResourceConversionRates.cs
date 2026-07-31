// Multiplier applied to each raw resource unit when ResourceValueTotalSystem converts a
// player's ResourceValueComponent holdings into one shared "unified value" number. Gold —
// already the game's premium/persistent currency — anchors the scale at 1:1; every other
// resource is worth less per unit since they're earned/spent far more heavily over a match.
// Purely a reporting weight — nothing in the simulation depends on these ratios except that
// one system's output, so they're safe to retune freely.
public static class ResourceConversionRates
{
    public const float Wood       = 0.2f;
    public const float Stone      = 0.25f;
    public const float Metal      = 0.5f;
    public const float Gems       = 2f;
    public const float Soulstones = 3f;
    public const float Gold       = 1f;
}
