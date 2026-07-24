using System.Collections.Generic;
using UnityEngine;

// Maps an ability ID (equipped in an AbilityComponent slot) to its Ability definition —
// analogous to CardRegistry for Card. Abilities are stateless data+behavior singletons
// looked up by ID, not constructed per-entity.
public static class AbilityManager
{
    // Test ability #1 — instant activate, fires a ring of the caster's own pooled
    // projectiles outward (see SkillshotRangedTroopCard).
    public const int RingOfProjectilesAbilityId = 1;

    // Test ability #2 — target location, spawns an AoeSpellCard-identical entity at the
    // point (see SkillshotRangedTroopCard).
    public const int AoeSpellCloneAbilityId = 2;

    // Test ability #3 — target location, fires one of the caster's own pooled skillshot
    // projectiles straight at the point (see SkillshotRangedTroopCard). Exists mainly to
    // exercise the direction-arrow indicator (ShowDirectionArrow) — see
    // AbilityIndicatorManager.
    public const int SkillshotAbilityId = 3;

    // Test ability #4 — target entity, deals direct damage to an enemy/neutral within
    // Range (see BasicMeleeTroopCard). Exists mainly to exercise AbilityType.TargetEntity
    // and its target-indicator preview — see AbilityIndicatorManager/EntityTargetIndicator.
    public const int MeleeStrikeAbilityId = 4;

    // Instant activate, freezes (StunnedComponent, ModifierID.Frozen) every chilled enemy
    // within IceNovaRangeMultiplier x the caster's own Range stat, consuming their Chilled
    // debuff in the process (see IceManCard, the only troop that currently equips this).
    public const int IceNovaAbilityId = 5;

    // Target entity (friendly or enemy), knocks whatever's targeted away from the caster via
    // DisplacementSystem — a no-op if the target has no MovableComponent at all (e.g. a
    // building). Test ability exercising DisplacementSystem; not currently equipped by any
    // troop (see GroundSlamAbilityId, StoneConstructCard's windup-based version of the
    // same push).
    public const int PushAbilityId = 6;

    // Target entity (friendly or enemy) — same push as PushAbilityId, but winds up for
    // GroundSlamWindupSeconds first (ActionWindupComponent blocks the caster's own
    // CanMove/CanPerform for the duration) before resolving via ScheduledCallSystem, instead
    // of pushing instantly (see StoneConstructCard, the only troop that currently equips
    // this).
    public const int GroundSlamAbilityId = 7;

    // Instant activate, cloaks the caster (ShadowCloakComponent, ModifierID.ShadowCloak) so
    // it can't be selected/targeted by anyone but its own owner for
    // ShadowCloakDurationSeconds — see ShadowCloakSystem/StalkerCard, the only troop that
    // currently equips this. Already-fired projectiles (seeking or hitbox) still land, since
    // neither SeekingProjectileSystem nor SkillshotProjectileSystem consult the veto this
    // relies on.
    public const int ShadowCloakAbilityId = 8;

    private const int RingProjectileCount = 8;
    private const float AoeSpellCloneRange = 8f;
    // The shot always travels the caster's second projectile pool's own Range (see
    // ProjectilePool.AimSkillshot/ResolveOwnerAtIndex) regardless of where within this
    // circle you aim (DirectionArrowAlwaysMaxRange below) — kept in step with
    // SkillshotRangedTroopCard.AbilityProjectileRange so the indicator circle always shows
    // exactly as far as the shot will actually go.
    private const float SkillshotAbilityRange = 84f;
    // Short windup before the shot actually fires — see BuildSkillshotAbility. The caster
    // can't move or act (ActionWindupComponent vetoes CanMove/CanPerform) for the duration.
    private const float SkillshotWindupSeconds = 0.4f;
    private const float MeleeStrikeRange = 6f;
    private const int MeleeStrikeDamage = 25;
    // Not used for any cast validation (RingOfProjectilesAbility is Instant — no location
    // to check), only so AbilityIndicatorManager can preview roughly how far the fired
    // projectiles will travel. Abilities are stateless/shared, so this can't read the
    // caster's own Range stat dynamically; kept in step with SkillshotRangedTroopCard.Range
    // (the only troop that currently equips this ability), which is what
    // ProjectilePool.AimSkillshot actually uses for RangeRemaining.
    private const float RingOfProjectilesRange = 16f;

