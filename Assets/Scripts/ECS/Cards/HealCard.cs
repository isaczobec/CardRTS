// Entity-targeted heal spell — reuses HealModifierComponent as a genuine heal-over-time
// (see HealModifierSystem), incrementing once per HealProcPeriodSeconds for the full
// HealDurationSeconds — same shape as HealerGuardianCard's own granted heal-over-time
// (HealPeriodSeconds = 1s procs) rather than a single lump sum. TotalHealAdditive/
// TotalHealRatio (15% of max health plus 100 flat, see HealRequest.Execute) are the sum
// across every proc if the effect runs the full 30 seconds uninterrupted — HealAdditivePerProc/
// HealRatioPerProc below are just those totals divided evenly across the proc count. Also
// carries a RemoveModifierOnDamageComponent (the same component SleepingDraughtCard uses) so
// the modifier — and whatever healing hasn't procced yet — is canceled outright the instant
// the target takes any damage. Reuses ModifierID.Healing/RenderableModifierType.Healing — the
// same icon/status Healer Guardian's own granted heal-over-time already uses.
public class HealCard : TargetEntityCard
{
    private const float HealDurationSeconds = 30f;
    // Mirrors HealerGuardianCard's own HealPeriodSeconds.
    private const float HealProcPeriodSeconds = 1f;
    // Same short activation delay (ActivatableComponent) SpeedBoostCard/BarrierCard's own
    // buffs get before actually kicking in.
    private const float ActivationDelaySeconds = 1f;

    private const float TotalHealAdditive = 100f;
    private const float TotalHealRatio = 0.15f;

    private const float ProcCount = HealDurationSeconds / HealProcPeriodSeconds;
    private const float HealAdditivePerProc = TotalHealAdditive / ProcCount;
    private const float HealRatioPerProc = TotalHealRatio / ProcCount;

    public override int ShopGoldCost => 90;

    public override CardType Type => CardType.Heal;
    public override CardCategory Category => CardCategory.Spell;
    public override string Title => "Heal";
    public override string ImageName => "Heal";
    public override string Description => "Heals a friendly troop for 15% of its max health plus 100, spread out over 30 seconds. Canceled if the target takes any damage.";

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
            Gems = 4,
            Wood = 50,
        };

    // No range requirement at all — playable on any friendly troop anywhere on the map,
    // mirrors BarrierCard/SpeedBoostCard.
    public override bool RequiresFriendlyBuildingRange() => false;
    public override float MaxDistanceFromFriendlyBuilding => 0f; // unused, see above

    public override bool CanTargetFriendly => true;
    public override bool CanTargetEnemyOrNeutral => false;

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
            TicksRemaining = TickManager.SecondsToTicks(HealDurationSeconds),
            ModifierID     = ModifierID.Healing,
        });

        int periodTicks = TickManager.SecondsToTicks(HealProcPeriodSeconds);
        ecs.AddComponent(modifier.Id, new HealModifierComponent
        {
            PeriodTicks         = periodTicks,
            TicksUntilNextProc  = periodTicks,
            HealAdditivePerProc = HealAdditivePerProc,
            HealRatioPerProc    = HealRatioPerProc,
        });

        ecs.AddComponent(modifier.Id, new RemoveModifierOnDamageComponent());

        ecs.AddComponent(modifier.Id, new RenderableModifierComponent
        {
            Type = RenderableModifierType.Healing,
        });
    }
}
