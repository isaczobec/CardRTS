using UnityEngine;

/// <summary>
/// Owns the WorldGenHandler and drives world generation + rendering.
/// Assign Renderer in the Inspector. Call GenerateAndRender() once the game starts.
/// </summary>
public class WorldManager : Singleton<WorldManager>
{
    public WorldRenderer Renderer;

    [SerializeField] TileSettings[] _tileSettings;

    public WorldGenHandler Handler { get; private set; }

    // Indexed by (int)TileType for O(1) lookup. Built in GenerateAndRender.
    TileSettings[] _settingsByType;

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
        BuildTileSettingsLookup();
    }

    void BuildTileSettingsLookup()
    {
        int count = System.Enum.GetValues(typeof(TileType)).Length;
        _settingsByType = new TileSettings[count];
        if (_tileSettings != null)
            foreach (var s in _tileSettings)
                _settingsByType[(int)s.Type] = s;
    }

    public TileSettings GetTileSettings(TileType type) => _settingsByType[(int)type];

    public bool HasCollision(ushort tileX, ushort tileY)
        => _settingsByType[(int)Handler.GetTileType(tileX, tileY)].HasCollision;

    void SetupWorldGen(WorldGenHandler handler)
    {
        // --- Terrain noise ---
        handler.AddResource(new PerlinNoiseGenerator("terrain")
        {
            Scale   = 0.04f,
            OffsetX = 0f,
            OffsetY = 0f,
        });

        // --- Biome axes: temperature (X) and humidity (Y) ---
        handler.AddResource(new PerlinNoiseGenerator("biomeTemp")
        {
            Scale   = 0.005f,
            OffsetX = 500f,
            OffsetY = 300f,
        });
        handler.AddResource(new PerlinNoiseGenerator("biomeHumidity")
        {
            Scale   = 0.005f,
            OffsetX = 200f,
            OffsetY = 700f,
        });

        // Desert biome: hot (high temp) and dry (low humidity).
        handler.features.Add(new BiomeFeature
        {
            NoiseKeyX = "biomeTemp",
            NoiseKeyY = "biomeHumidity",
            MinX = 0.60f, MaxX = 1.00f,
            MinY = 0.00f, MaxY = 1.00f,
            ChildFeatures = new WorldGenFeature[]
            {
                // Repaint interior terrain with arid thresholds (more sand, no grass).
                // Water is preserved so existing lakes/rivers remain as oases.
                new BiomeBorderFillFeature { Threshold = 1.0f, FillType = TileType.Sand },
            }
        });

        // Wetland biome: cold (low temp) and humid (high humidity).
        handler.features.Add(new BiomeFeature
        {
            NoiseKeyX = "biomeTemp",
            NoiseKeyY = "biomeHumidity",
            MinX = 0.00f, MaxX = 0.60f,
            MinY = 0.00f, MaxY = 1.00f,
            ChildFeatures = new WorldGenFeature[]
            {
                // Repaint interior terrain with lush thresholds (more grass, less sand).
                // Water is preserved so rivers flow through the wetland naturally.
                new BiomeBorderFillFeature { Threshold = 1.0f, FillType = TileType.Grass },
            }
        });
    }
}
