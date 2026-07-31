using UnityEngine;

// Shared setup for "passive resource generation building" cards (SawmillCard/QuarryCard/
// MineCard) — computes how much a newly-placed one should generate per minute based on how
// far it is from the OWNER's own base (RenderableType.PlayerBaseCore — see
// SpawnPlayerBasesFeature) relative to how far that same base is from the world's center:
// MinRatePerMinute right at/near the base, scaling up LINEARLY to MaxRatePerMinute at the
// world center (and clamped there for anything placed even further out), then attaches a
// ResourceGeneratorComponent proccing often enough for FloatingTextManager's "+N" popups to
// actually round to a visible nonzero integer (see ProcPeriodSeconds).
public static class ResourceGeneratorCardHelper
{
    private const float MinRatePerMinute = 30f;
    private const float MaxRatePerMinute = 100f;

    // How often a generator procs a chunk of its own total per-minute rate — a period of a
    // few seconds reads better as floating "+N" feedback than either a single once-a-minute
    // lump sum or a per-tick trickle too small to even round to a nonzero displayed integer
    // (see FloatingTextManager.SpawnResourceGain's own Mathf.RoundToInt).
    private const float ProcPeriodSeconds = 5f;

    public static void AddResourceGenerator(ECS ecs, ulong id, ushort ownerPlayerId, float x, float y, ResourceType type)
    {
        float ratePerMinute = ComputeRatePerMinute(ecs, ownerPlayerId, x, y);
        int periodTicks = TickManager.SecondsToTicks(ProcPeriodSeconds);

        ecs.AddComponent(id, new ResourceGeneratorComponent
        {
            Type                = type,
            AmountPerProc       = ratePerMinute * (ProcPeriodSeconds / 60f),
            PeriodTicks         = periodTicks,
            TicksUntilNextProc  = periodTicks,
        });
    }

    // Distance-based, not "how far along the base-to-center line" — a generator placed
    // anywhere at the reference distance from the base reads as "fully at the far end" (100/
    // min) regardless of which direction from the base it actually is, which is simpler and
    // reads just as sensibly as a stricter on-the-line interpretation.
    private static float ComputeRatePerMinute(ECS ecs, ushort ownerPlayerId, float x, float y)
    {
        // No base found (e.g. already destroyed) — conservative fallback rather than
        // erroring out or dividing by an unknown reference distance.
        if (!PlayerBaseQuery.TryFindPosition(ecs, ownerPlayerId, out float baseX, out float baseY))
            return MinRatePerMinute;

        float worldSize = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks;
        Vector2 center = new Vector2(worldSize * 0.5f, worldSize * 0.5f);
        Vector2 basePos = new Vector2(baseX, baseY);

        float maxDistance = Vector2.Distance(basePos, center);
        if (maxDistance <= 0.0001f) return MinRatePerMinute;

        float distance = Vector2.Distance(new Vector2(x, y), basePos);
        float t = Mathf.Clamp01(distance / maxDistance);
        return Mathf.Lerp(MinRatePerMinute, MaxRatePerMinute, t);
    }

}
