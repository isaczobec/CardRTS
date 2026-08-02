using System;

// One instance per distinct upgrade definition, shared across the whole game — mirrors
// Card's own "stateless singleton per Type" shape (see Card.cs's doc comment for why: all
// per-purchase state lives on the ECS side, on UpgradeComponent entities, not on this class).
//
// Upgrades are bought in the shop (see BuyUpgradeSystem/BuyUpgradeInput) and attached to one
// specific card already in the buyer's deck — as a new UpgradeComponent entity targeting it
// (see UpgradeComponent's doc comment for why a separate entity per upgrade rather than a
// component slot on the card) — rather than played themselves, so, unlike Card, an upgrade
// doesn't need its own Cost/range rules, just a shop price and its actual effect.
public abstract class CardUpgrade
{
    public abstract UpgradeType Type { get; }

    public abstract string Title { get; }

    public abstract string Description { get; }

    // Key into ImageRegistry for this upgrade's artwork — mirrors Card.ImageName.
    public abstract string ImageName { get; }

    // Broad category this upgrade belongs to — drives UpgradeShopUIManager's Offense/
    // Defense/Utility filter buttons, mirrors Card.Category's role for the card shop's own
    // Troop/Building/Spell filter. Offense is the default (most upgrades are); override per
    // upgrade.
    public virtual UpgradeCategory Category => UpgradeCategory.Offense;

    // Persistent gold price to buy this upgrade — same shape as Card.ShopGoldCost.
    public virtual int ShopGoldCost => 0;

    // Maximum number of times this upgrade may be equipped on the SAME card at once —
    // checked by BuyUpgradeSystem before a purchase is allowed (see
    // UpgradeQuery.CountUpgradesOfType). int.MaxValue means no limit. Defaults to
    // int.MaxValue — every existing upgrade unless overridden (e.g. FocusFireUpgrade caps
    // at 1, GiantsbaneUpgrade at 2).
    public virtual int MaxStackCount => int.MaxValue;

    // Runs server-only, given the id of the entity a SpawnAtPointCard-kind upgraded card
    // just spawned and the ECS it spawned into — see SpawnAtPointCardPlaySystem, which calls
    // this right after the card's own OnPlayed returns that entity's id, ONLY when the
    // played card resolves to a SpawnAtPointCard. SpawnAtPointCard is the only card kind
    // that spawns exactly one entity per play (a MultiPointCard/TargetEntityCard play has no
    // single well-defined "the entity this play created" to hand an upgrade), which is what
    // this needs to act on — e.g. boosting a stat, adding a bonus component. A card upgraded
    // with one of these that turns out to be some other kind just never has this called.
    //
    // Left null (the default) for an upgrade that doesn't need a server-side hook at all —
    // the caller null-checks before invoking, so this is a genuine opt-in, not a required
    // override.
    public virtual Action<ulong, ECS> OnSpawnAtPointCardPlayed => null;
}
