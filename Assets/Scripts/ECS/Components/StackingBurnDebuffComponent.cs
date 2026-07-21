// Tracks how many consecutive hits from the same burn-inflicting source a target has
// endured — see ProjectileOnHitSystem.ApplyBurn/ApplyScorch, which increments Stacks (up to
// MaxStacks) every time the Scorched modifier this lives alongside is refreshed by a fresh
// hit, and scales the accompanying DamageOverTimeComponent.DamagePerProc up accordingly.
// Stacks starts at 0 on the first hit (the base burn, unstacked) and counts additional
// refreshing hits from there.
public struct StackingBurnDebuffComponent : IComponent
{
    public int Stacks;
    public int MaxStacks;
}
