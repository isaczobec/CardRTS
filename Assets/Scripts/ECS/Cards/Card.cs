// One instance per distinct card definition in the game — not one per card entity. Cards
// are stateless: all per-instance state (whose deck/hand it's in, etc.) lives on the ECS
// side in CardComponent, and whatever the card's OnPlayed creates. See CardRegistry for
// the CardType -> Card lookup a card-kind's play system uses to dispatch a played card.
//
// Card itself only holds what every card kind needs regardless of how it's played
// (metadata, cost, whether/how far it's restricted to friendly-building range). How a
// card is actually played — one ground point, several points, an existing entity, etc. —
// is a per-kind concern: each kind gets its own Card subclass (see SpawnAtPointCard for
// the current "spawn one entity at one point" kind) with its own OnPlayed-equivalent
// signature, its own InputBase subtype, and its own play system (see
// SpawnAtPointCardPlaySystem), all funneling through the same InputBuffer/lockstep
// pipeline. A new kind needs: a new InputBase subtype (registered in
// TickManager._inputTypeRegistry), a new Card subclass declaring that kind's OnPlayed
// signature, a new play system mirroring SpawnAtPointCardPlaySystem's validation chain
// (ownership/location/cost/range) but dispatching through the new signature, and
// (client-side) new CardHandRenderer input-capture logic — today it only knows how to
// resolve a single ground point per click/drag.
public abstract class Card
{
    public abstract CardType Type { get; }

    public abstract string Title { get; }

    // Key into ImageRegistry for this card's artwork.
    public abstract string ImageName { get; }

    public abstract string Description { get; }

    // Stats to show on the card face (CardGameObject.BuildCard). Fields that don't apply
    // to this card (e.g. Speed on a building) should be set to StatsComponent.STAT_NA so
    // the UI hides that row instead of showing "0".
    public abstract StatsComponent DefaultStats { get; }

    // Resource price to play this card, checked before OnPlayed runs and deducted via
    // ResourceHelper.Spend. A field of 0 means the card doesn't cost that resource at all
    // (and its row is hidden on the card face).
    public abstract ResourceCost Cost { get; }

    // Persistent gold price to unlock/buy this card via the card shop (see ShopUIManager) —
    // distinct from Cost, which is what it costs to PLAY the card once it's already in a
    // player's deck/hand. Defaults to 0 (not yet priced); override per card to set a real
    // shop price.
    public virtual int ShopGoldCost => 0;

    // Base max distance (world/tile units) from a friendly building this card may be
    // played at — only consulted when RequiresFriendlyBuildingRange() is true. Each
    // candidate building can modify its own effective range via
    // BuildingComponent.CardPlayRangeMultiplier (applied first) and CardPlayRangeBonus —
    // the card is playable if it's within range of at least one friendly building.
    public abstract float MaxDistanceFromFriendlyBuilding { get; }

    // Whether this card is restricted to playing within MaxDistanceFromFriendlyBuilding of
    // a friendly building at all (see BuildingRangeHelper.IsWithinRangeOfFriendlyBuilding).
    // Defaults to true (today's behavior for every existing card); override to return
    // false for cards with unlimited range — e.g. Clash-Royale-style spells playable
    // anywhere on the map.
    public virtual bool RequiresFriendlyBuildingRange() => true;

    // Base max distance (world/tile units) from a friendly *physical* troop
    // (TroopComponent.IsPhysicalTroop) this card may ALSO be played at, only consulted
    // when AllowsFriendlyTroopRange() is true. Unlike MaxDistanceFromFriendlyBuilding,
    // there's no per-troop multiplier/bonus equivalent to BuildingComponent — this value
    // applies directly (see TroopRangeHelper.IsWithinRangeOfFriendlyTroop).
    public virtual float MaxDistanceFromFriendlyTroop => 0f;

    // Whether this card may be played within MaxDistanceFromFriendlyTroop of a friendly
    // physical troop, as an alternative to (not a replacement for) the
    // RequiresFriendlyBuildingRange check — the card is playable if it's in range of
    // EITHER a friendly building OR (when this returns true) a friendly troop. Defaults to
    // false — most cards are building-range-only.
    public virtual bool AllowsFriendlyTroopRange() => false;
}
