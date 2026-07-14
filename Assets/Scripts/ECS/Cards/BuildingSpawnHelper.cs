// Shared "add every component a basic building needs" logic for BuildingCard and world-gen
// player bases (SpawnPlayerBasesFeature) — the two differ only in RenderableType, max
// health, and activation delay, so both funnel through here to avoid the two definitions
// drifting apart. Caller is expected to have already created the entity and added
// PositionComponent (BuildingCard does this itself; EntitySpawnAction does it before
// invoking its Spawner).
public static class BuildingSpawnHelper
{
    public const int Armor = 5;
    public const float BlockRadius = 3f;
    public const float SelectionScale = 3f;

    public static void AddBuildingComponents(ECS ecs, ulong id, ushort ownerPlayerId, RenderableType renderableType, int maxHealth, ulong ticksUntilActive,
        float cardPlayRangeMultiplier = 1f, float cardPlayRangeBonus = 0f)
    {
        ecs.AddComponent(id, new TroopComponent
        {
            OwnerPlayerId = ownerPlayerId,
        });

        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });

        ecs.AddComponent(id, new RenderableComponent { Type = renderableType });
        ecs.AddComponent(id, new SelectableComponent { OwnerPlayerId = ownerPlayerId, Scale = SelectionScale });

        ecs.AddComponent(id, new StatsComponent
        {
            MaxHealth = maxHealth,
            Armor     = Armor,
            // Speed/Range/Damage/AttackSpeed left at 0 — buildings don't move or attack.
        });
        ecs.AddComponent(id, new HealthComponent { CurrentHealth = maxHealth });
        ecs.AddComponent(id, new BuildingComponent
        {
            BlockRadius             = BlockRadius,
            CardPlayRangeMultiplier = cardPlayRangeMultiplier,
            CardPlayRangeBonus      = cardPlayRangeBonus,
        });
    }
}
