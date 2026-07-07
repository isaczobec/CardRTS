using System;
using System.Collections.Generic;

// Tracks which entities each friendly troop is currently targeting, built from
// SetTargetsInput each tick. Instance (not static) and registered per ECS, like
// PathfindingSystem, so a client's prediction ECS and the host's authoritative ECS
// keep independent records. Query via ecs.GetSystem<TargetingSystem>().GetTargets(id).
public class TargetingSystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    private readonly Dictionary<ulong, HashSet<ulong>> _targets = new Dictionary<ulong, HashSet<ulong>>();

    public void Execute(ECS ecs)
    {
        List<SetTargetsInput> inputs = ecs.GetInputsForTick<SetTargetsInput>();
        if (inputs == null) return;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();

        foreach (SetTargetsInput input in inputs)
            Apply(ecs, input, troopStore);
    }

    // Ignores any friendly id that isn't a troop, isn't owned by the requesting client,
    // or can't currently take actions — mirrors the guards PathfindingSystem applies to
    // move commands.
    private void Apply(ECS ecs, SetTargetsInput input, ComponentStore<TroopComponent> troopStore)
    {
        foreach (ulong friendlyId in input.FriendlyTroopIds)
        {
            if (!troopStore.HasComponent(friendlyId)) continue;

            TroopComponent troop = troopStore.GetComponent(friendlyId);
            if (troop.OwnerPlayerId != input.ClientId) continue;
            if (!troop.CanTakeActions) continue;

            if (!_targets.TryGetValue(friendlyId, out HashSet<ulong> set))
            {
                set = new HashSet<ulong>();
                _targets[friendlyId] = set;
            }

            if (!input.AdditionalSelect)
                set.Clear();

            foreach (ulong targetId in input.TargetTroopIds)
                if (ecs.HasEntity(targetId))
                    set.Add(targetId);
        }
    }

    public IReadOnlyCollection<ulong> GetTargets(ulong friendlyTroopId)
        => _targets.TryGetValue(friendlyTroopId, out HashSet<ulong> set) ? set : Array.Empty<ulong>();
}
