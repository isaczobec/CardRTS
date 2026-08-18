// Combat rules for "spawn crystal" CapturableBuildingComponent buildings and the player base
// behind them (see CapturableBuildingSystem, the only caller). Every rule below is keyed off
// CapturableBuildingComponent.HomePlayerId/IsOuter (fixed at world-gen time — whose bridge a
// crystal structurally belongs to, and where along it) rather than off current ownership,
// which is what lets "recapture your own bridge" and "invade someone else's" read as
// different, deliberately asymmetric rules instead of one generic adjacency check:
//
//  - Outer-protects-inner (foreign attackers only): once a player has qualified to attack a
//    bridge that isn't their own (see the cross-bridge gate below), that bridge's inner
//    crystal is untouchable while its current owner also owns that same bridge's outer one —
//    the outer crystal is always the front line for an outside invader.
//  - Cross-bridge gate: a player can't damage ANY crystal on a bridge that isn't their own
//    unless they currently own BOTH crystals on their own home bridge — "if a player has lost
//    a spawn crystal on their own bridge, they should not be able to attack the spawn
//    crystals of others before recapturing their own one."
//  - Home-bridge reclaim (a bridge's own home player attacking their own bridge): the
//    opposite order from the rule above — inner is always attackable, and outer only becomes
//    attackable once the home player already owns their own inner, mirroring "expand from
//    your base" ("they should have to conquer the one closest to their base before retaking
//    the one closest to mid").
//  - Base invulnerability: a player's base takes 0 damage while that player still owns AT
//    LEAST ONE of the two crystals on their own home bridge.
public static class CapturableBuildingQuery
{
    public static bool CanDamageCrystal(ECS ecs, ushort attackerPlayerId, CapturableBuildingComponent target)
    {
        if (attackerPlayerId == target.HomePlayerId)
        {
            // Reclaiming your own lost bridge: inner is always fair game; outer only opens up
            // once you already hold inner again.
            if (!target.IsOuter) return true;
            return OwnsCrystal(ecs, target.HomePlayerId, isOuter: false) == attackerPlayerId;
        }

        // Invading someone else's bridge: must fully hold your own first.
        if (OwnsCrystal(ecs, attackerPlayerId, isOuter: true) != attackerPlayerId) return false;
        if (OwnsCrystal(ecs, attackerPlayerId, isOuter: false) != attackerPlayerId) return false;

        if (target.IsOuter) return true;

        // Inner is protected only while its own current owner also holds this bridge's outer.
        ushort? innerOwner = OwnsCrystal(ecs, target.HomePlayerId, isOuter: false);
        ushort? outerOwner = OwnsCrystal(ecs, target.HomePlayerId, isOuter: true);
        return innerOwner == null || innerOwner != outerOwner;
    }

    // True if playerId currently owns at least one of the two crystals on their OWN home
    // bridge — see CapturableBuildingSystem's base-invulnerability rule.
    public static bool OwnsAnyOwnBridgeCrystal(ECS ecs, ushort playerId)
        => OwnsCrystal(ecs, playerId, isOuter: true) == playerId || OwnsCrystal(ecs, playerId, isOuter: false) == playerId;

    // Current owner of the (unique) inner/outer crystal on homePlayerId's own bridge, or null
    // if no such crystal exists (shouldn't normally happen — every player's spoke places
    // exactly one of each).
    private static ushort? OwnsCrystal(ECS ecs, ushort homePlayerId, bool isOuter)
    {
        ComponentStore<CapturableBuildingComponent> capturableStore = ecs.GetComponentStore<CapturableBuildingComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (capturableStore == null || troopStore == null) return null;

        ushort? found = null;
        capturableStore.ForEach((ulong id) =>
        {
            if (found != null) return;
            CapturableBuildingComponent c = capturableStore.GetComponent(id);
            if (c.HomePlayerId != homePlayerId || c.IsOuter != isOuter) return;
            if (!troopStore.HasComponent(id)) return;
            found = troopStore.GetComponent(id).OwnerPlayerId;
        });
        return found;
    }
}
