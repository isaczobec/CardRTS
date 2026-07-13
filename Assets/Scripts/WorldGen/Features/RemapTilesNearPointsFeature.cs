using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic "remap tiles near a set of points" feature. Points is evaluated once, at
/// Generate time, via a lambda taking the WorldGenHandler — so it can depend on state only
/// known once earlier features have run (e.g. SpawnPlayerBasesFeature.Bases). Every tile
/// within Radius of any point whose current TileType is a key in TileMap is set to the
/// paired value; tile types with no entry are left untouched.
///
/// Used by WorldManager.SetupWorldGen, with Points sourced from SpawnPlayerBasesFeature and
/// TileMap built by WorldManager.BuildCollisionClearingMap(), to guarantee no colliding
/// tiles spawn within a base's footprint.
/// </summary>
public class RemapTilesNearPointsFeature : WorldGenFeature
{
    public Func<WorldGenHandler, IEnumerable<(float x, float y)>> Points;
    public float Radius = 6f;
    public Dictionary<TileType, TileType> TileMap = new();

    public override void Generate(WorldGenHandler handler)
    {
        if (Points == null || TileMap.Count == 0) return;

        ushort worldSize = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);

        foreach ((float px, float py) in Points(handler))
        {
            ushort minX = (ushort)Mathf.Clamp(Mathf.FloorToInt(px - Radius), 0, worldSize - 1);
            ushort maxX = (ushort)Mathf.Clamp(Mathf.CeilToInt(px + Radius), 0, worldSize - 1);
            ushort minY = (ushort)Mathf.Clamp(Mathf.FloorToInt(py - Radius), 0, worldSize - 1);
            ushort maxY = (ushort)Mathf.Clamp(Mathf.CeilToInt(py + Radius), 0, worldSize - 1);

            for (ushort ty = minY; ty <= maxY; ty++)
            {
                for (ushort tx = minX; tx <= maxX; tx++)
                {
                    float dx = tx - px, dy = ty - py;
                    if (dx * dx + dy * dy > Radius * Radius) continue;

                    TileType current = handler.GetTileType(tx, ty);
                    if (TileMap.TryGetValue(current, out TileType replacement))
                        handler.SetTileType(tx, ty, replacement);
                }
            }
        }
    }
}
