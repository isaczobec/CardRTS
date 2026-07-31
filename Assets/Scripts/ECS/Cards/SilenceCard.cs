// Offensive crowd-control card — pure silence (see SilenceComponent/SilenceSystem: can't
// attack or use abilities, but can still move) on an enemy/neutral troop, with no slow and
// no early-removal-on-damage — unlike SleepingDraughtCard, which layers both of those on
// top of the same underlying SilenceComponent. The modifier gets the same short activation
// delay (ActivatableComponent) SpeedBoostCard/SleepingDraughtCard's own buffs do before
// actually kicking in. Playable close to either a friendly building OR a friendly troop,
// same shape as HealerGuardianCard/SleepingDraughtCard.
public class SilenceCard : TargetEntityCard
{
    private const float DurationSeconds = 11f;
    private const float ActivationDelaySeconds = 2f;

    private const float MaxDistanceFromBuilding = 20f;
    private const float MaxDistanceFromTroop = 15f;

    public override CardType Type => CardType.Silence;
    public override CardCategory Category => CardCategory.Spell;
    public override string Title => "Silence";
    public override string ImageName => "Silence";
    public override string Description => "Silences an enemy or neutral troop for 8 seconds — it cannot attack or use abilities, but can still move.";

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
            Gems = 20,
        };

    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;
    public override bool AllowsFriendlyTroopRange() => true;
    public override float MaxDistanceFromFriendlyTroop => MaxDistanceFromTroop;

    public override bool CanTargetFriendly => false;
    public override bool CanTargetEnemyOrNeutral => true;

    public override void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, ulong targetEntityId)
    {
        // TargetEntityCardPlaySystem is server-only (mirrors SpawnAtPointCardPlaySystem),
        // so this modifier entity is never predicted/duplicated client-side — same reasoning
        // as BarrierCard/SpeedBoostCard/SleepingDraughtCard.
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
            TicksRemaining = TickManager.SecondsToTicks(DurationSeconds),
            ModifierID     = ModifierID.Silence,
        });

        ecs.AddComponent(modifier.Id, new SilenceComponent());

        ecs.AddComponent(modifier.Id, new RenderableModifierComponent
        {
            Type = RenderableModifierType.Silence,
        });
    }
}
