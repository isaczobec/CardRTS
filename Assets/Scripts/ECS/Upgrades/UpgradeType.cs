// One entry per distinct upgrade definition — see UpgradeRegistry for the UpgradeType ->
// CardUpgrade lookup, mirroring CardType/CardRegistry.
public enum UpgradeType : byte
{
    // Example upgrade proving the pattern end-to-end (see DamageBoostUpgrade) — replace/add
    // to as real upgrades are designed.
    DamageBoost = 0,
    HealthBonus = 1,
    SpeedBoost = 2,
    RangeBoost = 3,
    ImprovedArmor = 4,
    SpellShield = 5,
    AttackSpeedBoost = 6,
    Giantsbane = 7,
    FocusFire = 8,
    Lifesteal = 9,
    Deflection = 10,
    Bruiser = 11,
}
