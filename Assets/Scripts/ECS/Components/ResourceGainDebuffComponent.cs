// Granted by DiscardCardSystem whenever a player discards a card (see CardHandRenderer's
// double-right-click gesture) — attached to a modifier entity (ModifierComponent.
// TargetEntityId = the player's OWN PlayerResourcesComponent entity, not a troop; no icon —
// see DiscardCardSystem.ApplyResourceGainDebuff's own doc comment) targeting the discarding
// player themselves. See ResourceGainDebuffSystem, which subscribes to ResourcesAdded and
// scales Multiplier down by MultiplierRatio for Wood/Stone/Metal/Gold gains while an active
// modifier of this kind targets the gaining player.
public struct ResourceGainDebuffComponent : IComponent
{
    // Fraction of a normal Wood/Stone/Metal/Gold gain still received while this is active
    // (e.g. 0.3 = -70%). Gems/Soulstones gains are untouched — see ResourceGainDebuffSystem.
    public float MultiplierRatio;
}
