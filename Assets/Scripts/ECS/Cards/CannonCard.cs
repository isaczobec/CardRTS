// A defensive turret building — built via the same BuildingSpawnHelper shared logic every
// other building uses (BuildingCard, world-gen player bases), just passing real Range/
// Damage/AttackSpeed through its combat-stats params instead of leaving them at 0, plus a
// TurretAIComponent (see TurretAISystem) and its own pool of homing SeekingProjectileComponent
// projectiles (RenderableType.CannonProjectile) to actually fire with. Unlike every troop
// card, this building can never be manually targeted/ordered — TurretAISystem picks its own
// target entirely on its own.
public class CannonCard : SpawnAtPointCard
{
    // Tankier than a plain Building (350) — this is meant to be worth defending/attacking.
    // Cut by 30% (explicit design ask) from the original 500.
    private const int MaxHealth = 350;
    private const int Range = 14;
    private const int Damage = 55;
    // Slow, heavy shots — a defensive structure, not a DPS race.
    private const float AttackSpeedMilliseconds = 1400f;

    private const float ActivationDelaySeconds = 2f;

    private const int ProjectilePoolSize = 12;
    private const int ProjectileSpeedMilliTilesPerSecond = 19000; // 12 tiles/sec

    // Small leeway (as a multiple of Range) allowed when re-checking the target is still in
    // range once the windup finishes — mirrors every ranged troop's own AttackRangeMultiplier.
    private const float AttackRangeMultiplier = 1.2f;
    // Multiple of the attack windup (AttackSpeed ticks) the post-shot wind-down lasts —
    // mirrors BasicRangedAIComponent.WindDownMultiplier.
    private const float WindDownMultiplier = 1.5f;

    // Gold dropped to whoever destroys this building — mirrors BuildingCard's own.
    private const int GoldDropOnDeath = 30;

    private const float MaxDistanceFromBuilding = 30f;

    public override int ShopGoldCost => 160;

    public override CardType Type => CardType.Cannon;
    public override CardCategory Category => CardCategory.Building;
    public override string Title => "Cannon";
    public override string ImageName => "Cannon";
    public override string Description => "A stationary cannon that automatically fires at the closest enemy troop within range.";
    public override string IndicatorPrefabName => "Cannon";

    // Speed is STAT_NA (not just 0) — this building doesn't move, so that row shouldn't be
    // shown on the card face at all. Range/Damage/AttackSpeed ARE real, unlike BuildingCard.
    public override StatsComponent DefaultStats => new StatsComponent
    {
        MaxHealth   = MaxHealth,
        Speed       = StatsComponent.STAT_NA,
        Range       = Range,
        Armor       = BuildingSpawnHelper.Armor,
        Damage      = Damage,
        AttackSpeed = TickManager.MillisecondsToTicks(AttackSpeedMilliseconds),
        SpellResist = BuildingSpawnHelper.SpellResist,
    };

    public override ResourceCost Cost => new ResourceCost
        {
            Stone = 150,
            Metal = 80
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;

    public override ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(x, y));
        BuildingSpawnHelper.AddBuildingComponents(ecs, id, ownerPlayerId, RenderableType.Cannon, MaxHealth,
            (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds),
            range: Range, damage: Damage, attackSpeedTicks: TickManager.MillisecondsToTicks(AttackSpeedMilliseconds));

        ecs.AddComponent(id, new TurretAIComponent
        {
            WindDownMultiplier    = WindDownMultiplier,
            AttackRangeMultiplier = AttackRangeMultiplier,
        });

        ulong firstProjectileId = ProjectilePool.CreatePool(ecs, id, ProjectilePoolSize, ProjectileSpeedMilliTilesPerSecond,
            renderableType: RenderableType.CannonProjectile);
        ecs.AddComponent(id, new ProjectileOwnerComponent
        {
            MaxProjectiles   = ProjectilePoolSize,
            NextProjectileId = firstProjectileId,
        });

        ecs.AddComponent(id, new OnDeathResourceDropComponent { Drop = new ResourceCost
        {
            Gold = GoldDropOnDeath
        } } );

        return id;
    }
}