    // The caster's own +50%-speed-for-3s buff (see BuildRingOfProjectilesAbility).
    private const float RingOfProjectilesSpeedBoostRatio = 0.5f;
    private const float RingOfProjectilesSpeedBoostSeconds = 3f;

    // How far (as a multiple of the caster's own Range stat) Ice Nova reaches.
    private const float IceNovaRangeMultiplier = 3f;
    // IceManCard.Range (14) x IceNovaRangeMultiplier — not used for any cast validation
    // (Instant — no location to check), only so AbilityIndicatorManager can preview roughly
    // how far this reaches. Abilities are stateless/shared, so this can't read the caster's
    // own Range stat dynamically for the preview the way the actual effect radius does at
    // cast time; kept in step with IceManCard.Range, same limitation as
    // RingOfProjectilesRange's own comment describes.
    private const float IceNovaRange = 42f;
    private const int IceNovaFallbackRange = 10;
    private const float IceNovaFreezeDurationSeconds = 3f;
    // Cast time before the nova actually resolves — see BuildIceNovaAbility. Mirrors
    // BuildSkillshotAbility's own SkillshotWindupSeconds: an ActionWindupComponent blocks
    // the caster's own CanMove/CanPerform for the duration, and ResolveIceNova (the actual
    // effect) is deferred via ScheduledCallSystem to fire on the windup's very last tick.
    private const float IceNovaCastTimeSeconds = 1f;

    private const float PushRange = 6f;
    // World units/second — DisplacementSystem steps the target by this every tick for
    // PushDurationSeconds, in the direction straight away from the caster at the moment the
    // push lands.
    private const float PushSpeed = 20f;
    private const float PushDurationSeconds = 0.3f;

    private const float GroundSlamRange = 6f;
    private const float GroundSlamSpeed = 20f;
    private const float GroundSlamDisplacementDurationSeconds = 0.3f;
    // Cast time before the slam actually lands — see BuildGroundSlamAbility. Mirrors
    // BuildIceNovaAbility's own IceNovaCastTimeSeconds: an ActionWindupComponent blocks the
    // caster's own CanMove/CanPerform for the duration, and ResolveGroundSlam (the actual
    // push) is deferred via ScheduledCallSystem to fire on the windup's very last tick.
    private const float GroundSlamWindupSeconds = 0.6f;

    // How long Shadow Cloak's untargetability lasts — explicit design ask (StalkerCard).
    private const float ShadowCloakDurationSeconds = 8f;

    // Scratch, reused across every Ice Nova cast rather than reallocated per cast.
    private static readonly List<ulong> _queryBuffer = new List<ulong>();

    private static readonly Dictionary<int, Ability> _abilities = new Dictionary<int, Ability>
    {
        { RingOfProjectilesAbilityId, BuildRingOfProjectilesAbility() },
        { AoeSpellCloneAbilityId, BuildAoeSpellCloneAbility() },
        { SkillshotAbilityId, BuildSkillshotAbility() },
        { MeleeStrikeAbilityId, BuildMeleeStrikeAbility() },
        { IceNovaAbilityId, BuildIceNovaAbility() },
        { PushAbilityId, BuildPushAbility() },
        { GroundSlamAbilityId, BuildGroundSlamAbility() },
        { ShadowCloakAbilityId, BuildShadowCloakAbility() },
    };

