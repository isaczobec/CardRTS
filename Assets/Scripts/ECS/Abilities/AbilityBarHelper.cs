using System.Collections.Generic;

// Shared per-player ability-bar lookup/mutation, mirroring DeckHelper.FindPlayerDeckEntity's
// exact pattern for PlayerDeckComponent (and ResourceHelper.FindPlayerResourcesEntity's for
// PlayerResourcesComponent) — AbilityBarComponent lives on that very same player entity (see
// NetworkManager.SpawnPlayerEntity) but gets its own lookup here to keep each component's
// concern self-contained.
public static class AbilityBarHelper
{
    // True if abilityIds contains at least one ability not already on ownerPlayerId's bar, AND
    // there isn't enough empty room left for all of the new ones — i.e. buying a card that
    // grants abilityIds would either silently fail to fit everything (see
    // RegisterPurchasedAbilities' own log-and-skip fallback) or push the bar past
    // AbilityBarComponent.SlotCount. Checked by both BuyCardSystem (the authoritative reject)
    // and ShopUIManager (the unaffordable-overlay display) so a card that would overflow the
    // bar is rejected/dimmed consistently rather than only discovered after paying for it.
    // False (never blocks) if the player has no ability bar entity yet — nothing to overflow.
    public static bool WouldExceedCapacity(ECS ecs, ushort ownerPlayerId, int[] abilityIds)
    {
        if (abilityIds == null || abilityIds.Length == 0) return false;

        ulong barEntityId = FindPlayerAbilityBarEntity(ecs, ownerPlayerId);
        if (barEntityId == 0) return false;

        ComponentStore<AbilityBarComponent> barStore = ecs.GetComponentStore<AbilityBarComponent>();
        if (barStore == null || !barStore.HasComponent(barEntityId)) return false;

        AbilityBarComponent bar = barStore.GetComponent(barEntityId);

        HashSet<int> newDistinctIds = null;
        foreach (int abilityId in abilityIds)
        {
            if (abilityId == 0 || bar.Contains(abilityId)) continue;
            (newDistinctIds ??= new HashSet<int>()).Add(abilityId);
        }
        if (newDistinctIds == null) return false;

        int emptySlots = 0;
        for (int slot = 0; slot < AbilityBarComponent.SlotCount; slot++)
            if (bar.GetAbilityId(slot) == 0) emptySlots++;

        return newDistinctIds.Count > emptySlots;
    }

    // Finds the entity carrying ownerPlayerId's AbilityBarComponent, or 0 if none exists (e.g.
    // that player's entity hasn't been set up yet).
    public static ulong FindPlayerAbilityBarEntity(ECS ecs, ushort ownerPlayerId)
    {
        ComponentStore<PlayerComponent> playerStore = ecs.GetComponentStore<PlayerComponent>();
        ComponentStore<AbilityBarComponent> barStore = ecs.GetComponentStore<AbilityBarComponent>();
        if (playerStore == null || barStore == null) return 0;

        ulong found = 0;
        playerStore.ForEach((ulong id) =>
        {
            if (found != 0) return;
            if (!barStore.HasComponent(id)) return;
            if (playerStore.GetComponent(id).PlayerId == ownerPlayerId) found = id;
        });
        return found;
    }

    // Appends every not-yet-present id in abilityIds (in array order) to ownerPlayerId's bar,
    // capped at AbilityBarComponent.SlotCount — called from BuyCardSystem right after a
    // successful purchase, with Card.GrantedAbilityIds. abilityIds is static per-CardType data
    // (see Card.GrantedAbilityIds), so this is fully deterministic and safe to run unconditionally
    // on both a predicting client and the server, exactly like the resource deduction right
    // before it in BuyCardSystem — no isServer guard needed. No-ops if abilityIds is empty/null
    // or the player has no ability bar entity yet.
    public static void RegisterPurchasedAbilities(ECS ecs, ushort ownerPlayerId, int[] abilityIds)
    {
        if (abilityIds == null || abilityIds.Length == 0) return;

        ulong barEntityId = FindPlayerAbilityBarEntity(ecs, ownerPlayerId);
        if (barEntityId == 0) return;

        ComponentStore<AbilityBarComponent> barStore = ecs.GetComponentStore<AbilityBarComponent>();
        ref AbilityBarComponent bar = ref barStore.GetComponent(barEntityId);

        bool changed = false;
        foreach (int abilityId in abilityIds)
        {
            if (abilityId == 0) continue;

            if (!bar.TryAdd(abilityId, out bool added))
            {
                DebugLogger.LogWarning($"[AbilityBarHelper] Player {ownerPlayerId}'s ability bar is full ({AbilityBarComponent.SlotCount}/{AbilityBarComponent.SlotCount}) — ability {abilityId} not added.", "abilities");
                continue;
            }

            if (added) changed = true;
        }

        if (changed)
            ecs.Delta.MarkComponentDirty(barEntityId, typeof(AbilityBarComponent));
    }
}
