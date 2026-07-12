using System;
using System.Collections.Generic;

public enum TargetKind : byte
{
    PlayerAssigned = 0,
    Automatic = 1,
}

// Tracks which entities each friendly troop is currently targeting, and whether each
// one was explicitly assigned by the player or picked automatically (e.g. by a future
// troop-AI system). Built from SetTargetsInput each tick, plus SetAutomaticTarget /
// RemoveAutomaticTarget for other systems to drive automatic targeting directly.
// Instance (not static) and registered per ECS, like PathfindingSystem, so a client's
// prediction ECS and the host's authoritative ECS keep independent records. Query/
// mutate via ecs.GetSystem<TargetingSystem>().
public class TargetingSystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    // friendlyTroopId -> (targetId -> kind)
    private readonly Dictionary<ulong, Dictionary<ulong, TargetKind>> _targets = new Dictionary<ulong, Dictionary<ulong, TargetKind>>();
    private readonly List<ulong> _removalScratch = new List<ulong>();

    public void Setup(ECS ecs) { }

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
    // move commands. A player targeting command always clears that troop's automatic
    // targets (regardless of AdditionalSelect) before applying the player's own — taking
    // explicit control supersedes whatever the AI had picked.
    private void Apply(ECS ecs, SetTargetsInput input, ComponentStore<TroopComponent> troopStore)
    {
        foreach (ulong friendlyId in input.FriendlyTroopIds)
        {
            if (!troopStore.HasComponent(friendlyId)) continue;

            TroopComponent troop = troopStore.GetComponent(friendlyId);
            if (troop.OwnerPlayerId != input.ClientId) continue;
            if (!troop.CanTakeActions) continue;

            Dictionary<ulong, TargetKind> targets = GetOrCreate(friendlyId);

            RemoveAllOfKind(targets, TargetKind.Automatic);
            if (!input.AdditionalSelect)
                RemoveAllOfKind(targets, TargetKind.PlayerAssigned);

            foreach (ulong targetId in input.TargetTroopIds)
                if (ecs.HasEntity(targetId))
                    targets[targetId] = TargetKind.PlayerAssigned;
        }
    }

    // All current targets for a friendly troop, regardless of kind.
    public IReadOnlyCollection<ulong> GetTargets(ulong friendlyTroopId)
        => _targets.TryGetValue(friendlyTroopId, out Dictionary<ulong, TargetKind> targets) ? targets.Keys : Array.Empty<ulong>();

    public TargetKind? GetTargetKind(ulong friendlyTroopId, ulong targetId)
        => _targets.TryGetValue(friendlyTroopId, out Dictionary<ulong, TargetKind> targets) && targets.TryGetValue(targetId, out TargetKind kind)
            ? kind
            : null;

    // Adds/updates an automatic target — for troop-AI systems (e.g. "attack whatever
    // enemy wandered into range"). Never overwrites a target the player explicitly
    // assigned; player intent takes priority over AI.
    public void SetAutomaticTarget(ulong friendlyTroopId, ulong targetId)
    {
        Dictionary<ulong, TargetKind> targets = GetOrCreate(friendlyTroopId);
        if (targets.TryGetValue(targetId, out TargetKind existing) && existing == TargetKind.PlayerAssigned)
            return;
        targets[targetId] = TargetKind.Automatic;
    }

    // Removes a specific automatic target (e.g. it left range or died). No-ops if it
    // isn't currently an automatic target for this troop — this never removes a
    // player-assigned target.
    public void RemoveAutomaticTarget(ulong friendlyTroopId, ulong targetId)
    {
        if (!_targets.TryGetValue(friendlyTroopId, out Dictionary<ulong, TargetKind> targets)) return;
        if (targets.TryGetValue(targetId, out TargetKind kind) && kind == TargetKind.Automatic)
            targets.Remove(targetId);
    }

    private Dictionary<ulong, TargetKind> GetOrCreate(ulong friendlyTroopId)
    {
        if (!_targets.TryGetValue(friendlyTroopId, out Dictionary<ulong, TargetKind> targets))
        {
            targets = new Dictionary<ulong, TargetKind>();
            _targets[friendlyTroopId] = targets;
        }
        return targets;
    }

    private void RemoveAllOfKind(Dictionary<ulong, TargetKind> targets, TargetKind kind)
    {
        _removalScratch.Clear();
        foreach (KeyValuePair<ulong, TargetKind> kvp in targets)
            if (kvp.Value == kind)
                _removalScratch.Add(kvp.Key);
        foreach (ulong id in _removalScratch)
            targets.Remove(id);
    }
}
