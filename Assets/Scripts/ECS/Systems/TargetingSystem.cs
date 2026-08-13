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

    // Fallback for StatsQuery.GetRange, matching BasicRangedAISystem's own default.
    private const int DefaultRange = 8;

    public void Execute(ECS ecs)
    {
        List<SetTargetsInput> inputs = ecs.GetInputsForTick<SetTargetsInput>();
        if (inputs == null) return;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<BasicRangedAIComponent> rangedAiStore = ecs.GetComponentStore<BasicRangedAIComponent>();
        ComponentStore<MovableComponent> movStore = ecs.GetComponentStore<MovableComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();

        foreach (SetTargetsInput input in inputs)
            Apply(ecs, input, troopStore, rangedAiStore, movStore, posStore);
    }

    // Ignores any friendly id that isn't a troop, isn't owned by the requesting client,
    // or can't currently take actions — mirrors the guards PathfindingSystem applies to
    // move commands. A player targeting command always clears that troop's automatic
    // targets (regardless of AdditionalSelect) before applying the player's own — taking
    // explicit control supersedes whatever the AI had picked.
    private void Apply(ECS ecs, SetTargetsInput input, ComponentStore<TroopComponent> troopStore,
        ComponentStore<BasicRangedAIComponent> rangedAiStore, ComponentStore<MovableComponent> movStore, ComponentStore<PositionComponent> posStore)
    {
        foreach (ulong friendlyId in input.FriendlyTroopIds)
        {
            if (!troopStore.HasComponent(friendlyId)) continue;

            TroopComponent troop = troopStore.GetComponent(friendlyId);
            if (troop.OwnerPlayerId != input.ClientId) continue;
            if (!ActivationQuery.IsActivated(ecs, friendlyId)) continue;

            // An attack-target order is always allowed to land, even mid-recall — it just
            // ends the recall immediately instead of being silently blocked by
            // CanPerformRequest's own veto (see RecallSystem.CancelRecall).
            RecallSystem.CancelRecall(ecs, friendlyId);

            Dictionary<ulong, TargetKind> targets = GetOrCreate(friendlyId);

            RemoveAllOfKind(targets, TargetKind.Automatic);
            if (!input.AdditionalSelect)
                RemoveAllOfKind(targets, TargetKind.PlayerAssigned);

            foreach (ulong targetId in input.TargetTroopIds)
                if (ecs.HasEntity(targetId))
                    targets[targetId] = TargetKind.PlayerAssigned;

            // A fresh attack-target command supersedes a prior move order, the same way a
            // move order already interrupts an in-progress attack windup (see
            // BasicMeleeAISystem/BasicRangedAISystem's _movedThisTick check). Without this,
            // playerDestinationSet stays stuck true until the stale move order's destination
            // is reached, which blocks BasicMeleeAISystem/BasicRangedAISystem's automatic-
            // target fallback (AcquireTargets and the Automatic-kind lookup are both gated on
            // !playerDestinationSet). That fallback is what lets a client which doesn't own
            // this troop (and so never sees the real SetTargetsInput that assigned this
            // PlayerAssigned target — see InputBuffer/NetworkManager.OnClientTickInput, which
            // only ever reaches the server) keep that troop's local prediction chasing/
            // attacking in step with the authoritative simulation. Leaving it gated stuck
            // true left such an observer's local replica of this troop with no active target
            // at all, so only PathfindingSystem kept nudging it toward a destination that
            // never got refreshed locally — fine over a long chase (never arrives before the
            // next reconciliation correction), but at short range it would reach that stale
            // destination and stop within a tick or two, then get corrected, then stop again,
            // reading as the position jittering with no walk animation.
            if (input.TargetTroopIds.Count > 0 && movStore != null && movStore.HasComponent(friendlyId))
            {
                ref MovableComponent mov = ref movStore.GetComponent(friendlyId);
                if (mov.playerDestinationSet)
                {
                    mov.playerDestinationSet = false;
                    ecs.Delta.MarkComponentDirty(friendlyId, typeof(MovableComponent));
                }
            }

            StopIfRangedTargetInRange(ecs, friendlyId, input.TargetTroopIds, rangedAiStore, movStore, posStore);
        }
    }

    // A ranged troop otherwise only stops once BasicRangedAISystem's own Tick notices the
    // newly assigned target is in range — while still mid-chase (or mid windup/wind-down
    // against a stale target) that reads as a brief continued slide toward wherever it was
    // already heading before the command lands. Stopping it here instead, the instant a
    // targeting command comes in for a target already in range, gives immediate feedback
    // that the command was received.
    private void StopIfRangedTargetInRange(ECS ecs, ulong friendlyId, List<ulong> targetIds,
        ComponentStore<BasicRangedAIComponent> rangedAiStore, ComponentStore<MovableComponent> movStore, ComponentStore<PositionComponent> posStore)
    {
        if (rangedAiStore == null || !rangedAiStore.HasComponent(friendlyId)) return;
        if (movStore == null || !movStore.HasComponent(friendlyId)) return;
        if (posStore == null || !posStore.HasComponent(friendlyId)) return;

        PositionComponent myPos = posStore.GetComponent(friendlyId);
        int range = StatsQuery.GetRange(ecs, friendlyId, DefaultRange);

        bool inRange = false;
        foreach (ulong targetId in targetIds)
        {
            if (!posStore.HasComponent(targetId)) continue;

            PositionComponent targetPos = posStore.GetComponent(targetId);
            float dx = targetPos.X - myPos.X, dy = targetPos.Y - myPos.Y;
            if (dx * dx + dy * dy <= range * range)
            {
                inRange = true;
                break;
            }
        }

        if (!inRange) return;

        ref MovableComponent mov = ref movStore.GetComponent(friendlyId);
        if (mov.currentMovementMode == MovementMode.NotMoving) return;

        mov.currentMovementMode = MovementMode.NotMoving;
        ecs.Delta.MarkComponentDirty(friendlyId, typeof(MovableComponent));
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

    // Removes every automatic-kind target for a friendly troop, leaving any player-assigned
    // one untouched — used when a troop switches into Passive mode (SetAIModeSystem). Passive
    // never picks a new automatic target itself (AcquireTargets is skipped entirely for
    // Passive — see BasicMeleeAISystem/BasicRangedAISystem's mode guard), but switching into
    // it doesn't retroactively forget one already picked up under Guard/Aggressive.
    public void ClearAutomaticTargets(ulong friendlyTroopId)
    {
        if (!_targets.TryGetValue(friendlyTroopId, out Dictionary<ulong, TargetKind> targets)) return;
        RemoveAllOfKind(targets, TargetKind.Automatic);
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
