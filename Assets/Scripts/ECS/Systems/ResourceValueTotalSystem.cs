using System.Collections.Generic;

// Server-only. Every PeriodTicks simulation ticks, sums every live ResourceValueComponent
// by OwnerPlayerId (battlefield entities plus every card/upgrade a player owns — see that
// component's own doc comment for exactly what carries one) into a single "unified value"
// number via ResourceConversionRates, and writes the result onto that player's own
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

            ref PlayerTotalResourceValueComponent totalComponent = ref totalStore.GetComponent(id);
            totalComponent.TotalValue = total;
            ecs.Delta.MarkComponentDirty(id, typeof(PlayerTotalResourceValueComponent));
        });
    }
}
