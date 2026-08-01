using System.Collections.Generic;

// Server-only. Every PeriodTicks simulation ticks, sums every live ResourceValueComponent
// by OwnerPlayerId (battlefield entities plus every card/upgrade a player owns — see that
// component's own doc comment for exactly what carries one), PLUS that player's own
// currently-held (unspent) PlayerResourcesComponent stock — a player sitting on a stockpile
// is just as "ahead" as one who already spent it on troops/buildings/cards, so leaving it
// out would undercount them — both converted into a single "unified value" number via
// ResourceConversionRates, and writes the result onto that player's own
// PlayerTotalResourceValueComponent.
//
// Purely a read-and-report step — it never mutates anything ResourceValueComponent-bearing,
// so replaying it an extra time during reconciliation is harmless; it always recomputes the
// same total for the same tick. Uses ecs.CurrentSimulationTick % PeriodTicks rather than a
// stored counter for exactly that reason — deterministic, and needs zero component state of
// its own to stay reconciliation-safe.
public static class ResourceValueTotalSystem
{
    public const int PeriodTicks = 100;

    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        if (ecs.CurrentSimulationTick % PeriodTicks != 0) return;

        ComponentStore<ResourceValueComponent> valueStore = ecs.GetComponentStore<ResourceValueComponent>();
        ComponentStore<PlayerComponent> playerStore = ecs.GetComponentStore<PlayerComponent>();
        ComponentStore<PlayerTotalResourceValueComponent> totalStore = ecs.GetComponentStore<PlayerTotalResourceValueComponent>();
        ComponentStore<PlayerResourcesComponent> resourcesStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (valueStore == null || playerStore == null || totalStore == null) return;

        Dictionary<ushort, float> totalsByPlayer = new Dictionary<ushort, float>();
        valueStore.ForEach((ulong id) =>
        {
            ResourceValueComponent value = valueStore.GetComponent(id);
            float unified =
                value.Wood       * ResourceConversionRates.Wood +
                value.Stone      * ResourceConversionRates.Stone +
                value.Metal      * ResourceConversionRates.Metal +
                value.Gems       * ResourceConversionRates.Gems +
                value.Soulstones * ResourceConversionRates.Soulstones +
                value.Gold       * ResourceConversionRates.Gold;

            totalsByPlayer.TryGetValue(value.OwnerPlayerId, out float existing);
            totalsByPlayer[value.OwnerPlayerId] = existing + unified;
        });

        playerStore.ForEach((ulong id) =>
        {
            if (!totalStore.HasComponent(id)) return;

            totalsByPlayer.TryGetValue(playerStore.GetComponent(id).PlayerId, out float total);

            // PlayerResourcesComponent lives on this same entity (see its own doc comment) —
            // currently-held resources count toward net worth exactly like invested ones do.
            if (resourcesStore != null && resourcesStore.HasComponent(id))
            {
                PlayerResourcesComponent held = resourcesStore.GetComponent(id);
                total +=
                    held.Wood       * ResourceConversionRates.Wood +
                    held.Stone      * ResourceConversionRates.Stone +
                    held.Metal      * ResourceConversionRates.Metal +
                    held.Gems       * ResourceConversionRates.Gems +
                    held.Soulstones * ResourceConversionRates.Soulstones +
                    held.Gold       * ResourceConversionRates.Gold;
            }

            ref PlayerTotalResourceValueComponent totalComponent = ref totalStore.GetComponent(id);
            totalComponent.TotalValue = total;
            ecs.Delta.MarkComponentDirty(id, typeof(PlayerTotalResourceValueComponent));
        });
    }
}
