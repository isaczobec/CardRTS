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

    private const int RingProjectileCount = 8;
    private const float AoeSpellCloneRange = 8f;
    private const float SkillshotAbilityRange = 20f;
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

    private static readonly Dictionary<int, Ability> _abilities = new Dictionary<int, Ability>
    {
        { RingOfProjectilesAbilityId, BuildRingOfProjectilesAbility() },
        { AoeSpellCloneAbilityId, BuildAoeSpellCloneAbility() },
        { SkillshotAbilityId, BuildSkillshotAbility() },
    };

    public static bool TryGet(int abilityId, out Ability ability) => _abilities.TryGetValue(abilityId, out ability);

    private static Ability BuildRingOfProjectilesAbility() => new Ability
    {
        Type = AbilityType.Instant,
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

    // Fires one of the caster's own pooled projectiles (ProjectilePool.FireInDirection —
    // aims whatever kind the pool holds; a skillshot troop's pool is skillshot-typed, see
    // SkillshotRangedTroopCard) in a straight line toward the cast point. Deterministic and
    // side-effect-free like RingOfProjectilesAbility, so — unlike AoeSpellCloneAbility — no
    // isServer guard is needed; both server and predicting clients activating "the same"
    // pooled projectile is exactly how every other pooled shot in this codebase already works.
    private static Ability BuildSkillshotAbility() => new Ability
    {
        Type = AbilityType.TargetLocation,
        Range = SkillshotAbilityRange,
        ImageName = "SkillshotAbility",
        ShowRangeCircle = true,
        ShowDirectionArrow = true,
        // The shot always travels the full Range regardless of where within it you aim, so
        // the arrow should always read as the full range circle's radius, not shrink to
        // wherever the cursor happens to be.
        DirectionArrowAlwaysMaxRange = true,
        ExecuteAtLocation = (ecs, input) =>
        {
            ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
            if (posStore == null || !posStore.HasComponent(input.CastingEntityId)) return;

            PositionComponent casterPos = posStore.GetComponent(input.CastingEntityId);
            Vector2 firePosition = new Vector2(casterPos.X, casterPos.Y);
            Vector2 direction = new Vector2(input.X, input.Y) - firePosition;

            ProjectilePool.FireInDirection(ecs, input.CastingEntityId, direction, firePosition);
        },
    };
}
