using UnityEngine;

/// <summary>
/// Owns the WorldGenHandler and drives world generation + rendering.
/// Assign Renderer in the Inspector. Call GenerateAndRender() once the game starts.
/// </summary>
public class WorldManager : Singleton<WorldManager>
{
    public WorldRenderer Renderer;

    public WorldGenHandler Handler { get; private set; }

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