    // Runs after the field initializers above (C# guarantees static field initializers run
    // before an explicit static constructor's body), registering ResolveIceNova with
    // ScheduledCallSystem so BuildIceNovaAbility's deferred call can find it. This is "at
    // initialization time" in the sense that matters: AbilityManager can't be touched at all
    // (e.g. via TryGet, or BuildIceNovaAbility's own ExecuteInstant below scheduling a call)
    // without this type's static constructor having already run first, so the registration
    // is always in place before any IceNovaResolve ScheduledCallComponent could possibly
    // exist to look it up.
    static AbilityManager()
    {
        ScheduledCallSystem.RegisterCall(ScheduledCallType.IceNovaResolve, ResolveIceNova);
        ScheduledCallSystem.RegisterCall(ScheduledCallType.GroundSlamResolve, ResolveGroundSlam);
    }

    public static bool TryGet(int abilityId, out Ability ability) => _abilities.TryGetValue(abilityId, out ability);

    private static Ability BuildRingOfProjectilesAbility() => new Ability
    {
        Type = AbilityType.Instant,
        Name = "Ring of Projectiles",
        Description = "Fires a ring of projectiles outward in every direction and grants a brief speed boost.",
        Range = RingOfProjectilesRange,
        ImageName = "RingOfProjectiles",
        // No cast location for an Instant ability, so this is just the plain range-circle
        // preview around the caster — no cursor circle/arrow, nothing to clamp.
        ShowRangeCircle = true,
        ExecuteInstant = (ecs, input) =>
        {
            ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
            if (posStore == null || !posStore.HasComponent(input.CastingEntityId)) return;

            PositionComponent casterPos = posStore.GetComponent(input.CastingEntityId);
            Vector2 firePosition = new Vector2(casterPos.X, casterPos.Y);

            for (int i = 0; i < RingProjectileCount; i++)
            {
                float angle = i * (360f / RingProjectileCount) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                ProjectilePool.FireInDirection(ecs, input.CastingEntityId, direction, firePosition);
            }

            // Spawned unconditionally on both the predicting client and the server, unlike
            // AoeSpellCloneAbility above — by design (see ModifierComponent's own doc
            // comment), a modifier only ever acts through StatModifierSystem's request
            // subscriptions, which key off ModifierComponent.TargetEntityId, not the
            // modifier entity's own ID, so the client and server ending up with two
            // different entity IDs for "the same" buff is harmless.
            EntityHandle modifier = ecs.CreateEntity();
            ecs.AddComponent(modifier.Id, new ModifierComponent
            {
                TargetEntityId = input.CastingEntityId,
                TicksRemaining = TickManager.SecondsToTicks(RingOfProjectilesSpeedBoostSeconds),
                ModifierID     = ModifierID.StatChange,
            });
            ecs.AddComponent(modifier.Id, new StatModifierComponent
            {
                SpeedRatioBonus = RingOfProjectilesSpeedBoostRatio,
            });
        },
    };

    private static Ability BuildAoeSpellCloneAbility() => new Ability
    {
        Type = AbilityType.TargetLocation,
        Name = "AOE Blast",
        Description = "Creates an area-of-effect blast at the targeted point.",
        Range = AoeSpellCloneRange,
        ImageName = "AoeSpellClone",
        ShowRangeCircle = true,
        // Previews the spell's actual blast radius, not the cast range above — reads
        // AoeSpellCard.Range directly so the preview can never drift from what actually
        // spawns (see AoeSpellCard.OnPlayed, which this ability calls).
        ShowCursorCircle = true,
        CursorCircleRadius = AoeSpellCard.Range,
        ExecuteAtLocation = (ecs, input) =>
        {
            // AbilitySystem runs unconditionally (predicted on clients too), but spawning
            // an entity must not be — a client that also created one here would end up
            // with an extra, never-reconciled "ghost" spell alongside the server's
            // authoritative one. Mirrors SpawnAtPointCardPlaySystem's own isServer guard,
            // since this ends up calling the same OnPlayed a played AoeSpellCard would.
            bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
            if (!isServer) return;

            ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
            if (troopStore == null || !troopStore.HasComponent(input.CastingEntityId)) return;

            // Reused rather than duplicated, so the ability's spawned entity never drifts
            // from what AoeSpellCard itself spawns.
            if (!CardRegistry.TryGet(CardType.AoeSpell, out Card card) || card is not SpawnAtPointCard aoeSpellCard) return;

            ushort ownerPlayerId = troopStore.GetComponent(input.CastingEntityId).OwnerPlayerId;
            aoeSpellCard.OnPlayed(ecs, 0, ownerPlayerId, input.X, input.Y);
        },
    };

