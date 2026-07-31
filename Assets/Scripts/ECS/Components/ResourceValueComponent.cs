// Tags an entity as representing a specific amount of "invested" resource value for one
// player — read by ResourceValueTotalSystem to compute each player's total current value.
// Fields mirror ResourceCost/PlayerResourcesComponent 1:1.
//
// Attached to:
//  - the entity a played SpawnAtPointCard spawns (troop, building, ...), valued at that
//    card's own ResourceCost — see SpawnAtPointCardPlaySystem, which attaches this
//    automatically for any card that doesn't already tag its own spawn(s) itself. Cards
//    that spawn several entities from a single play (e.g. SkeletonsCard) instead tag each
//    one individually with an even split of the card's cost (see ResourceValueHelper.Split)
//    since the play system only ever sees the first spawned entity's id.
//  - troops spawned indirectly by another troop's own action rather than a card play (e.g.
//    SkeletonsCard's kill-triggered resurrection, SantaClausCard's hit-triggered
//    reinforcement) — valued case-by-case at the spawn site, see each one's own comment.
//  - card entities themselves (CardComponent, BuyCardSystem/DeckHelper) and upgrade
//    entities themselves (UpgradeComponent, BuyUpgradeSystem) — Gold = the shop price paid,
//    every other field 0.
//
// Naturally disappears when the entity dies/is deleted (ecs.DeleteEntity removes every
// component on it), so a dead troop correctly stops counting toward its owner's total
// without ResourceValueTotalSystem needing any death-handling logic of its own.
public struct ResourceValueComponent : IComponent
{
    public ushort OwnerPlayerId;
    public int Wood;
    public int Stone;
    public int Metal;
    public int Gems;
    public int Soulstones;
    public int Gold;
}
