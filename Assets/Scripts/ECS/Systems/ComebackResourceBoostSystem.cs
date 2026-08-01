using UnityEngine;

// Subscribes to ResourcesAdded and scales Multiplier up for any player who's currently
// behind in net worth (PlayerTotalResourceValueComponent.TotalValue — see
// ResourceValueTotalSystem, which now folds a player's currently-held resources into that
// total too) relative to whichever player currently has the highest total — so a losing
// player claws back ground a little faster on every resource gain (harvesting, passive
// generation, on-death drops, ...), not just the positional drops ResourceDropBoostSystem
// already boosts.
//
// valueRatio = (this player's TotalValue) / (the leading player's TotalValue). At
// valueRatio 1.0 (tied with, or ahead of, the leader) the multiplier is left untouched —
// explicit "no penalty to the leader" design ask, and a tie gets no bonus either. Below
// that it scales linearly, reaching its cap of MaxBonusRatio (+50%, i.e. a 1.5x multiplier)
// once a player's TotalValue drops to ComebackRatioThreshold (half) of the leader's, and
// stays capped there for anything further behind than that.
//
// Runs via a plain Subscribe (not SubscribeExecuted), same shape as ResourceDropBoostSystem
// — mutates Multiplier BEFORE ResourcesAdded.Execute applies Amount * Multiplier — so the
// two compose (both just multiply the same float) regardless of registration order. No
// isServer gate needed: this only scales a float on an in-flight request, safe to run
// identically on client prediction and server alike. Unlike ResourceDropBoostSystem, this
// isn't gated on X/Y at all — it applies to every kind of resource gain, positional or not.
public static class ComebackResourceBoostSystem
{
    // Where the leader-ratio the linear ramp caps out at its max bonus — see this class's
    // own doc comment.
    private const float ComebackRatioThreshold = 0.5f;
    // +50% -> a 1.5x multiplier at/below ComebackRatioThreshold.
    private const float MaxBonusRatio = 0.5f;

    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
        => ecs.Requests.Subscribe<ResourcesAdded>(ApplyComebackBoost);

    private static void ApplyComebackBoost(ResourcesAdded request, ECS ecs)
    {
        ComponentStore<PlayerTotalResourceValueComponent> totalStore = ecs.GetComponentStore<PlayerTotalResourceValueComponent>();
        if (totalStore == null || !totalStore.HasComponent(request.PlayerEntityId)) return;

        float leaderValue = 0f;
        totalStore.ForEach((ulong id) =>
        {
            float value = totalStore.GetComponent(id).TotalValue;
            if (value > leaderValue) leaderValue = value;
        });

        if (leaderValue <= 0f) return; // nobody has any net worth yet — nothing to compare against

        float playerValue = totalStore.GetComponent(request.PlayerEntityId).TotalValue;
        float valueRatio = playerValue / leaderValue;
        if (valueRatio >= 1f) return; // tied with or ahead of the leader — no bonus, no penalty

        float bonus = Mathf.Clamp01((1f - valueRatio) / (1f - ComebackRatioThreshold)) * MaxBonusRatio;
        request.Multiplier *= 1f + bonus;
    }
}
