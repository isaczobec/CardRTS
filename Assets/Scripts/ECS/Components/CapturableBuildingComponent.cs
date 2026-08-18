// Tags a "spawn crystal" objective building (see CapturableBuildingFeature/
// EntitySpawnAction.AddCapturableBuildingComponents) — one inner + one outer placed on every
// player's own spoke bridge to the mid island. Starts owned by HomePlayerId (the player
// whose bridge this is); destroying one instantly re-captures it for whoever landed the
// killing blow (see CapturableBuildingSystem), which is what actually lets a player use it as
// a friendly building (BuildingRangeHelper) to spawn troops near it.
//
// HomePlayerId/IsOuter are fixed at world-gen time and never change, independent of whoever
// CURRENTLY owns the crystal (TroopComponent.OwnerPlayerId, which does change on capture) —
// CapturableBuildingQuery keys almost every rule off this fixed "whose bridge is this"
// identity rather than off current ownership, since the whole point is to gate attacking a
// bridge (or the base behind it) on how much of THAT SPECIFIC bridge someone currently holds.
public struct CapturableBuildingComponent : IComponent
{
    // Which player's spoke bridge this crystal was placed on.
    public ushort HomePlayerId;

    // True for the crystal closer to the mid island (further from HomePlayerId's own base);
    // false for the one closer to HomePlayerId's own base.
    public bool IsOuter;
}
