// Defensive buff card — same "no range requirement, playable on any friendly troop" shape
// as SpeedBoostCard, giving the target a BarrierComponent-carrying modifier (see
// BarrierSystem) instead of a stat change: a pool of absorption health that fully blocks
// incoming damage until it runs out, rather than reducing/mitigating it. The modifier itself
// gets the same short activation delay (ActivatableComponent) SpeedBoostCard's does before
// the barrier actually starts absorbing hits.
public class BarrierCard : TargetEntityCard
{
    private const float BarrierMaxHealth = 140f;
    // 1 = the barrier loses exactly as much of its own health as the damage it blocks.
    private const float BarrierDamageMultiplier = 1f;
    private const float BarrierDamageAdditiveBonus = 0f;
    private const float ActivationDelaySeconds = 1f;
    private const float BarrierDurationSeconds = 12f;

    public override CardType Type => CardType.Barrier;
    public override CardCategory Category => CardCategory.Spell;
    public override string Title => "Barrier";
    public override string ImageName => "Barrier";
    public override string Description => "Shields a friendly troop with a barrier that fully blocks incoming damage until its own health runs out.";

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

    // Spells now cost only Gems (explicit design ask) — Metal/Stone folded into a single
    // Gems price roughly proportional to the old total resource investment (~8 non-gem
    // units per gem).
    public override ResourceCost Cost => new ResourceCost
        {
            Gems = 15,
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
        // modifier entity is never predicted/duplicated client-side. Mirrors SpeedBoostCard.
        EntityHandle modifier = ecs.CreateEntity();

        ulong ticksUntilActive = (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds);
        ecs.AddComponent(modifier.Id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });

        // Expires after BarrierDurationSeconds even if HealthRemaining never runs out — but
        // can also still end earlier than that on its own once HealthRemaining hits 0 (see
        // BarrierSystem, which shortens TicksRemaining down to end it early rather than
        // waiting out the rest of this duration).
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = targetEntityId,
            TicksRemaining = TickManager.SecondsToTicks(BarrierDurationSeconds),
            ModifierID     = ModifierID.Barrier,
        });

        ecs.AddComponent(modifier.Id, new BarrierComponent
        {
            MaxHealth           = BarrierMaxHealth,
            HealthRemaining     = BarrierMaxHealth,
            DamageMultiplier    = BarrierDamageMultiplier,
            DamageAdditiveBonus = BarrierDamageAdditiveBonus,
        });

        ecs.AddComponent(modifier.Id, new RenderableModifierComponent
        {
            Type = RenderableModifierType.Barrier,
        });
    }
}
