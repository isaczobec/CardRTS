public struct StatsComponent : IComponent
{
    // Sentinel for "this stat doesn't apply to this card" (e.g. Speed on a building),
    // as opposed to a real value of 0. UI reads this to hide the stat's row entirely
    // rather than displaying "0".
    public const int STAT_NA = int.MinValue;

    public int MaxHealth;
    public int Speed;
    public int Range;
    public int Armor;
    public int Damage;
    public int AttackSpeed;

    // Mitigates DamageType.Spell damage the same way Armor mitigates DamageType.Normal —
    // see ArmorMitigationSystem.
    public int SpellResist;
}
