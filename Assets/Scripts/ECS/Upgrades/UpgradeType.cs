// One entry per distinct upgrade definition — see UpgradeRegistry for the UpgradeType ->
// CardUpgrade lookup, mirroring CardType/CardRegistry.
public enum UpgradeType : byte
{
    // Example upgrade proving the pattern end-to-end (see DamageBoostUpgrade) — replace/add
    // to as real upgrades are designed.
    DamageBoost = 0,
}
