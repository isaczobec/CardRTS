// One instance of an active buff/debuff (slow, stun, ...) targeting TargetEntityId — a
// separate entity of its own, not a component slot on the target, so multiple modifiers
// (even of the same "kind", from different sources) can affect the same entity
// simultaneously without needing to be merged/stacked into a single component. A modifier
// entity is allowed to be spawned client-side (e.g. predicted instantly on a projectile
// hit) even though the server will later create its own copy under a different entity ID —
// that's fine, since a modifier's actual effect is expected to act through systems that
// subscribe to requests (the same veto pattern as IsActiveRequest/CanTakeActionsRequest),
// not through anything that depends on the modifier entity's own identity matching across
// client and server. See ModifierQuery.IsActive and ModifierSystem.
public struct ModifierComponent : IComponent
{
    public ulong TargetEntityId;

    // Ticks remaining before this modifier expires — counts down each tick (see
    // ModifierSystem), which deletes the entity on the server once it reaches 0.
    // int.MaxValue means the modifier never expires on its own (removed some other way).
    public int TicksRemaining;
}
