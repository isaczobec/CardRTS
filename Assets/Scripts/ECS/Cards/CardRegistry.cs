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
        { CardType.AoeSpell, new AoeSpellCard() },
        { CardType.SkillshotRangedTroop, new SkillshotRangedTroopCard() },
        { CardType.SpeedBoost, new SpeedBoostCard() },
        { CardType.Blink, new BlinkCard() },
        { CardType.IronKnight, new IronKnightCard() },
        { CardType.IceMan, new IceManCard() },
        { CardType.FireMan, new FireManCard() },
        { CardType.GoblinSnatcher, new GoblinSnatcherCard() },
        { CardType.StoneConstruct, new StoneConstructCard() },
        { CardType.Skeletons, new SkeletonsCard() },
        { CardType.EphemeralSkeletons, new EphemeralSkeletonsCard() },
        { CardType.Stalker, new StalkerCard() },
        { CardType.Barrier, new BarrierCard() },
        { CardType.AoeRoot, new AoeRootCard() },
        { CardType.SantaClaus, new SantaClausCard() },
        { CardType.Cannon, new CannonCard() },
        { CardType.MissileSilo, new MissileSiloCard() },
        { CardType.Pirate, new PirateCard() },
        { CardType.Sawmill, new SawmillCard() },
        { CardType.Quarry, new QuarryCard() },
        { CardType.Mine, new MineCard() },
        { CardType.HealerGuardian, new HealerGuardianCard() },
        { CardType.ShadowAngel, new ShadowAngelCard() },
    };

    public static bool TryGet(CardType type, out Card card) => _cards.TryGetValue(type, out card);
}