    // Winds up for SkillshotWindupSeconds (ActionWindupComponent — blocks the caster's own
    // CanMove/CanPerform for the duration, so it can't move, auto-attack, or cast another
    // ability while winding up), then fires one of the caster's own pooled projectiles
    // (FireProjectileOnExpireComponent — ProjectilePool.FireInDirection under the hood;
    // aims whatever kind the pool holds, a skillshot troop's pool is skillshot-typed, see
    // SkillshotRangedTroopCard) in a straight line toward the cast point, captured once at
    // cast time so it still fires exactly where aimed regardless of what happens during the
    // windup. Deterministic and side-effect-free like RingOfProjectilesAbility, so — unlike
    // AoeSpellCloneAbility — no isServer guard is needed; both server and predicting clients
    // creating "the same" modifier and firing "the same" pooled projectile off it is exactly
    // how every other pooled shot in this codebase already works.
    private static Ability BuildSkillshotAbility()
    {
        Ability ability = new Ability
        {
            Type = AbilityType.TargetLocation,
            Name = "Piercing Shot",
            Description = "Winds up briefly, then fires a piercing shot straight toward the targeted point, hitting everything in its path.",
            Range = SkillshotAbilityRange,
            ImageName = "SkillshotAbility",
            ShowRangeCircle = true,
            ShowDirectionArrow = true,
            // The shot always travels the full Range regardless of where within it you aim,
            // so the arrow should always read as the full range circle's radius, not shrink
            // to wherever the cursor happens to be.
            DirectionArrowAlwaysMaxRange = true,
            // Fires from the caster's SECOND pool (index 1 — see
            // ProjectileOwnerComponent's own doc comment) rather than its default
            // auto-attack pool, so this ability can fire a visually/mechanically distinct
            // (faster, longer-range) projectile — see SkillshotRangedTroopCard, which sets
            // up that second pool.
            ProjectileOwnerIndex = 1,
        };

        ability.ExecuteAtLocation = (ecs, input) =>
        {
            ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
            if (posStore == null || !posStore.HasComponent(input.CastingEntityId)) return;

            PositionComponent casterPos = posStore.GetComponent(input.CastingEntityId);
            Vector2 firePosition = new Vector2(casterPos.X, casterPos.Y);
            Vector2 direction = new Vector2(input.X, input.Y) - firePosition;

            ulong poolOwnerId = ProjectilePool.ResolveOwnerAtIndex(ecs, input.CastingEntityId, ability.ProjectileOwnerIndex);

            EntityHandle modifier = ecs.CreateEntity();
            ecs.AddComponent(modifier.Id, new ModifierComponent
            {
                TargetEntityId = input.CastingEntityId,
                TicksRemaining = Mathf.Max(1, TickManager.SecondsToTicks(SkillshotWindupSeconds)),
            });
            ecs.AddComponent(modifier.Id, new ActionWindupComponent());
            ecs.AddComponent(modifier.Id, new FireProjectileOnExpireComponent
            {
                DirectionX = direction.x,
                DirectionY = direction.y,
                ProjectilePoolOwnerId = poolOwnerId,
            });

            ecs.FlagEvents.Add(new AttackWindupBeganEvent { EntityId = input.CastingEntityId });
        };

        return ability;
    }

