// Shared "find a player's own base entity position" lookup (RenderableType.PlayerBaseCore —
// see SpawnPlayerBasesFeature) — used by anything that needs to resolve where a player's
// base currently stands (e.g. ResourceGeneratorCardHelper's distance-based generation rate,
// BallisticMissileCard's own launch point).
public static class PlayerBaseQuery
{
    public static bool TryFindPosition(ECS ecs, ushort ownerPlayerId, out float x, out float y)
    {
        x = 0f;
        y = 0f;

        ComponentStore<RenderableComponent> renderableStore = ecs.GetComponentStore<RenderableComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (renderableStore == null || troopStore == null || posStore == null) return false;

        bool found = false;
        float foundX = 0f, foundY = 0f;

        renderableStore.ForEach((ulong id) =>
        {
            if (found) return;
            if (renderableStore.GetComponent(id).Type != RenderableType.PlayerBaseCore) return;
            if (!troopStore.HasComponent(id) || troopStore.GetComponent(id).OwnerPlayerId != ownerPlayerId) return;
            if (!posStore.HasComponent(id)) return;

            PositionComponent pos = posStore.GetComponent(id);
            foundX = pos.X;
            foundY = pos.Y;
            found = true;
        });

        x = foundX;
        y = foundY;
        return found;
    }
}
