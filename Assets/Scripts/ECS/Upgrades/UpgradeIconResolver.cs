using System.Collections.Generic;
using UnityEngine;

// Resolves the (Sprite, ShopGoldCost, Title, Description) tuples CardGameObject.
// SetUpgradeIcons needs for every upgrade equipped on a given card — shared by
// CardHandRenderer (hand cards) and UpgradeShopUIManager (deck-picker cards) so both build
// the icon list the same way, the same way artwork resolution (ImageRegistry.TryGet) is
// duplicated per call site elsewhere rather than centralized, except here there'd otherwise
// be two near-identical copies of the same UpgradeQuery walk.
public static class UpgradeIconResolver
{
    public static List<(Sprite icon, int shopGoldCost, string title, string description)> Resolve(ECS ecs, ulong cardEntityId)
    {
        var icons = new List<(Sprite icon, int shopGoldCost, string title, string description)>();

        UpgradeQuery.ForEachUpgradeOnCard(ecs, cardEntityId, upgrade =>
        {
            Sprite sprite = null;
            if (ImageRegistry.instance != null)
                ImageRegistry.instance.TryGet(upgrade.ImageName, out sprite);

            icons.Add((sprite, upgrade.ShopGoldCost, upgrade.Title, upgrade.Description));
        });

        return icons;
    }
}
