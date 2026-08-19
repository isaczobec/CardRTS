// Offensive counterpart to SpeedBoostCard — same shape (a plain StatModifierComponent-
// carrying modifier entity, same short ActivatableComponent deploy delay before it kicks
// in, no range requirement at all), just targeting an enemy/neutral troop instead of a
// friendly one and applying a movement-speed PENALTY instead of a bonus. Reuses
// ModifierID.Chilled/RenderableModifierType.Chilled for its icon rather than a dedicated
// one — mirrors CripplingStrikesUpgrade/ProjectileOnHitSystem.ApplySlow's own convention of
// treating "Chilled" as the generic slow-debuff presentation regardless of source.
public class SlownessCard : TargetEntityCard
{
    private const float SlowRatio = -0.4f;
    private const float SlowDurationSeconds = 8f;
    private const float ActivationDelaySeconds = 1f;

    public override CardType Type => CardType.Slowness;
    public override CardCategory Category => CardCategory.Spell;
    public override string Title => "Slowness";
    public override string ImageName => "Slowness";
    public override string Description => $"Slows an enemy troop by {-SlowRatio * 100f:0}% for {SlowDurationSeconds:0} seconds.";

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

    // Spells now cost only Gems (explicit design ask) — Metal folded into a single Gems
    // price roughly proportional to the old total resource investment (~8 non-gem units
    // per gem).
    public override ResourceCost Cost => new ResourceCost
        {
            Gems = 7
        };

    // No range requirement at all — mirrors SpeedBoostCard, playable on any enemy troop
    // anywhere on the map.
    public override bool RequiresFriendlyBuildingRange() => false;
    public override float MaxDistanceFromFriendlyBuilding => 0f; // unused, see above

    public override bool CanTargetFriendly => false;
    public override bool CanTargetEnemyOrNeutral => true;

    public override void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, ulong targetEntityId)
    {
        // TargetEntityCardPlaySystem is server-only (mirrors SpawnAtPointCardPlaySystem),
        // unlike AbilitySystem — OnPlayed only ever runs here on the server, so this
        // modifier entity is never predicted/duplicated client-side. Mirrors SpeedBoostCard.
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
            TicksRemaining = TickManager.SecondsToTicks(SlowDurationSeconds),
            ModifierID     = ModifierID.Chilled,
        });

        ecs.AddComponent(modifier.Id, new StatModifierComponent
        {
            SpeedRatioBonus = SlowRatio,
        });

        ecs.AddComponent(modifier.Id, new RenderableModifierComponent
        {
            Type = RenderableModifierType.Chilled,
        });
    }
}
