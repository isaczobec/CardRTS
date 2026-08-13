// Present on a troop/building while it's channeling a manual recall back to its owner's
// deck (see RecallSystem) — the player-triggered counterpart to a card simply never having
// returned yet because its spawn is still alive (see SpawnedByCardComponent/
// CardReturnSystem). While this component is present the entity can't walk, attack, or cast
// abilities (RecallSystem vetoes CanMoveRequest/CanMoveOnOwnAccountRequest/CanPerformRequest
// for it), and taking any damage or receiving a fresh move/attack-target/ability order
// cancels it immediately (see RecallSystem.CancelRecall and its callers in
// PathfindingSystem/TargetingSystem/AbilitySystem).
//
// TicksRemaining counts down to 0, at which point RecallSystem refunds a ratio of this
// entity's own ResourceValueComponent to its owner and deletes it. InitialTicksRemaining is
// the duration it started at (never decremented) — kept alongside it purely so a progress
// ratio (TicksRemaining / InitialTicksRemaining) can be read for UI, the same role
// ActivatableComponent.InitialTicksUntilActive plays for deploy-delay progress.
public struct RecallingComponent : IComponent
{
    public int TicksRemaining;
    public int InitialTicksRemaining;
}
