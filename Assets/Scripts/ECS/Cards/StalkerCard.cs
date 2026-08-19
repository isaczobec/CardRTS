using System;
using System.Collections.Generic;

// A BasicMeleeTroopCard variant — same baseline stats, just slightly faster, and equipped
// with AbilityManager's Shadow Cloak ability (temporary untargetability). Its own dedicated
// model/prefab (see RenderableType.Stalker, wired to the "StalkerRenderer" scene object in
// RenderableManager).
//
// Also carries an OnHitScheduleComponent (see OnHitScheduleSystem) — an "ambush" payoff that
// resolves the instant it lands a hit WHILE Shadow Cloak is still active (ResolveAmbush
// checks ShadowCloakSystem.IsCloaked itself and no-ops otherwise; OnHitScheduleComponent's
// own trigger — any hit at all — doesn't know or care about cloak state, same as
// OnKillScheduleComponent firing on every kill): the cloak ends immediately
// (ShadowCloakSystem.RemoveActiveShadowCloakModifiers) instead of running out its own
// duration, the entity it hit is slowed and has its attack speed reduced for a few seconds,
// and the Stalker itself gets a brief attack speed boost. A hit landed after the cloak has
// already ended (naturally or from an earlier ambush) triggers nothing.
public class StalkerCard : SpawnAtPointCard
{
    // Unchanged from BasicMeleeTroopCard — see its own comment for the balance baseline
    // these are scaled from.
    private const int MaxHealth = 220;
    // Slightly faster than BasicMeleeTroopCard.Speed (50) — explicit design ask.
    private const int Speed = 46;
    private const int Range = 5;
    private const int Armor = 20;
    private const int Damage = 30;
    private const float AttackSpeedMilliseconds = 490f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    private const float DetectionRangeMultiplier = 72f; // 6x (explicit design ask) from 12
    // Same neutral:enemy ratio as SkillshotRangedTroopCard's own tuning (explicit design ask)
    // — back down at the pre-6x-bump value; see BasicMeleeTroopCard's own identical comment.
    private const float EnemyDetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 124.8f; // +30% (explicit design ask) from 96
    private const float AttackRangeMultiplier = 1.5f;
    private const float CooldownMultiplier = 3f;

    private const float MaxDistanceFromBuilding = 20f;

    // Cooldown for the troop's Shadow Cloak ability (see AbilityManager.ShadowCloakAbilityId)
    // — comfortably longer than the cloak's own 8s duration so there's real downtime between
    // casts, judgment call consistent with e.g. StoneConstructCard's GroundSlamCooldownSeconds.
    private const float ShadowCloakCooldownSeconds = 45f;

    // Ambush payoff (see OnHitScheduleComponent/ResolveAmbush) — explicit design ask.
    private const float AmbushTargetSlowDurationSeconds = 7f;
    private const float AmbushTargetSlowRatio = -0.4f;
    // AttackSpeed is a tick PERIOD (lower = faster attacks — see StatsQuery.GetAttackSpeed),
    // the opposite of every other stat here, where higher is better. A "-20% attack speed"
    // DEBUFF (attacks slower) therefore needs a POSITIVE ratio here (lengthens the period) —
    // see ModifierIconManager.ResolveStatChange's own comment, which negates this same field
    // right back for display so the UI still reads "-20%" despite the mechanically-inverted
    // sign.
    private const float AmbushTargetAttackSpeedDebuffRatio = 0.3f;
    private const float AmbushSelfAttackSpeedBuffDurationSeconds = 7f;
    // "+60% attack speed" (attacks faster) needs a NEGATIVE ratio (shortens the period) —
    // same inverted-sign reasoning as AmbushTargetAttackSpeedDebuffRatio above.
    private const float AmbushSelfAttackSpeedBuffRatio = -0.75f;

    static StalkerCard()
    {
        ScheduledCallSystem.RegisterCall(ScheduledCallType.StalkerAmbushResolve, ResolveAmbush);
    }

    // Pricier than BasicMeleeTroopCard (100) to match its added utility.
    public override int ShopGoldCost => 120;

    public override CardType Type => CardType.Stalker;
    public override string Title => "Stalker";
    public override string ImageName => "Stalker";
    public override string Description => "A swift melee troop that can cloak itself, becoming untargetable by enemies for a short time.";
    public override string IndicatorPrefabName => "Stalker";

    public const float SelectionScale = 1.5f;

