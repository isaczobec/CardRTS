using UnityEngine;

// Wraps an inner Perlin sample into a "ridged" pattern (1 - |2p - 1|) — instead of the smooth
// round blobs plain Perlin noise produces, the output forms thin, winding veins along the
// inner noise's 0.5 contour lines, peaking at 1 exactly on the contour and falling off to
// either side. Thresholding only the top of that output (a high NoiseTileFeature.
// NoiseMinThreshold, e.g. 0.85) yields narrow, connected corridors rather than filled areas —
// used for TundraBiome's tunnel-like mountain formations (see WorldManager.SetupWorldGen).
public class RidgedNoiseGenerator : NoiseGenerator
{
    public override string Identifier { get; }
    public float Scale = 0.05f;
    public float OffsetX = 0f;
    public float OffsetY = 0f;

    public RidgedNoiseGenerator(string identifier)
    {
        Identifier = identifier;
    }

    public override float Sample(float x, float y)
    {
        float p = Mathf.PerlinNoise((x + OffsetX) * Scale, (y + OffsetY) * Scale);
        return 1f - Mathf.Abs(2f * p - 1f);
    }
}
