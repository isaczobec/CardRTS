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

    private const int RingProjectileCount = 8;
    private const float AoeSpellCloneRange = 8f;

    private static readonly Dictionary<int, Ability> _abilities = new Dictionary<int, Ability>
    {
        { RingOfProjectilesAbilityId, BuildRingOfProjectilesAbility() },
        { AoeSpellCloneAbilityId, BuildAoeSpellCloneAbility() },
    };

    public static bool TryGet(int abilityId, out Ability ability) => _abilities.TryGetValue(abilityId, out ability);

    private static Ability BuildRingOfProjectilesAbility() => new Ability
    {
        Type = AbilityType.Instant,
        Range = 0f, // unused by an Instant ability
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
        },
    };

    private static Ability BuildAoeSpellCloneAbility() => new Ability
    {
        Type = AbilityType.TargetLocation,
        Range = AoeSpellCloneRange,
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
}
