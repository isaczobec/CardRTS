// One instance of a purchased upgrade attached to TargetCardEntityId — a separate entity of
// its own, not a component slot on the card, so multiple upgrades (even multiple of the same
// Type) can be equipped onto the same card simultaneously. Mirrors ModifierComponent's own
// "separate entity per instance, not a slot on the target" shape (see its doc comment) for
// exactly the same reason.
//
// Spawned server-only by BuyUpgradeSystem once a purchase is confirmed — unlike
// ModifierComponent, this is never predicted client-side: there's no gameplay reason to show
// an upgrade's effect before the server confirms the purchase, and BuyUpgradeSystem's
// (predicted) resource deduction alone already gives immediate spend feedback.
public struct UpgradeComponent : IComponent
{
    public ulong TargetCardEntityId;
    public UpgradeType Type;
}
