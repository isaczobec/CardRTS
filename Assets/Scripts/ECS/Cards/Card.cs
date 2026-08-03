using UnityEngine;

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

    // Broad category this card belongs to — drives the card face's title color (Building =
    // burgundy, Spell = purple, Troop = dark blue — see CardGameObject.ResolveTitleColor)
    // and the card-type label shown on its face (see CardGameObject.BuildCard). Troop is
    // the default since most cards are one; override per building/spell card.
    public virtual CardCategory Category => CardCategory.Troop;

    // Key into CardColorRegistry for this card's BackgroundColor — defaults to ImageName
    // (one registry entry per distinct card image covers most cases); override if a card
    // needs a different lookup key instead (e.g. sharing a color entry with another card).
    public virtual string ColorRegistryId => ImageName;

    // Card-face shader background color (_BgColor on the card material — see
    // CardGameObject.BuildCard) — looked up from CardColorRegistry by ColorRegistryId rather
    // than a per-card C# override, so colors can be tuned/added in the Inspector without
    // touching code. Falls back to this neutral default if the registry has no entry for
    // ColorRegistryId (or the registry doesn't exist in the scene at all).
    public virtual Color BackgroundColor
    {
        get
        {
            if (CardColorRegistry.instance != null && CardColorRegistry.instance.TryGetBG(ColorRegistryId, out Color color))
                return color;
            return new Color(0.02122641f, 0.1011946f, 0.5f);
        }
    }

    // Stats/cost panel backing color (_TextBgColor) — always a darker shade of
    // BackgroundColor itself (explicit design ask: the panel should read as recessed into
    // the card face, not as an independently chosen color), so this is deliberately NOT
    // overridden per card — override BackgroundColor instead and this follows automatically.
    public virtual Color TextBackgroundColor
    {
        get
        {
            if (CardColorRegistry.instance != null && CardColorRegistry.instance.TryGetBG(ColorRegistryId, out Color color))
                return color;
            return new Color(0.08018869f, 0.1100438f, 1.0f);
        }
    }

    // Border accent color (_EdgeColor) — a neutral, desaturated (gray/off-gray) tone derived
    // from BackgroundColor's own brightness rather than a saturated per-card theme color
    // (explicit design ask), so — like TextBackgroundColor — this is deliberately NOT
    // overridden per card.
    public virtual Color EdgeColor
    {
        get
        {
            if (CardColorRegistry.instance != null && CardColorRegistry.instance.TryGetBG(ColorRegistryId, out Color color))
                return color;
            return new Color(0.5f, 0.5f, 0.5f);
        }
    }

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

    // Whether playing this card puts at least one permanent, physical troop into play — the
    // card-level counterpart to ResourceValueComponent.CanCollectResources, which is the
    // same "IsPhysicalTroop with no LifetimeComponent" check but per spawned ENTITY rather
    // than per card definition (see that field's own doc comment for why it matters: a
    // permanent troop could eventually go kill a map resource node and earn its owner
    // resources, a short-lived one can't be relied on to). Read by
    // ResourceCollectorTrickleSystem to find a card in a resource-starved player's hand/deck
    // worth boosting them toward. Defaults to true for any ordinary Troop-category card;
    // Building/Spell cards never spawn a troop at all, so the default already excludes them
    // via Category — override to false on the handful of Troop-category cards whose ENTIRE
    // spawn is lifetime-limited (EphemeralSkeletonsCard, HealerGuardianCard).
    public virtual bool CanCollectResources => Category == CardCategory.Troop;

    // AbilityManager ability id(s) this card's OnPlayed equips on the troop it spawns, in the
    // order they should first appear on the player-wide ability bar (see AbilityBarComponent/
    // AbilityBarHelper.RegisterPurchasedAbilities, called from BuyCardSystem on a successful
    // purchase) — static declarative data, not derived from OnPlayed itself, since resolving
    // it would otherwise require actually spawning a throwaway entity. Empty for any card that
    // grants no ability (the overwhelming majority); override on the handful that equip an
    // AbilityComponent.
    public virtual int[] GrantedAbilityIds => System.Array.Empty<int>();
}
