using UnityEngine;

/// <summary>
/// Enqueue after IslandPlayerBaseFeature (see WorldManager.SetupWorldGen) — places a single
/// neutral island at the exact map center, resolved by name from
/// WorldManager.instance.IslandRegistry (see IslandName). Read by IslandBridgeFeature (via
/// GetPreviousFeature) to connect every player base island to it.
/// </summary>
public class MidIslandFeature : WorldGenFeature
{
    public string IslandName;

    // Null if IslandName didn't resolve to a registered island, or that island's prefab has
    // no IslandFootprint component — IslandBridgeFeature checks for this and just skips
    // spoke bridges rather than throwing.
    public IslandPlacementHelper.PlacedIsland? MidIsland { get; private set; }

    public override void Generate(WorldGenHandler handler)
    {
        if (string.IsNullOrEmpty(IslandName))
        {
            Debug.LogWarning("[MidIslandFeature] No IslandName assigned — skipping.");
            return;
        }

        IslandRegistry registry = WorldManager.instance != null ? WorldManager.instance.IslandRegistry : null;
        if (registry == null)
        {
            Debug.LogWarning("[MidIslandFeature] No IslandRegistry assigned on WorldManager — skipping.");
            return;
        }

        if (!registry.TryGetPrefab(IslandName, out GameObject prefab))
        {
            Debug.LogWarning($"[MidIslandFeature] No island registered under name '{IslandName}' — skipping.");
            return;
        }

        ushort worldSize = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);
        int center = worldSize / 2;

        MidIsland = IslandPlacementHelper.TryPlaceIsland(handler, prefab, center, center, worldSize);
        if (MidIsland == null)
            Debug.LogWarning($"[MidIslandFeature] Prefab '{prefab.name}' has no IslandFootprint component — skipping.");
    }
}