    // Deterministic and side-effect-free like the other test abilities — a DamageRequest is
    // just enqueued and flushed the same tick by DamageResolutionSystem (which runs after
    // AbilitySystem), exactly the way BasicMeleeAISystem's own attacks already work, so no
    // isServer guard is needed here either.
    private static Ability BuildMeleeStrikeAbility() => new Ability
    {
        Type = AbilityType.TargetEntity,
        Name = "Melee Strike",
        Description = "Deals direct damage to a nearby enemy.",
        Range = MeleeStrikeRange,
        ImageName = "MeleeStrike",
        CanTargetFriendly = false,
        CanTargetEnemyOrNeutral = true,
        ShowRangeCircle = true,
        ShowTargetIndicator = true,
        ExecuteOnEntity = (ecs, input) =>
        {
            ecs.Requests.CreateRequest(new DamageRequest(input.TargetEntityId, MeleeStrikeDamage) { DealerEntityId = input.CastingEntityId });
        },
    };

    // Starts the target's displacement via DisplacementSystem.BeginDisplacement (a no-op if
    // it has no MovableComponent at all, e.g. a building) — deterministic and side-effect-free
    // like MeleeStrikeAbility's own DamageRequest (a plain component mutation, not an entity
    // create/delete), so no isServer guard is needed here either.
    private static Ability BuildPushAbility() => new Ability
    {
        Type = AbilityType.TargetEntity,
        Name = "Shove",
        Description = "Pushes a nearby troop away, briefly knocking it out of control.",
        Range = PushRange,
        ImageName = "Push",
        CanTargetFriendly = true,
        CanTargetEnemyOrNeutral = true,
        ShowRangeCircle = true,
        ShowTargetIndicator = true,
        ExecuteOnEntity = (ecs, input) =>
        {
            ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
            if (posStore == null) return;
            if (!posStore.HasComponent(input.TargetEntityId) || !posStore.HasComponent(input.CastingEntityId)) return;

            PositionComponent casterPos = posStore.GetComponent(input.CastingEntityId);
            PositionComponent targetPos = posStore.GetComponent(input.TargetEntityId);
            Vector2 direction = new Vector2(targetPos.X - casterPos.X, targetPos.Y - casterPos.Y);
            // Caster and target exactly overlapping is the only way this is zero — an
            // arbitrary but deterministic fallback direction beats a NaN from normalizing a
            // zero vector.
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;

            DisplacementSystem.BeginDisplacement(ecs, input.TargetEntityId,
                direction.x * PushSpeed, direction.y * PushSpeed, TickManager.SecondsToTicks(PushDurationSeconds));
        },
    };

    // Same push as BuildPushAbility, but winds up for GroundSlamWindupSeconds first
    // (ActionWindupComponent — blocks the caster's own CanMove/CanPerform for the duration,
    // exactly like BuildSkillshotAbility/BuildIceNovaAbility) before resolving via
    // ResolveGroundSlam below, deferred through ScheduledCallSystem. Deterministic and
    // side-effect-free itself (the windup modifier and the scheduled call both act through
    // TargetEntityId/ScheduledCall matching, not the entities' own ids), so no isServer guard
    // is needed here — same reasoning as BuildIceNovaAbility.
    private static Ability BuildGroundSlamAbility() => new Ability
    {
        Type = AbilityType.TargetEntity,
        Name = "Ground Slam",
        Description = "Winds up briefly, then slams the ground, pushing a nearby troop away.",
        Range = GroundSlamRange,
        ImageName = "GroundSlam",
        CanTargetFriendly = true,
        CanTargetEnemyOrNeutral = true,
        ShowRangeCircle = true,
        ShowTargetIndicator = true,
        ExecuteOnEntity = (ecs, input) =>
        {
            ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
            if (posStore == null) return;
            if (!posStore.HasComponent(input.CastingEntityId) || !posStore.HasComponent(input.TargetEntityId)) return;

            int windupTicks = Mathf.Max(1, TickManager.SecondsToTicks(GroundSlamWindupSeconds));

            EntityHandle modifier = ecs.CreateEntity();
            ecs.AddComponent(modifier.Id, new ModifierComponent
            {
                TargetEntityId = input.CastingEntityId,
                TicksRemaining = windupTicks,
            });
            ecs.AddComponent(modifier.Id, new ActionWindupComponent());

            // param0 = caster, param3 = the originally-targeted entity — both re-read live
            // at resolve time (ResolveGroundSlam), not captured here, so the push direction
            // reflects where they actually are once the windup ends rather than where they
            // stood when the cast began.
            ScheduledCallSystem.Schedule(ecs, ScheduledCallType.GroundSlamResolve, windupTicks,
                input.CastingEntityId, param3: input.TargetEntityId);

            ecs.FlagEvents.Add(new AttackWindupBeganEvent { EntityId = input.CastingEntityId });
        },
    };

