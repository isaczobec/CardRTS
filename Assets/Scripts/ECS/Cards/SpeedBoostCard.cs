// Simple buff card — no range requirement, playable on any friendly troop, giving it a
// temporary movement-speed boost via a StatModifierComponent-carrying modifier entity (see
// ModifierComponent/StatModifierSystem). The modifier itself gets a short activation delay
// (ActivatableComponent) before the boost actually kicks in — DeployProgressIndicatorManager
// shows that delay's progress following the target troop (since the modifier entity itself
// has no PositionComponent of its own).
public class SpeedBoostCard : TargetEntityCard
{
    private const float SpeedBoostRatio = 0.4f;
    private const float SpeedBoostDurationSeconds = 11f;
    private const float ActivationDelaySeconds = 1f;

    public override CardType Type => CardType.SpeedBoost;
    public override string Title => "Speed Boost";
    public override string ImageName => "SpeedBoost";
    public override string Description => "Gives a friendly troop a temporary movement speed boost.";

    public override int ShopGoldCost => 100; 

    public override StatsComponent DefaultStats => new StatsComponent
    {
        MaxHealth   = StatsComponent.STAT_NA,
        Speed       = StatsComponent.STAT_NA,
        Range       = StatsComponent.STAT_NA,
        Armor       = StatsComponent.STAT_NA,
        Damage      = StatsComponent.STAT_NA,
        AttackSpeed = StatsComponent.STAT_NA,
        SpellResist = StatsComponent.STAT_NA,
    };

    public override ResourceCost Cost => new ResourceCost
        {
            Metal = 40
        };

    // No range requirement at all — playable on any friendly troop anywhere on the map.
    public override bool RequiresFriendlyBuildingRange() => false;
    public override float MaxDistanceFromFriendlyBuilding => 0f; // unused, see above

    public override bool CanTargetFriendly => true;
    public override bool CanTargetEnemyOrNeutral => false;

    public override void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, ulong targetEntityId)
    {
        // TargetEntityCardPlaySystem is server-only (mirrors SpawnAtPointCardPlaySystem),
        // unlike AbilitySystem — OnPlayed only ever runs here on the server, so this
        // modifier entity is never predicted/duplicated client-side. Contrast with e.g.
        // AbilityManager's RingOfProjectilesAbility, which explicitly opts INTO spawning
        // its own modifier on both client and server.
        EntityHandle modifier = ecs.CreateEntity();

        ulong ticksUntilActive = (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds);
        ecs.AddComponent(modifier.Id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });

        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = targetEntityId,
            TicksRemaining = TickManager.SecondsToTicks(SpeedBoostDurationSeconds),
            ModifierID     = ModifierID.StatChange,
        });

        ecs.AddComponent(modifier.Id, new StatModifierComponent
        {
            SpeedRatioBonus = SpeedBoostRatio,
        });

        ecs.AddComponent(modifier.Id, new RenderableModifierComponent
        {
            Type = RenderableModifierType.SpeedBoost,
        });
    }
}
