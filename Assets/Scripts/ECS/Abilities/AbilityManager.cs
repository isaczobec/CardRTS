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

    // Target location, winds up then fires one of the caster's own pooled skillshot
    // projectiles (a hook) straight at the point — same casting shape as SkillshotAbilityId
    // (ActionWindupComponent + FireProjectileOnExpireComponent) — but on hit, yanks the
    // struck enemy troop via DisplacementSystem to just behind the caster's current position
    // (ProjectileOnHitEffectType.Hook — see ProjectileOnHitSystem.ApplyHook) instead of just
    // dealing damage. See PirateCard, the only troop that currently equips this.
    public const int HookAbilityId = 9;

    // Target entity (enemy/neutral only), winds up for KineticPullWindupSeconds
    // (ActionWindupComponent, same shape as GroundSlamAbilityId) then pulls the target
    // toward the caster's own position via DisplacementSystem — the exact same primitive
    // GroundSlamAbilityId's push uses, just with the direction vector flipped (toward the
    // caster instead of away from it). See KineticKnightCard, the only troop that currently
    // equips this.
    public const int KineticPullAbilityId = 10;

    // Instant, self-target — grants the caster a BarrierComponent-carrying modifier (same
    // absorption-shield primitive BarrierCard grants a target — see BarrierSystem) for
    // KineticShieldDurationSeconds. See KineticKnightCard, the only troop that currently
    // equips this.
    public const int KineticShieldAbilityId = 11;

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
    // Fallback radius (see Ability.CasterSelectionRadius) used only when no Ranger is
    // currently selected — PrioritizeSelectedTroops is on for this ability (explicit design
    // ask: "select your Rangers, aim, volley"), so in the common case every selected Ranger
    // casts regardless of this radius. Must comfortably exceed SkillshotAbilityRange itself —
    // otherwise aiming near the edge of the shot's actual range (a legal cast point) would
    // put the cursor further from the caster than this radius allows, and no Ranger would
    // ever resolve as a caster there at all.
    private const float SkillshotCasterSelectionRadius = 100f;
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
    // See Ability.CasterSelectionRadius — an instant, self-centered nova; a single closest
    // Ice Man triggering it (not the whole roster at once) is the intended AOE-clear payoff.
    // Explicit design ask: this should comfortably exceed IceNovaRange itself, the same as
    // every other ability's own CasterSelectionRadius — otherwise the preview's own range
    // circle (drawn at IceNovaRange around the caster) could extend past the radius the cursor
    // is even allowed to select that caster from, which reads as backwards.
    private const float IceNovaCasterSelectionRadius = 55f;

    private const float PushRange = 18f;
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
    // Slow, deliberate melee tank — a tight-ish caster-selection radius (see
    // Ability.CasterSelectionRadius) keeps this a single surgical push, not something that
    // fires from a Stone Construct clear across the screen. Still comfortably exceeds
    // GroundSlamRange itself, matching every other ability's own CasterSelectionRadius.
    private const float GroundSlamCasterSelectionRadius = 15f;

    // How long Shadow Cloak's untargetability lasts — explicit design ask (StalkerCard).
    private const float ShadowCloakDurationSeconds = 8f;

    // The cloak breaks early (ending it immediately, instead of running out
    // ShadowCloakDurationSeconds) once its target has taken this many separate damage
    // instances while cloaked — see ShadowCloakSystem's own DamageRequest.SubscribeExecuted
    // handler. Explicit design ask (StalkerCard).
    private const int ShadowCloakMaxDamageInstancesBeforeBreak = 2;
    // See Ability.CasterSelectionRadius — a self-buff escape/ambush tool, meant to trigger
    // one nearby Stalker at a time, not the whole roster. Comfortably exceeds
    // ShadowCloakCastIndicatorRadius below, same as every other ability's own
    // CasterSelectionRadius vs. its own Range. 3.5x the original 12 — explicit design ask
    // (the cursor shouldn't have to be nearly on top of the Stalker to select it).
    private const float ShadowCloakCasterSelectionRadius = 42f;
    // Shadow Cloak is Instant/self-targeted — it has no real "cast range" to speak of, so
    // this isn't a gameplay distance at all, purely a small cosmetic ShowRangeCircle (see
    // BuildShadowCloakAbility) drawn around whichever Stalker currently resolves as the
    // caster while the hotkey is held, so the player gets a clear "this one will cloak if I
    // let go" cue — explicit design ask.
    private const float ShadowCloakCastIndicatorRadius = 3f;

    // Not used for any cast validation (the shot always travels its own pool's Range — see
    // ProjectilePool.AimSkillshot), only so AbilityIndicatorManager can preview roughly how
    // far it reaches; kept in step with PirateCard.Range, the only troop that currently
    // equips this, same limitation SkillshotAbilityRange's own comment describes.
    private const float HookAbilityRange = 50f;
    // Mirrors BuildSkillshotAbility's own SkillshotWindupSeconds — an ActionWindupComponent
    // blocks the caster's own CanMove/CanPerform for the duration.
    private const float HookWindupSeconds = 0.4f;
    // See Ability.CasterSelectionRadius — a hard single-target pull; multi-hooking the same
    // fight would stack CC too strongly, so this stays a single closest-Pirate cast. Must
    // comfortably exceed HookAbilityRange itself, same as every other ability's own
    // CasterSelectionRadius.
    private const float HookCasterSelectionRadius = 65f;

    // How far a target may be from the caster to be a legal Kinetic Pull cast — also used
    // (alongside KineticPullSpeed/KineticPullDurationSeconds below) so a max-range target
    // ends up pulled almost exactly to the caster's own position rather than under- or
    // over-shooting it.
    private const float KineticPullRange = 25f;
    // Mirrors GroundSlamAbilityId's own GroundSlamWindupSeconds — "a short windup" per the
    // explicit design ask.
    private const float KineticPullWindupSeconds = 0.6f;
    // World units/second — DisplacementSystem steps the target by this every tick for
    // KineticPullDurationSeconds, in the direction straight toward the caster's current
    // position at the moment the pull lands. KineticPullSpeed * KineticPullDurationSeconds
    // == KineticPullRange, so a target at the very edge of Range travels almost exactly far
    // enough to reach the caster (a closer target simply arrives sooner/passes nearer it —
    // same "constant vector, not a precisely-computed landing point" simplification
    // PushAbilityId/GroundSlamAbilityId already use).
    private const float KineticPullSpeed = 20f;
    private const float KineticPullDurationSeconds = KineticPullRange / KineticPullSpeed;
    // See Ability.CasterSelectionRadius — hard CC, same single-caster reasoning as
    // HookCasterSelectionRadius. Must comfortably exceed KineticPullRange itself, same as
    // every other ability's own CasterSelectionRadius.
    private const float KineticPullCasterSelectionRadius = 32f;

    private const float KineticShieldMaxHealth = 100f;
    private const float KineticShieldDurationSeconds = 7f;
    // See Ability.CasterSelectionRadius — kept consistent with this troop's own
    // KineticPullCasterSelectionRadius rather than opening this up to a squad-wide shield.
    // Comfortably exceeds KineticShieldCastIndicatorRadius below, same as every other
    // ability's own CasterSelectionRadius vs. its own Range. 3.5x the original 14 — explicit
    // design ask (the cursor shouldn't have to be nearly on top of the Kinetic Knight to
    // select it), matching ShadowCloakCasterSelectionRadius's own bump.
    private const float KineticShieldCasterSelectionRadius = 49f;
    // Kinetic Shield is Instant/self-targeted — it has no real "cast range" to speak of, so
    // this isn't a gameplay distance at all, purely a small cosmetic ShowRangeCircle (see
    // BuildKineticShieldAbility) drawn around whichever Kinetic Knight currently resolves as
    // the caster while the hotkey is held, so the player gets a clear "this one will shield if
    // I let go" cue — explicit design ask, mirrors ShadowCloakCastIndicatorRadius.
    private const float KineticShieldCastIndicatorRadius = 3f;

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
        { HookAbilityId, BuildHookAbility() },
        { KineticPullAbilityId, BuildKineticPullAbility() },
        { KineticShieldAbilityId, BuildKineticShieldAbility() },
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
        ScheduledCallSystem.RegisterCall(ScheduledCallType.KineticPullResolve, ResolveKineticPull);
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
            // Explicit design ask: select your Rangers, aim, and every selected one volleys
            // together — see Ability.PrioritizeSelectedTroops/MaxSimultaneousCasters.
            CasterSelectionRadius = SkillshotCasterSelectionRadius,
            MaxSimultaneousCasters = 0, // unlimited
            PrioritizeSelectedTroops = true,
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

            ecs.FlagEvents.Add(new AttackWindupBeganEvent { EntityId = input.CastingEntityId, X = firePosition.x, Y = firePosition.y });
        };

        return ability;
    }

    // Winds up for HookWindupSeconds (ActionWindupComponent — blocks the caster's own
    // CanMove/CanPerform for the duration), then fires one of the caster's own pooled
    // projectiles (FireProjectileOnExpireComponent — ProjectilePool.FireInDirection under the
    // hood) in a straight line toward the cast point, captured once at cast time so it still
    // fires exactly where aimed regardless of what happens during the windup. The firing
    // mechanism itself is IDENTICAL to BuildSkillshotAbility — the only difference is what
    // happens on hit, which lives entirely on the pool's own ProjectileOnHitComponent
    // (EffectType.Hook — see PirateCard, which sets that up) rather than here. Deterministic
    // and side-effect-free like BuildSkillshotAbility, so no isServer guard is needed either.
    private static Ability BuildHookAbility()
    {
        Ability ability = new Ability
        {
            Type = AbilityType.TargetLocation,
            Name = "Hook",
            Description = "Winds up briefly, then fires a hook straight toward the targeted point. An enemy troop it hits is yanked to just behind the Pirate.",
            Range = HookAbilityRange,
            ImageName = "Hook",
            ShowRangeCircle = true,
            ShowDirectionArrow = true,
            DirectionArrowAlwaysMaxRange = true,
            // Pirate has no separate auto-attack pool (it's a melee troop) — the hook is its
            // only pool, at the default index.
            ProjectileOwnerIndex = 0,
            CasterSelectionRadius = HookCasterSelectionRadius,
            MaxSimultaneousCasters = 1,
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
                TicksRemaining = Mathf.Max(1, TickManager.SecondsToTicks(HookWindupSeconds)),
            });
            ecs.AddComponent(modifier.Id, new ActionWindupComponent());
            ecs.AddComponent(modifier.Id, new FireProjectileOnExpireComponent
            {
                DirectionX = direction.x,
                DirectionY = direction.y,
                ProjectilePoolOwnerId = poolOwnerId,
                // The hook's pool owner (index 0) IS the Pirate itself, so without this its
                // travel distance would default to the Pirate's own melee Range stat (5) —
                // wildly short of what this ability's own Range (used for the cast-time
                // range-circle/arrow preview above) promises. Tying both to the SAME constant
                // (HookAbilityRange) guarantees the shot always travels exactly as far as the
                // indicator shows.
                RangeOverride = HookAbilityRange,
            });

            ecs.FlagEvents.Add(new AttackWindupBeganEvent { EntityId = input.CastingEntityId, X = firePosition.x, Y = firePosition.y });
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
        CasterSelectionRadius = GroundSlamCasterSelectionRadius,
        MaxSimultaneousCasters = 1,
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

            PositionQuery.TryGet(ecs, input.CastingEntityId, out float slamX, out float slamY);
            ecs.FlagEvents.Add(new AttackWindupBeganEvent { EntityId = input.CastingEntityId, X = slamX, Y = slamY });
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

    // Same windup-then-resolve shape as BuildGroundSlamAbility, but the direction ResolveKineticPull
    // computes is flipped (toward the caster, not away from it) — enemy/neutral-only per the
    // explicit design ask ("targets an enemy entity"), unlike GroundSlamAbilityId/PushAbilityId
    // which allow friendly targets too. Deterministic and side-effect-free itself (the windup
    // modifier and the scheduled call both act through TargetEntityId/ScheduledCall matching,
    // not the entities' own ids), so no isServer guard is needed here — same reasoning as
    // BuildGroundSlamAbility.
    private static Ability BuildKineticPullAbility() => new Ability
    {
        Type = AbilityType.TargetEntity,
        Name = "Kinetic Pull",
        Description = "Winds up briefly, then pulls a nearby enemy toward the Kinetic Knight.",
        Range = KineticPullRange,
        ImageName = "KineticPull",
        CanTargetFriendly = false,
        CanTargetEnemyOrNeutral = true,
        ShowRangeCircle = true,
        ShowTargetIndicator = true,
        CasterSelectionRadius = KineticPullCasterSelectionRadius,
        MaxSimultaneousCasters = 1,
        ExecuteOnEntity = (ecs, input) =>
        {
            ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
            if (posStore == null) return;
            if (!posStore.HasComponent(input.CastingEntityId) || !posStore.HasComponent(input.TargetEntityId)) return;

            int windupTicks = Mathf.Max(1, TickManager.SecondsToTicks(KineticPullWindupSeconds));

            EntityHandle modifier = ecs.CreateEntity();
            ecs.AddComponent(modifier.Id, new ModifierComponent
            {
                TargetEntityId = input.CastingEntityId,
                TicksRemaining = windupTicks,
            });
            ecs.AddComponent(modifier.Id, new ActionWindupComponent());

            // param0 = caster, param3 = the originally-targeted entity — both re-read live
            // at resolve time (ResolveKineticPull), not captured here, so the pull direction
            // reflects where they actually are once the windup ends rather than where they
            // stood when the cast began — mirrors ResolveGroundSlam exactly.
            ScheduledCallSystem.Schedule(ecs, ScheduledCallType.KineticPullResolve, windupTicks,
                input.CastingEntityId, param3: input.TargetEntityId);

            PositionQuery.TryGet(ecs, input.CastingEntityId, out float pullX, out float pullY);
            ecs.FlagEvents.Add(new AttackWindupBeganEvent { EntityId = input.CastingEntityId, X = pullX, Y = pullY });
        },
    };

    // The actual Kinetic Pull — deferred out of BuildKineticPullAbility's ExecuteOnEntity so
    // it can be scheduled to run once the windup above ends, via ScheduledCallSystem
    // (registered against ScheduledCallType.KineticPullResolve in the static constructor
    // above). Mirrors ResolveGroundSlam exactly, just toward the caster instead of away from
    // it (see BuildPushAbility for the "away" version this is the mirror image of).
    private static void ResolveKineticPull(ECS ecs, ScheduledCallComponent call)
    {
        ulong casterId = call.Param0;
        ulong targetId = call.Param3;

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (posStore == null) return;
        if (!posStore.HasComponent(casterId) || !posStore.HasComponent(targetId)) return;

        PositionComponent casterPos = posStore.GetComponent(casterId);
        PositionComponent targetPos = posStore.GetComponent(targetId);
        Vector2 direction = new Vector2(casterPos.X - targetPos.X, casterPos.Y - targetPos.Y);
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;

        DisplacementSystem.BeginDisplacement(ecs, targetId,
            direction.x * KineticPullSpeed, direction.y * KineticPullSpeed, TickManager.SecondsToTicks(KineticPullDurationSeconds));
    }

    // Instant, self-target — grants the caster its own BarrierComponent-carrying modifier
    // (see BarrierComponent/BarrierSystem — the exact same absorption-shield primitive
    // BarrierCard grants a target, just self-cast here instead of played from hand). No
    // ActivatableComponent/activation-delay on the modifier — unlike a card-played buff
    // (BarrierCard/SpeedBoostCard/...), an ability's own cast already gates when it takes
    // effect, mirrors BuildShadowCloakAbility's own self-buff shape exactly. Deterministic
    // and side-effect-free like that ability (acts through ModifierComponent.TargetEntityId,
    // not the modifier entity's own id), so no isServer guard is needed here either.
    private static Ability BuildKineticShieldAbility() => new Ability
    {
        Type = AbilityType.Instant,
        Name = "Kinetic Shield",
        Description = "Grants the Kinetic Knight a 100 HP shield that fully blocks incoming damage for 7 seconds.",
        ImageName = "KineticShield",
        // Purely cosmetic — see KineticShieldCastIndicatorRadius's own comment. Not a real
        // cast range (Instant/self-targeted, never consulted by AbilitySystem).
        Range = KineticShieldCastIndicatorRadius,
        ShowRangeCircle = true,
        CasterSelectionRadius = KineticShieldCasterSelectionRadius,
        MaxSimultaneousCasters = 1,
        ExecuteInstant = (ecs, input) =>
        {
            EntityHandle modifier = ecs.CreateEntity();
            ecs.AddComponent(modifier.Id, new ModifierComponent
            {
                TargetEntityId = input.CastingEntityId,
                TicksRemaining = TickManager.SecondsToTicks(KineticShieldDurationSeconds),
                ModifierID     = ModifierID.Barrier,
            });
            ecs.AddComponent(modifier.Id, new BarrierComponent
            {
                MaxHealth       = KineticShieldMaxHealth,
                HealthRemaining = KineticShieldMaxHealth,
                DamageMultiplier = 1f,
            });
            ecs.AddComponent(modifier.Id, new RenderableModifierComponent
            {
                Type = RenderableModifierType.Barrier,
            });
        },
    };

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
        // Purely cosmetic — see ShadowCloakCastIndicatorRadius's own comment. Not a real
        // cast range (Instant/self-targeted, never consulted by AbilitySystem).
        Range = ShadowCloakCastIndicatorRadius,
        ShowRangeCircle = true,
        CasterSelectionRadius = ShadowCloakCasterSelectionRadius,
        MaxSimultaneousCasters = 1,
        ExecuteInstant = (ecs, input) =>
        {
            EntityHandle modifier = ecs.CreateEntity();
            ecs.AddComponent(modifier.Id, new ModifierComponent
            {
                TargetEntityId = input.CastingEntityId,
                TicksRemaining = TickManager.SecondsToTicks(ShadowCloakDurationSeconds),
                ModifierID     = ModifierID.ShadowCloak,
            });
            ecs.AddComponent(modifier.Id, new ShadowCloakComponent
            {
                MaxDamageInstancesBeforeBreak = ShadowCloakMaxDamageInstancesBeforeBreak,
            });
            ecs.AddComponent(modifier.Id, new RenderableModifierComponent
            {
                Type = RenderableModifierType.ShadowCloak,
            });
            ecs.AddComponent(modifier.Id, new StatModifierComponent
            {
                SpeedRatioBonus = 0.9f,
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
        CasterSelectionRadius = IceNovaCasterSelectionRadius,
        MaxSimultaneousCasters = 1,
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

            PositionQuery.TryGet(ecs, input.CastingEntityId, out float novaX, out float novaY);
            ecs.FlagEvents.Add(new AttackWindupBeganEvent { EntityId = input.CastingEntityId, X = novaX, Y = novaY });
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
