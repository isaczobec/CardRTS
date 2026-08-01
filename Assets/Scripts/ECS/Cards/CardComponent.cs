public enum CardType : byte
{
    BasicMeleeTroop = 0,
    BasicRangedTroop = 1,
    Building = 2,
    AoeSpell = 3,
    SkillshotRangedTroop = 4,
    SpeedBoost = 5,
    Blink = 6,
    IronKnight = 7,
    IceMan = 8,
    FireMan = 9,
    GoblinSnatcher = 10,
    StoneConstruct = 11,
    Skeletons = 12,
    EphemeralSkeletons = 13,
    Stalker = 14,
    Barrier = 15,
    AoeRoot = 16,
    SantaClaus = 17,
    Cannon = 18,
    MissileSilo = 19,
    Pirate = 20,
    Sawmill = 21,
    Quarry = 22,
    Mine = 23,
    HealerGuardian = 24,
    ShadowAngel = 25,
    SleepingDraught = 26,
    Silence = 27,
    ConstructionWorker = 28,
    StrategyConsultant = 29,
    Heal = 30,
    BallisticMissile = 31,
    MassiveSleepingDraught = 32,
    Tornado = 33,
    Orcs = 34,
    KineticKnight = 35,
}

public enum CardLocation : byte
{
    Deck = 0,
    Hand = 1,
}

public struct CardComponent : IComponent
{
    public CardType Type;
    public CardLocation Location;

    // Whose deck/hand this card belongs to — see e.g. SpawnAtPointCardPlaySystem, which
    // rejects a play input for a card the requesting client doesn't own.
    public ushort OwnerPlayerId;

    // Entity ID of the next card behind this one in its owner's deck queue, or 0 if this
    // is currently the last (tail) card — or it isn't in the deck at all. Only meaningful
    // while Location == CardLocation.Deck. See PlayerDeckComponent / DeckHelper / DeckSystem.
    public ulong NextInDeckId;
}