    // The actual Ground Slam push — deferred out of BuildGroundSlamAbility's ExecuteOnEntity
    // so it can be scheduled to run once the windup above ends, via ScheduledCallSystem
    // (registered against ScheduledCallType.GroundSlamResolve in the static constructor
    // above). See BuildPushAbility for the push logic itself, which this mirrors exactly.
    private static void ResolveGroundSlam(ECS ecs, ScheduledCallComponent call)
    {
        ulong casterId = call.Param0;
        ulong targetId = call.Param3;

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (posStore == null) return;
        if (!posStore.HasComponent(casterId) || !posStore.HasComponent(targetId)) return;

        PositionComponent casterPos = posStore.GetComponent(casterId);
        PositionComponent targetPos = posStore.GetComponent(targetId);
        Vector2 direction = new Vector2(targetPos.X - casterPos.X, targetPos.Y - casterPos.Y);
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;

        DisplacementSystem.BeginDisplacement(ecs, targetId,
            direction.x * GroundSlamSpeed, direction.y * GroundSlamSpeed, TickManager.SecondsToTicks(GroundSlamDisplacementDurationSeconds));
    }

    // Instant, self-targeted, deterministic and side-effect-free like RingOfProjectilesAbility
    // (spawns a modifier entity acting through ModifierComponent.TargetEntityId, not the
    // modifier entity's own id) — so, like that ability, no isServer guard is needed; both
    // the server and a predicting client creating "the same" cloak modifier is harmless.
    private static Ability BuildShadowCloakAbility() => new Ability
    {
        Type = AbilityType.Instant,
        Name = "Shadow Cloak",
        Description = "Cloaks the caster, making it untargetable by enemies for a short time. Shots already fired at it can still land.",
        ImageName = "ShadowCloak",
        ExecuteInstant = (ecs, input) =>
        {
            EntityHandle modifier = ecs.CreateEntity();
            ecs.AddComponent(modifier.Id, new ModifierComponent
            {
                TargetEntityId = input.CastingEntityId,
                TicksRemaining = TickManager.SecondsToTicks(ShadowCloakDurationSeconds),
                ModifierID     = ModifierID.ShadowCloak,
            });
            ecs.AddComponent(modifier.Id, new ShadowCloakComponent());
            ecs.AddComponent(modifier.Id, new RenderableModifierComponent
            {
                Type = RenderableModifierType.ShadowCloak,
            });
        },
    };

    // Winds up for IceNovaCastTimeSeconds (ActionWindupComponent — blocks the caster's own
    // CanMove/CanPerform for the duration, exactly like BuildSkillshotAbility), then
    // resolves via ResolveIceNova below. Deterministic and side-effect-free itself (the
    // windup modifier and the scheduled call both act through TargetEntityId/ScheduledCall
    // matching, not the entities' own ids), so — like RingOfProjectilesAbility — no
    // isServer guard is needed here; the actual server-only deletion concerns live inside
    // ResolveIceNova and ScheduledCallSystem instead.
    private static Ability BuildIceNovaAbility() => new Ability
    {
        Type = AbilityType.Instant,
        Name = "Ice Nova",
        Description = "Winds up briefly, then freezes every chilled enemy within range solid, consuming the chill.",
        Range = IceNovaRange,
        ImageName = "IceNova",
        ShowRangeCircle = true,
        ExecuteInstant = (ecs, input) =>
        {
            ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
            if (posStore == null || !posStore.HasComponent(input.CastingEntityId)) return;

            int windupTicks = Mathf.Max(1, TickManager.SecondsToTicks(IceNovaCastTimeSeconds));

            EntityHandle modifier = ecs.CreateEntity();
            ecs.AddComponent(modifier.Id, new ModifierComponent
            {
                TargetEntityId = input.CastingEntityId,
                TicksRemaining = windupTicks,
            });
            ecs.AddComponent(modifier.Id, new ActionWindupComponent());

            ScheduledCallSystem.Schedule(ecs, ScheduledCallType.IceNovaResolve, windupTicks, input.CastingEntityId);

            ecs.FlagEvents.Add(new AttackWindupBeganEvent { EntityId = input.CastingEntityId });
        },
    };

