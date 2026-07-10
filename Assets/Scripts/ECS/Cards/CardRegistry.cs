using System.Collections.Generic;

// One Card instance per CardType, shared across the whole game — see Card for why cards
// are stateless singletons rather than per-entity objects.
public static class CardRegistry
{
    private static readonly Dictionary<CardType, Card> _cards = new Dictionary<CardType, Card>
    {
        { CardType.BasicMeleeTroop, new BasicMeleeTroopCard() },
        { CardType.BasicRangedTroop, new BasicRangedTroopCard() },
        { CardType.Building, new BuildingCard() },
    };

    public static bool TryGet(CardType type, out Card card) => _cards.TryGetValue(type, out card);
}
