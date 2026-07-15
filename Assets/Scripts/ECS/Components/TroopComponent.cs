public struct TroopComponent : IComponent
{
    public const ushort NEUTRAL_OWNER_PLAYER_ID = ushort.MaxValue;
    public ushort OwnerPlayerId;

    public bool IsDead;

    // Whether this is a real, mobile player-controlled troop unit — as opposed to an
    // entity that only carries TroopComponent for its OwnerPlayerId (buildings, neutral
    // resource nodes like trees/rocks/ore, the AoeSpellCard's damage-aura entity). Checked
    // by TroopRangeHelper so cards that allow playing near a friendly troop
    // (Card.AllowsFriendlyTroopRange) don't also count those non-troop entities.
    // The struct default is false — every real troop spawn site must set this explicitly,
    // same convention as BuildingComponent.CardPlayRangeMultiplier.
    public bool IsPhysicalTroop;
}
