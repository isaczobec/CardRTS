using UnityEngine;

// Places the world's three soulstone resource nodes (see EntitySpawnAction.SpawnSoulstoneNodeX)
// in a small fixed cluster at the exact center of the map — a neutral, contested objective
// every player is equidistant from, unlike EntityClusterFeature's per-biome scattered
// placement. Each node has a different size and dead-on-spawn respawn timer, ramping down to
// a shared shorter timer after each one's own first respawn (see
// RespawnCooldownRampComponent). Enqueue after terrain/biome generation (see
// WorldManager.SetupWorldGen), like SpawnPlayerBasesFeature.
public class SoulstoneClusterFeature : WorldGenFeature
{
    // Distance from the exact map center each of the three nodes is placed, arranged in an
    // equilateral triangle so none of them overlap.
    public float ClusterRadius = 12f;

    public override void Generate(WorldGenHandler handler)
    {
        float worldSize = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks;
        float center = worldSize * 0.5f;

        PlaceNode(handler, center, 0f, EntitySpawnAction.SpawnSoulstoneNodeSmall);
        PlaceNode(handler, center, 120f, EntitySpawnAction.SpawnSoulstoneNodeMedium);
        PlaceNode(handler, center, 240f, EntitySpawnAction.SpawnSoulstoneNodeLarge);
    }

    private void PlaceNode(WorldGenHandler handler, float center, float angleDegrees, System.Action<ulong, ECS> spawner)
    {
        float angle = angleDegrees * Mathf.Deg2Rad;
        float x = center + Mathf.Cos(angle) * ClusterRadius;
        float y = center + Mathf.Sin(angle) * ClusterRadius;
        handler.EnqueueAction(new EntitySpawnAction { X = x, Y = y, Spawner = spawner });
    }
}
