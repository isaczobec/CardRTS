using System.Collections.Generic;

// One CardUpgrade instance per UpgradeType, shared across the whole game — mirrors
// CardRegistry exactly.
public static class UpgradeRegistry
{
    private static readonly Dictionary<UpgradeType, CardUpgrade> _upgrades = new Dictionary<UpgradeType, CardUpgrade>
    {
        { UpgradeType.DamageBoost, new DamageBoostUpgrade() },
    };

    public static bool TryGet(UpgradeType type, out CardUpgrade upgrade) => _upgrades.TryGetValue(type, out upgrade);
}
