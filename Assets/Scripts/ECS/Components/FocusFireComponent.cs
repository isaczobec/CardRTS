// Granted by FocusFireUpgrade — attached to a permanent modifier entity (ModifierComponent.
// TargetEntityId = the troop it was equipped on, TicksRemaining = int.MaxValue, ModifierID =
// FocusFire) ALONGSIDE a StatModifierComponent on that SAME entity — that StatModifierComponent
// is what StatModifierSystem actually reads for the real attack-speed effect (it doesn't care
// about ModifierID), while this component just tracks the bookkeeping (current target/stack
// count) FocusFireSystem needs to keep that StatModifierComponent's AttackSpeedRatioBonus in
// sync. A dedicated component instead of reusing OnHitScheduleComponent, so this upgrade can
// be equipped on ANY card without colliding with that card's own, unrelated use of
// OnHitScheduleComponent (e.g. Healer Guardian, Stalker, Santa Claus all already carry one for
// their own on-hit effects). See FocusFireSystem.
public struct FocusFireComponent : IComponent
{
    // Entity this troop most recently dealt damage to — 0 (the struct default) means "no
    // target yet", which can never match a real entity id (see ECS.NextEntityId), so the
    // very first hit always takes the "new target" branch in FocusFireSystem.
    public ulong CurrentTargetId;

    public int Stacks;
    public int MaxStacks;

    // Per-stack attack-speed bonus, expressed as the DISPLAYED (positive-means-faster) ratio
    // — FocusFireSystem negates it before writing to StatModifierComponent.AttackSpeedRatioBonus,
    // same convention as AttackSpeedBoostUpgrade.
    public float AttackSpeedRatioBonusPerStack;
}
