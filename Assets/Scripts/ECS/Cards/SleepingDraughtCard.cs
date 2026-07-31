// Offensive crowd-control card — puts an enemy/neutral troop to "sleep": silenced (see
// SilenceComponent/SilenceSystem — can't attack or use abilities, but can still move) and
// slowed (-30% speed via StatModifierComponent), both bundled onto ONE modifier entity
// alongside a RemoveModifierOnDamageComponent (see RemoveModifierOnDamageSystem) that wakes
// the target — ending the whole effect early — the instant it takes any damage. The modifier
// gets the same short activation delay (ActivatableComponent) SpeedBoostCard/BarrierCard's
// own buffs do before actually kicking in. Playable close to either a friendly building OR a
// friendly troop, same shape as HealerGuardianCard.
using System.Runtime.CompilerServices;

public class SleepingDraughtCard : TargetEntityCard
{
    private const float DurationSeconds = 10f;
    private const float SlowRatio = -0.3f;
    private const float ActivationDelaySeconds = 2f;

    private const float MaxDistanceFromBuilding = 20f;
    private const float MaxDistanceFromTroop = 15f;

    public override CardType Type => CardType.SleepingDraught;
    public override string Title => "Sleeping Draught";
    public override string ImageName => "SleepingDraught";
    public override string Description => "Puts an enemy or neutral troop to sleep for 10 seconds: silenced and 30% slowed. Wakes up early if it takes any damage.";

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
        Wood = 50,
        Metal = 40,
        Stone = 40,
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
        // as BarrierCard/SpeedBoostCard.
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
            ModifierID     = ModifierID.SleepingDraught,
        });

        ecs.AddComponent(modifier.Id, new SilenceComponent());

        ecs.AddComponent(modifier.Id, new StatModifierComponent
        {
            SpeedRatioBonus = SlowRatio,
        });

        ecs.AddComponent(modifier.Id, new RemoveModifierOnDamageComponent());

        ecs.AddComponent(modifier.Id, new RenderableModifierComponent
        {
            Type = RenderableModifierType.SleepingDraught,
        });
    }
}
