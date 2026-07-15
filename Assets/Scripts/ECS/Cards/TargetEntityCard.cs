// Card kind for "click an existing selectable entity" cards — the entity-targeted
// counterpart to SpawnAtPointCard (see Card.cs for how a new kind sits alongside existing
// ones). Range-from-building/troop (Card.RequiresFriendlyBuildingRange/
// AllowsFriendlyTroopRange), when enabled, is checked against the TARGET entity's own
// position — the same way SpawnAtPointCard checks it against the played point.
public abstract class TargetEntityCard : Card
{
    // Whether this card may be played on an entity owned by the playing player.
    public abstract bool CanTargetFriendly { get; }

    // Whether this card may be played on an entity NOT owned by the playing player (covers
    // both enemy and neutral).
    public abstract bool CanTargetEnemyOrNeutral { get; }

    // How close to the cursor (world units) a candidate entity must be to be considered
    // "the one the cursor is pointing at" — see EntityTargeting.FindClosestSelectable and
    // CardTargetIndicatorManager. The same role Ability.TargetSelectionRadius plays for
    // ability targeting; independent of any building/troop range requirement above.
    public virtual float TargetSelectionRadius => 5.5f;

    // Called by TargetEntityCardPlaySystem when a player plays this card on targetEntityId.
    // cardEntityId is the card entity that was played (recycled back into the deck
    // afterwards, not deleted), in case an implementation ever needs to read more off it
    // than TargetEntityCardPlaySystem already validated.
    public abstract void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, ulong targetEntityId);
}
