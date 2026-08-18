using System.Collections.Generic;
using UnityEngine;

// Combat-range rules for CapturableBuildingComponent buildings (see CapturableBuildingSystem,
// the only caller) — "adjacent" is always in terms of the coarse island/bridge region graph
// (see IslandGraphBuilder/NavMeshHandler.GetAdjacentIslandRegions), i.e. one bridge hop away,
// the same graph Pathfinding's own hierarchical search uses.
public static class CapturableBuildingQuery
{
    // 50% damage reduction once a player is allowed to attack a building at all but hasn't
    // reached it yet (see GetDamageMultiplier) — explicit design ask.
    private const float NonAdjacentDamageMultiplier = 0.5f;

    // 1f = full damage, NonAdjacentDamageMultiplier = half damage, 0f = fully blocked.
    //
    // - A building directly adjacent to the attacker's OWN base always takes full damage —
    //   explicit design ask ("the buildings closest to the player's base should never have
    //   this damage reduction"), otherwise nobody could ever start capturing anything.
    // - Otherwise, the attacker must already OWN some other capturable building adjacent to
    //   this one to be allowed to damage it at all (0% multiplier — fully blocked — if not:
    //   "so that a player has to 'expand' from their base"), and even then only at half
    //   damage, since it still isn't adjacent to their own base — UNLESS the target is itself
    //   a mid-bridge building (targetIsMidBridge) and the attacker already owns ANY OTHER
    //   mid-bridge building (regardless of island-graph adjacency to this specific one) —
    //   explicit design ask: "if I own one of the mid bridge conquerable buildings, I should
    //   be able to attack all other mid bridge conquerable buildings", so contesting the
    //   cluster around the shared mid island isn't gated by which single spoke a player
    //   originally expanded up. That first mid-bridge building still has to be captured via
    //   the normal adjacency expansion above — this only loosens mid-bridge-to-mid-bridge.
    public static float GetDamageMultiplier(ECS ecs, ushort attackerPlayerId, int targetIslandRegionId, bool targetIsMidBridge)
    {
        NavMeshHandler handler = NavMeshHandler.instance;
        // No region graph for this world (e.g. a legacy/non-island map) — fail open rather
        // than making every capturable building unkillable by accident.
        if (handler == null || targetIslandRegionId < 0) return 1f;

        List<int> adjacentRegions = new List<int>(handler.GetAdjacentIslandRegions(targetIslandRegionId));

        int attackerBaseRegionId = GetPlayerBaseRegionId(ecs, handler, attackerPlayerId);
        if (attackerBaseRegionId >= 0 && adjacentRegions.Contains(attackerBaseRegionId))
            return 1f;

        bool unlocked = OwnsAdjacentCapturableBuilding(ecs, attackerPlayerId, adjacentRegions)
            || (targetIsMidBridge && OwnsAnyMidBridgeCapturableBuilding(ecs, attackerPlayerId));

        return unlocked ? NonAdjacentDamageMultiplier : 0f;
    }

    private static int GetPlayerBaseRegionId(ECS ecs, NavMeshHandler handler, ushort playerId)
    {
        if (!PlayerBaseQuery.TryFindPosition(ecs, playerId, out float x, out float y)) return -1;
        return handler.GetTileRegionId((ushort)Mathf.FloorToInt(x), (ushort)Mathf.FloorToInt(y));
    }

    private static bool OwnsAdjacentCapturableBuilding(ECS ecs, ushort attackerPlayerId, List<int> adjacentRegions)
    {
        if (adjacentRegions.Count == 0) return false;

        ComponentStore<CapturableBuildingComponent> capturableStore = ecs.GetComponentStore<CapturableBuildingComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (capturableStore == null || troopStore == null) return false;

        bool owns = false;
        capturableStore.ForEach((ulong id) =>
        {
            if (owns) return;
            if (!adjacentRegions.Contains(capturableStore.GetComponent(id).IslandRegionId)) return;
            if (!troopStore.HasComponent(id)) return;
            if (troopStore.GetComponent(id).OwnerPlayerId == attackerPlayerId) owns = true;
        });
        return owns;
    }

    // Ignores island-graph adjacency entirely — any mid-bridge building the attacker owns,
    // anywhere on the map, satisfies this. See GetDamageMultiplier's own comment.
    private static bool OwnsAnyMidBridgeCapturableBuilding(ECS ecs, ushort attackerPlayerId)
    {
        ComponentStore<CapturableBuildingComponent> capturableStore = ecs.GetComponentStore<CapturableBuildingComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (capturableStore == null || troopStore == null) return false;

        bool owns = false;
        capturableStore.ForEach((ulong id) =>
        {
            if (owns) return;
            if (!capturableStore.GetComponent(id).IsMidBridge) return;
            if (!troopStore.HasComponent(id)) return;
            if (troopStore.GetComponent(id).OwnerPlayerId == attackerPlayerId) owns = true;
        });
        return owns;
    }
}
