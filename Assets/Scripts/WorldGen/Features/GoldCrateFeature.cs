using UnityEngine;

// Scatters neutral gold crates (see EntitySpawnAction.SpawnGoldCrate) across the mid island
// (see MidIslandFeature) — each a one-time, flat-Gold pickup, unlike the mid island's
// renewable Soulstone/resource nodes. Total crate count scales with player count
// (CratesPerPlayer * ConnectedClientIds.Count) rather than a fixed number, so a bigger match
// has proportionally more bonus gold to contest. Enqueue after MidIslandFeature (needs its
// walkable tiles to already exist) and SoulstoneClusterFeature (so crates spawn clear of the
// three soulstone nodes already placed there) — see WorldManager.SetupWorldGen.
public class GoldCrateFeature : WorldGenFeature
{
    public int CratesPerPlayer = 2;

    // Crates are scattered within this radius band from the exact map center — the inner
    // bound keeps them clear of the map-center soulstone ring's own inner nodes, the outer
    // bound keeps them comfortably inside the mid island's own walkable extent (IsWalkable
    // below still rejects anything that lands off the island regardless).
    public float MinRadiusFromCenter = 12f;
    public float MaxRadiusFromCenter = 32f;

    // Minimum distance kept from every other already-spawned entity (the three soulstone
    // nodes, and any earlier crate placed by this same call) — via SpawnedEntityRegistry,
    // same mechanism EntityClusterFeature.MinDistanceToOtherEntities uses.
    public float MinDistanceToOtherEntities = 6f;

    private const int MaxPlacementAttempts = 30;

    public override void Generate(WorldGenHandler handler)
    {
        var midFeature = handler.GetPreviousFeature<MidIslandFeature>();
        if (midFeature == null || !midFeature.MidIsland.HasValue) return;

        int playerCount = handler.ConnectedClientIds?.Count ?? 0;
        int crateCount = Mathf.Max(0, CratesPerPlayer * playerCount);
        if (crateCount == 0) return;

        float worldSize = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks;
        float center = worldSize * 0.5f;
        float maxCoord = worldSize - 1f;

        var registry = handler.GetWorldGenResource<SpawnedEntityRegistry>(SpawnedEntityRegistry.ResourceKey);
        System.Random rng = handler.Random;

        for (int i = 0; i < crateCount; i++)
        {
            if (!TryPickCratePosition(center, maxCoord, registry, rng, out float x, out float y)) continue;
            handler.EnqueueAction(new EntitySpawnAction { X = x, Y = y, Spawner = EntitySpawnAction.SpawnGoldCrate });
        }
    }

    private bool TryPickCratePosition(float center, float maxCoord, SpawnedEntityRegistry registry, System.Random rng, out float x, out float y)
    {
        for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
        {
            float angle = (float)(rng.NextDouble() * 2.0 * Mathf.PI);
            float radius = Mathf.Lerp(MinRadiusFromCenter, MaxRadiusFromCenter, (float)rng.NextDouble());

            float candidateX = Mathf.Clamp(center + Mathf.Cos(angle) * radius, 0f, maxCoord);
            float candidateY = Mathf.Clamp(center + Mathf.Sin(angle) * radius, 0f, maxCoord);

            if (!IsWalkable(candidateX, candidateY)) continue;
            if (MinDistanceToOtherEntities > 0f && registry != null && registry.AnyWithinRadius(candidateX, candidateY, MinDistanceToOtherEntities))
                continue;

            x = candidateX;
            y = candidateY;
            return true;
        }

        x = y = 0f;
        return false;
    }

    // Same "collision" concept EntityClusterFeature.IsWalkable relies on — see that method's
    // own doc comment for why this is safe to call mid-world-gen.
    private static bool IsWalkable(float x, float y)
    {
        if (WorldManager.instance == null) return true;
        ushort tileX = (ushort)Mathf.Max(0, Mathf.RoundToInt(x));
        ushort tileY = (ushort)Mathf.Max(0, Mathf.RoundToInt(y));
        return !WorldManager.instance.HasCollision(tileX, tileY);
    }
}