    // The actual Ice Nova effect — deferred out of BuildIceNovaAbility's ExecuteInstant so
    // it can be scheduled to run once the cast-time windup above ends, via
    // ScheduledCallSystem (registered against ScheduledCallType.IceNovaResolve in the
    // static constructor above). call.Param0 is the caster's entity id, captured at cast
    // time by ScheduledCallSystem.Schedule's call above — everything else (the caster's
    // current position/owner/range) is re-read live here, at resolve time, rather than also
    // captured at cast time, so the nova is centered on where the caster actually is once it
    // goes off rather than where it stood when the cast began.
    //
    // Freezing (creating the Frozen modifier) is harmless to duplicate across the
    // predicting client and the server, same reasoning as RingOfProjectilesAbility — it
    // acts through ModifierComponent.TargetEntityId, not the modifier entity's own id.
    // Removing the target's Chilled modifier is different: that's a genuine deletion, so
    // (mirroring ModifierSystem's own natural-expiry deletion) it's gated server-only —
    // predicted-only deletion would desync a client from the server's authoritative entity
    // set. The client's own Chilled icon just lingers one round-trip until the server's
    // deletion delta lands, same latency every other server-only deletion in this codebase
    // already has.
    private static void ResolveIceNova(ECS ecs, ScheduledCallComponent call)
    {
        ulong casterId = call.Param0;

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (posStore == null || troopStore == null || healthStore == null) return;
        if (!posStore.HasComponent(casterId) || !troopStore.HasComponent(casterId)) return;

        PositionComponent casterPos = posStore.GetComponent(casterId);
        ushort casterOwnerId = troopStore.GetComponent(casterId).OwnerPlayerId;
        float radius = StatsQuery.GetRange(ecs, casterId, IceNovaFallbackRange) * IceNovaRangeMultiplier;
        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;

        _queryBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(casterPos.X, casterPos.Y, radius, _queryBuffer);

        foreach (ulong targetId in _queryBuffer)
        {
            if (targetId == casterId) continue;
            if (!troopStore.HasComponent(targetId)) continue;
            if (troopStore.GetComponent(targetId).OwnerPlayerId == casterOwnerId) continue;
            if (!healthStore.HasComponent(targetId)) continue;
            if (!ActivationQuery.IsActivated(ecs, targetId)) continue;
            if (!ProjectileOnHitSystem.HasActiveChilledModifier(ecs, targetId)) continue;

            if (isServer)
                ProjectileOnHitSystem.RemoveActiveChilledModifiers(ecs, targetId);

            EntityHandle modifier = ecs.CreateEntity();
            ecs.AddComponent(modifier.Id, new ModifierComponent
            {
                TargetEntityId = targetId,
                TicksRemaining = TickManager.SecondsToTicks(IceNovaFreezeDurationSeconds),
                ModifierID     = ModifierID.Frozen,
            });
            ecs.AddComponent(modifier.Id, new StunnedComponent());
            ecs.AddComponent(modifier.Id, new RenderableModifierComponent
            {
                Type = RenderableModifierType.Frozen,
            });
        }
    }
}
