// Shared "add every component a basic building needs" logic for BuildingCard and world-gen
// player bases (SpawnPlayerBasesFeature) — the two differ only in RenderableType, max
// health, and activation delay, so both funnel through here to avoid the two definitions
// drifting apart. Caller is expected to have already created the entity and added
// PositionComponent (BuildingCard does this itself; EntitySpawnAction does it before
// invoking its Spawner).
public static class BuildingSpawnHelper
{
    public const int Armor = 40;
    public const int SpellResist = 150;
    public const float BlockRadius = 3f;
    public const float SelectionScale = 3f;

    // range/damage/attackSpeedTicks default to 0 — the original "buildings don't move or
    // attack" behavior every existing caller (BuildingCard, world-gen player bases) still
    // gets unchanged. A combat building (e.g. CannonCard) passes real values for whichever
    // of these it actually uses.
    public static void AddBuildingComponents(ECS ecs, ulong id, ushort ownerPlayerId, RenderableType renderableType, int maxHealth, ulong ticksUntilActive,
        float cardPlayRangeMultiplier = 1f, float cardPlayRangeBonus = 0f, int range = 0, int damage = 0, int attackSpeedTicks = 0)
    {
        ecs.AddComponent(id, new TroopComponent
        {
            OwnerPlayerId = ownerPlayerId,
            IsPhysicalTroop = false,
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
            MaxHealth   = maxHealth,
            Armor       = Armor,
            SpellResist = SpellResist,
            // Speed left at 0 — buildings never move, full stop. Range/Damage/AttackSpeed
            // default to 0 too (still "buildings don't attack") but a caller can pass real
            // values for a combat building — see the params above.
            Range       = range,
            Damage      = damage,
            AttackSpeed = attackSpeedTicks,
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
