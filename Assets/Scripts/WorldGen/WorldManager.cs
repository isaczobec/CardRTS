using UnityEngine;

/// <summary>
/// Owns the WorldGenHandler and drives world generation + rendering.
/// Assign Renderer in the Inspector. Call GenerateAndRender() once the game starts.
/// </summary>
public class WorldManager : Singleton<WorldManager>
{
    public WorldRenderer Renderer;

    public WorldGenHandler Handler { get; private set; }

    /// <summary>
    /// Converts tile coordinates to a world-space Vector3.
    /// </summary>
    /// <param name="tileX">Tile X coordinate.</param>
    /// <param name="tileY">Tile Y coordinate.</param>
    /// <param name="center">If true, returns the center of the tile; otherwise returns the corner (origin).</param>
    /// <param name="useHeightmap">If true, samples the heightmap for the Y component. Center uses the average of the four corner heights.</param>
    public Vector3 TileToWorldPosition(ushort tileX, ushort tileY, bool center = false, bool useHeightmap = true)
    {
        float h = 0f;
        if (useHeightmap && Handler != null)
        {
            if (center)
            {
                ushort maxCoord = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks - 1);
                ushort x1 = (ushort)Mathf.Min(tileX + 1, maxCoord);
                ushort y1 = (ushort)Mathf.Min(tileY + 1, maxCoord);
                h = (Handler.GetHeight(tileX, tileY) +
                     Handler.GetHeight(x1,    tileY) +
                     Handler.GetHeight(tileX,    y1) +
                     Handler.GetHeight(x1,       y1)) * 0.25f;
            }
            else
            {
                h = Handler.GetHeight(tileX, tileY);
            }
        }
        float offset = center ? 0.5f : 0f;
        return new Vector3(tileX + offset, h, tileY + offset);
    }

    public void GenerateAndRender()
    {
        Handler = new WorldGenHandler();
        SetupWorldGen(Handler);
        Handler.Generate();
        Renderer.Render(Handler);
    }

    /// <summary>
    /// Configure resources and features here. Add tile-type thresholds once
    /// TileType values are defined in WorldGen.cs.
    /// </summary>
    void SetupWorldGen(WorldGenHandler handler)
    {
        handler.AddResource(new PerlinNoiseGenerator("terrain")
        {
            Scale   = 0.04f,
            OffsetX = 0f,
            OffsetY = 0f,
        });

        // Example: add a NoiseTileFeature once TileType has values.
        handler.features.Add(new NoiseTileFeature
        {
            NoiseResourceKey = "terrain",
            Thresholds = new()
            {
                new(0.35f, TileType.Water),
                new(0.45f, TileType.Sand),
                new(0.70f, TileType.Grass),
                new(1.00f, TileType.Mountain),
            }
        });
    }
}
