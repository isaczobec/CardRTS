// Attached to its own dedicated entity (created via ScheduledCallSystem.Schedule) — once
// ecs.CurrentSimulationTick reaches Tick, ScheduledCallSystem invokes whichever function is
// registered under Type and deletes the entity. Runs identically on the server and every
// predicting client, same as any other component-driven effect in this codebase (e.g.
// FireProjectileOnExpireComponent), so this needs no networking of its own beyond the
// ordinary component-create/delta/reconciliation pipeline already in place — the SAME
// deterministic cast-time code creates the SAME component on both sides, and each side
// resolves Type to its own locally-registered function.
//
// Param0-Param2 are generic argument slots for whatever the registered function needs to
// read back (e.g. the caster's entity id) — plain scalars rather than a more elaborate
// payload scheme, since that's enough for every scheduled call so far.
public struct ScheduledCallComponent : IComponent
{
    public ScheduledCallType Type;
    public ulong Tick;
    public ulong Param0;
    public float Param1;
    public float Param2;

    // Set once ScheduledCallSystem has invoked the registered function — mirrors
    // TeleportingModifierComponent.HasTeleported: entity deletion is server-only (see
    // ScheduledCallSystem), so a predicting client needs this to avoid re-invoking the
    // function every tick while it waits for the server's deletion delta to land.
    public bool Invoked;
}
