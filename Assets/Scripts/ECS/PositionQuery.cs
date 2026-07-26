// Raw (non-interpolated) PositionComponent lookup for simulation-side code that needs an
// entity's current world position but isn't rendering anything — e.g. a FlagEvent capturing
// where it happened at the moment it's raised (see FlagEvent.Position's own doc comment on
// why that capture must happen then, not lazily later). Renderers wanting a smooth, tick-
// interpolated position instead should use EntityPositionQuery.
public static class PositionQuery
{
    // False (leaving x/y at 0) if entityId has no PositionComponent at all.
    public static bool TryGet(ECS ecs, ulong entityId, out float x, out float y)
    {
        x = 0f;
        y = 0f;

        ComponentStore<PositionComponent> posStore = ecs?.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(entityId)) return false;

        PositionComponent pos = posStore.GetComponent(entityId);
        x = pos.X;
        y = pos.Y;
        return true;
    }
}
