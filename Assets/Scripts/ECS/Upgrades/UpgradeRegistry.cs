using System.Collections.Generic;

// One CardUpgrade instance per UpgradeType, shared across the whole game — mirrors
// CardRegistry exactly.
public static class UpgradeRegistry
{
    private static readonly Dictionary<UpgradeType, CardUpgrade> _upgrades = new Dictionary<UpgradeType, CardUpgrade>
    {
        { UpgradeType.DamageBoost, new DamageBoostUpgrade() },
        { UpgradeType.HealthBonus, new HealthBonusUpgrade() },
        { UpgradeType.SpeedBoost, new SpeedBoostUpgrade() },
        { UpgradeType.RangeBoost, new RangeBoostUpgrade() },
        { UpgradeType.ImprovedArmor, new ImprovedArmorUpgrade() },
        { UpgradeType.SpellShield, new SpellShieldUpgrade() },
        { UpgradeType.AttackSpeedBoost, new AttackSpeedBoostUpgrade() },
        { UpgradeType.Giantsbane, new GiantsbaneUpgrade() },
        { UpgradeType.FocusFire, new FocusFireUpgrade() },
        { UpgradeType.Lifesteal, new LifestealUpgrade() },
        { UpgradeType.Deflection, new DeflectionUpgrade() },
        { UpgradeType.Bruiser, new BruiserUpgrade() },
    };

    public static bool TryGet(UpgradeType type, out CardUpgrade upgrade) => _upgrades.TryGetValue(type, out upgrade);
}
