using System;

// Finds every UpgradeComponent entity currently targeting a given card — see
// UpgradeComponent's own doc comment for why an upgrade is its own entity rather than a
// component slot on the card (multiple upgrades per card). No spatial/indexed lookup exists
// for this (unlike e.g. ChunkTracker for position) — a plain linear scan over every upgrade
// entity, which is fine for how many a deck's worth of cards would ever have equipped at once.
public static class UpgradeQuery
{
    public static void ForEachUpgradeOnCard(ECS ecs, ulong cardEntityId, Action<CardUpgrade> action)
    {
        ComponentStore<UpgradeComponent> upgradeStore = ecs.GetComponentStore<UpgradeComponent>();
        if (upgradeStore == null) return;

        upgradeStore.ForEach((ulong id) =>
        {
            UpgradeComponent upgrade = upgradeStore.GetComponent(id);
            if (upgrade.TargetCardEntityId != cardEntityId) return;

            if (UpgradeRegistry.TryGet(upgrade.Type, out CardUpgrade definition))
                action(definition);
        });
    }

    // How many UpgradeComponent entities of a specific type currently target a card — see
    // BuyUpgradeSystem, which rejects a purchase that would exceed CardUpgrade.MaxStackCount.
    public static int CountUpgradesOfType(ECS ecs, ulong cardEntityId, UpgradeType type)
    {
        ComponentStore<UpgradeComponent> upgradeStore = ecs.GetComponentStore<UpgradeComponent>();
        if (upgradeStore == null) return 0;

        int count = 0;
        upgradeStore.ForEach((ulong id) =>
        {
            UpgradeComponent upgrade = upgradeStore.GetComponent(id);
            if (upgrade.TargetCardEntityId != cardEntityId) return;
            if (upgrade.Type != type) return;
            count++;
        });
        return count;
    }
}
