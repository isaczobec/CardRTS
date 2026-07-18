// Attach alongside RespawnableInPlaceComponent to change its CooldownTicks the first time
// (and only the first time) the entity respawns — e.g. a world-gen resource node that's
// dead-on-spawn with a long initial "grace period" cooldown, then settles into a shorter
// steady-state cooldown for every death after that. See RespawnCooldownRampSystem.
public struct RespawnCooldownRampComponent : IComponent
{
    public ulong CooldownTicksAfterFirstRespawn;

    // False until the ramp has been applied once — guards against re-applying it (a no-op,
    // but still worth skipping) on every respawn after the first.
    public bool HasRamped;
}
