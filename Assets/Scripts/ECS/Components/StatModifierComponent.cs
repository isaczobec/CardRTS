// Modifier payload for a StatModifierComponent-carrying modifier entity (paired with a
// ModifierComponent on the same entity — see ModifierSystem/ModifierQuery) — contributes a
// ratio bonus and/or additive bonus to one or more of ModifierComponent.TargetEntityId's
// stats. A ratio bonus of e.g. 0.1 means "+10%"; two separate +10%/+20% modifiers on the
// same entity combine into +30% (both summed into the matching StatRequest's
// RatioMultiplier, which starts at 1 meaning no change), not (1.1 * 1.2 =) +32%. See
// StatModifierSystem, which subscribes to each GetXRequest and applies these.
public struct StatModifierComponent : IComponent
{
    public float MaxHealthRatioBonus;
    public float MaxHealthAdditiveBonus;

    public float SpeedRatioBonus;
    public float SpeedAdditiveBonus;

    public float RangeRatioBonus;
    public float RangeAdditiveBonus;

    public float ArmorRatioBonus;
    public float ArmorAdditiveBonus;

    public float DamageRatioBonus;
    public float DamageAdditiveBonus;

    public float AttackSpeedRatioBonus;
    public float AttackSpeedAdditiveBonus;
}
