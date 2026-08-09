using UnityEngine;

// Places the world's three soulstone resource nodes (see EntitySpawnAction.SpawnSoulstoneNodeX)
// in a medium-sized ring on the mid island (see MidIslandFeature), centered on the exact map
// center — a neutral, contested objective every player is equidistant from. Each node has a
// different size and dead-on-spawn respawn timer, ramping down to a shared shorter timer after
// each one's own first respawn (see RespawnCooldownRampComponent). Enqueue after MidIslandFeature
// (see WorldManager.SetupWorldGen) so the mid island's walkable tiles already exist for the
// walkability fallback below.
public class SoulstoneClusterFeature : WorldGenFeature
{
    // Distance from the exact map center each of the three nodes is placed, arranged in an
    // equilateral triangle so none of them overlap. Tuned to land comfortably inside the mid
    // island's own walkable footprint without needing to be hand-tuned per island size —
    // FindWalkableNear below nudges any candidate that lands off the island (e.g. a smaller mid
    // island prefab, or an angle where the island's silhouette pinches in) onto the nearest
    // walkable tile instead.
    public float RingRadius = 40f;

    // How far (in tiles) FindWalkableNear's expanding-ring search will look for a walkable tile
    // if the raw ring position itself isn't one.
    private const int NearbyWalkableSearchRadius = 30;

    public override void Generate(WorldGenHandler handler)
    {
        float worldSize = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks;
        float center = worldSize * 0.5f;
        float maxCoord = worldSize - 1f;

        PlaceNode(handler, center, 0f, maxCoord, EntitySpawnAction.SpawnSoulstoneNodeSmall);
        PlaceNode(handler, center, 120f, maxCoord, EntitySpawnAction.SpawnSoulstoneNodeMedium);
        PlaceNode(handler, center, 240f, maxCoord, EntitySpawnAction.SpawnSoulstoneNodeLarge);
    }

    private void PlaceNode(WorldGenHandler handler, float center, float angleDegrees, float maxCoord, System.Action<ulong, ECS> spawner)
    {
        float angle = angleDegrees * Mathf.Deg2Rad;
        float x = center + Mathf.Cos(angle) * RingRadius;
        float y = center + Mathf.Sin(angle) * RingRadius;

        (float fx, float fy) = FindWalkableNear(x, y, maxCoord);
        handler.EnqueueAction(new EntitySpawnAction { X = fx, Y = fy, Spawner = spawner });
    }

    // Same "collision" concept EntityClusterFeature.IsWalkable relies on — see that method's own
    // doc comment for why this is safe to call mid-world-gen. Spirals outward tile-by-tile from
    // the candidate point until it finds a walkable one, so RingRadius doesn't need to exactly
    // match whatever the configured mid island prefab's real walkable extent happens to be.
    private (float x, float y) FindWalkableNear(float x, float y, float maxCoord)
    {
        int centerTileX = Mathf.Clamp(Mathf.RoundToInt(x), 0, Mathf.RoundToInt(maxCoord));
        int centerTileY = Mathf.Clamp(Mathf.RoundToInt(y), 0, Mathf.RoundToInt(maxCoord));

        if (IsWalkable(centerTileX, centerTileY))
            return (x, y);

        for (int radius = 1; radius <= NearbyWalkableSearchRadius; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;

                    int tileX = centerTileX + dx;
                    int tileY = centerTileY + dy;
                    if (tileX < 0 || tileY < 0 || tileX > maxCoord || tileY > maxCoord) continue;
                    if (!IsWalkable(tileX, tileY)) continue;

                    return (tileX, tileY);
                }
            }
        }

        return (x, y); // fall back to the raw ring position if nothing walkable was found nearby
    }

    private static bool IsWalkable(int tileX, int tileY)
        => WorldManager.instance == null || !WorldManager.instance.HasCollision((ushort)tileX, (ushort)tileY);
}
