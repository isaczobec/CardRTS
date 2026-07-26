// Marks a troop as a Shadow Angel and tracks whether it has already redistributed damage
// this tick — see ShadowAngelDamageShareSystem/ShadowAngelTickResetSystem.
//
// Without this, two Shadow Angels within each other's aura range (each holding the other's
// ShadowAngelDamageShareComponent) can ping-pong a single hit back and forth indefinitely:
// A redirects 90% to B, B redirects 90% of THAT back to A, and so on. That looks like it
// should converge to 0, but integer rounding gets stuck at a nonzero fixed point instead —
// e.g. Mathf.RoundToInt(1 * 0.9) == 1 forever — so the chain never actually terminates and
// RequestManager.Flush's while loop spins forever on newly-created DamageRequests, freezing
// the game. Limiting each Angel to redistributing at most once per tick breaks that cycle
// outright regardless of the rounding behavior: by the second bounce back to the same Angel,
// it just takes the hit instead of redirecting it again.
public struct ShadowAngelComponent : IComponent
{
    public bool HasRedistributedDamageThisTick;
}