    public override StatsComponent DefaultStats => BuildStats();
    // Metal folded into Wood/Stone, sum unchanged (260) — swift and agile, so the extra
    // goes almost entirely to wood.
    public override ResourceCost Cost => new ResourceCost
        {
            Wood = 210,
            Stone = 50,
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;
    public override int[] GrantedAbilityIds => new[] { AbilityManager.ShadowCloakAbilityId };

    private static StatsComponent BuildStats() => new StatsComponent
    {
        MaxHealth   = MaxHealth,
        Speed       = Speed,
        Range       = Range,
        Armor       = Armor,
        Damage      = Damage,
        AttackSpeed = TickManager.MillisecondsToTicks(AttackSpeedMilliseconds),
        SpellResist = SpellResist,
    };

    public override ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        StatsComponent stats = BuildStats();

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.Stalker, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                EnemyDetectionRangeMultiplier = EnemyDetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                CooldownMultiplier       = CooldownMultiplier,
            }),
            // Shadow Cloak (Q). Slots 2-4 (W/E/R) are left empty (0).
            (e, id) => e.AddComponent(id, new AbilityComponent
            {
                Ability1Id = AbilityManager.ShadowCloakAbilityId,
                Ability1CooldownTicks = TickManager.SecondsToTicks(ShadowCloakCooldownSeconds),
            }),
            // Innate trait, not a modifier — see OnHitScheduleComponent's own doc comment.
            // DelayTicks = 0 resolves as soon as ScheduledCallSystem next runs (effectively
            // the very next tick — see ResolveAmbush's own doc comment), not a deliberate
            // windup like GroundSlam/IceNova's own scheduled calls.
            (e, id) => e.AddComponent(id, new OnHitScheduleComponent
            {
                CallType   = ScheduledCallType.StalkerAmbushResolve,
                DelayTicks = 0,
            }),
        },
        SelectionScale: SelectionScale);
    }

    // The actual ambush payoff — deferred out to ScheduledCallSystem by OnHitScheduleSystem
    // the instant this Stalker lands a hit (see that system for exactly when/why), but only
    // actually does anything if Shadow Cloak is still active at that moment — a plain hit
    // thrown after the cloak's already gone (naturally expired, or consumed by an earlier
    // ambush this same cloak) is a no-op. Runs identically on the server and every predicting
    // client, same as AbilityManager.BuildRingOfProjectilesAbility's own buff — every entity
    // spawned below acts through ModifierComponent.TargetEntityId, not its own id, so
    // client/server ending up with two different entity ids for "the same" buff/debuff is
    // harmless; only ShadowCloakSystem.RemoveActiveShadowCloakModifiers' actual deletion
    // needs (and has) its own isServer gate. call.Param0 is the Stalker's own entity id,
    // call.Param3 is whatever it just hit.
    private static void ResolveAmbush(ECS ecs, ScheduledCallComponent call)
    {
        ulong stalkerId = call.Param0;
        ulong targetId = call.Param3;

        if (!ShadowCloakSystem.IsCloaked(ecs, stalkerId, out _)) return;
        ShadowCloakSystem.RemoveActiveShadowCloakModifiers(ecs, stalkerId);

        if (ecs.HasEntity(targetId))
        {
            EntityHandle debuff = ecs.CreateEntity();
            ecs.AddComponent(debuff.Id, new ModifierComponent
            {
                TargetEntityId = targetId,
                TicksRemaining = TickManager.SecondsToTicks(AmbushTargetSlowDurationSeconds),
                ModifierID     = ModifierID.StatChange,
            });
            ecs.AddComponent(debuff.Id, new StatModifierComponent
            {
                SpeedRatioBonus       = AmbushTargetSlowRatio,
                AttackSpeedRatioBonus = AmbushTargetAttackSpeedDebuffRatio,
            });
        }

        if (ecs.HasEntity(stalkerId))
        {
            EntityHandle buff = ecs.CreateEntity();
            ecs.AddComponent(buff.Id, new ModifierComponent
            {
                TargetEntityId = stalkerId,
                TicksRemaining = TickManager.SecondsToTicks(AmbushSelfAttackSpeedBuffDurationSeconds),
                ModifierID     = ModifierID.StatChange,
            });
            ecs.AddComponent(buff.Id, new StatModifierComponent
            {
                AttackSpeedRatioBonus = AmbushSelfAttackSpeedBuffRatio,
                SpeedRatioBonus = 0.2f
            });
        }
    }
}
