using System.Collections.Generic;

// Dev/cheat command: gives every player a chosen amount of one or more resources at once.
// Mutates PlayerResourcesComponent directly rather than going through a ResourcesAdded
// request (see ResourceHelper.Spend's own identical "mutate directly, mark dirty, raise one
// ResourcesChangedEvent" shape, just adding instead of subtracting) — a cheat command should
// hand over exactly the amount typed, not have it scaled down by ResourceGainDebuffSystem or
// scaled up by ComebackResourceBoostSystem/ResourceDropBoostSystem the way a normal gain
// would be.
public static class GiveResourcesCommand
{
    private static readonly string[] ResourceNames = { "wood", "stone", "metal", "gems", "soulstones", "gold" };

    public static void Register()
    {
        DevConsole.RegisterCommand(
            "give-resources",
            "Gives every player the specified amount of one or more resources. Usage: " +
            "give-resources [wood=N] [stone=N] [metal=N] [gems=N] [soulstones=N] [gold=N] [all=N] " +
            "— all=N gives N of every resource type; an individual type=N overrides that type's " +
            "amount on top of all=N (e.g. 'give-resources all=500 gold=2000' gives 500 of " +
            "everything except gold, which gets 2000). Server/standalone only.",
            Execute);
    }

    private static DevCommandResult Execute(DevCommandInfo info)
    {
        if (TickManager.instance == null || TickManager.instance.ECS == null)
            return DevCommandResult.Error("No active game.");

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer)
            return DevCommandResult.Error("Only the server/host can give resources.");

        Dictionary<string, float> amounts = new Dictionary<string, float>();

        if (info.keyWordArgs.TryGetValue("all", out string allStr))
        {
            if (!float.TryParse(allStr, out float allAmount) || allAmount <= 0f)
                return DevCommandResult.Error("Invalid amount for 'all' — must be a positive number.");
            foreach (string name in ResourceNames)
                amounts[name] = allAmount;
        }

        foreach (string name in ResourceNames)
        {
            if (!info.keyWordArgs.TryGetValue(name, out string valueStr)) continue;
            if (!float.TryParse(valueStr, out float value) || value <= 0f)
                return DevCommandResult.Error($"Invalid amount for '{name}' — must be a positive number.");
            amounts[name] = value;
        }

        if (amounts.Count == 0)
            return DevCommandResult.Error("Nothing to give — provide at least one of wood=/stone=/metal=/gems=/soulstones=/gold=/all=.");

        ECS ecs = TickManager.instance.ECS;
        ComponentStore<PlayerResourcesComponent> resourceStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null)
            return DevCommandResult.Error("No PlayerResourcesComponent store found.");

        int playerCount = 0;
        resourceStore.ForEach((ulong entityId) =>
        {
            playerCount++;
            GiveToPlayer(ecs, resourceStore, entityId, amounts);
        });

        if (playerCount == 0)
            return DevCommandResult.Error("No players found.");

        List<string> parts = new List<string>();
        foreach (KeyValuePair<string, float> kvp in amounts)
            parts.Add($"{kvp.Key}={kvp.Value:0.#}");

        return DevCommandResult.Success($"Gave resources to {playerCount} player(s): {string.Join(", ", parts)}.");
    }

    private static void GiveToPlayer(ECS ecs, ComponentStore<PlayerResourcesComponent> resourceStore, ulong entityId, Dictionary<string, float> amounts)
    {
        ref PlayerResourcesComponent resources = ref resourceStore.GetComponent(entityId);

        ResourcesChangedEvent changed = new ResourcesChangedEvent
        {
            EntityId = entityId,
            ClientId = ResourceHelper.GetOwnerPlayerId(ecs, entityId),
        };

        if (amounts.TryGetValue("wood", out float wood))             { resources.Wood       += wood;  changed.WoodDelta       = wood; }
        if (amounts.TryGetValue("stone", out float stone))           { resources.Stone      += stone; changed.StoneDelta      = stone; }
        if (amounts.TryGetValue("metal", out float metal))           { resources.Metal      += metal; changed.MetalDelta      = metal; }
        if (amounts.TryGetValue("gems", out float gems))             { resources.Gems       += gems;  changed.GemsDelta       = gems; }
        if (amounts.TryGetValue("soulstones", out float soulstones)) { resources.Soulstones += soulstones; changed.SoulstonesDelta = soulstones; }
        if (amounts.TryGetValue("gold", out float gold))             { resources.Gold       += gold;  changed.GoldDelta       = gold; }

        ecs.Delta.MarkComponentDirty(entityId, typeof(PlayerResourcesComponent));
        ecs.FlagEvents.Add(changed);
    }
}
