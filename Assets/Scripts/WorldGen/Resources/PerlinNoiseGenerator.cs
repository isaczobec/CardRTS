using UnityEngine;

public class PerlinNoiseGenerator : NoiseGenerator
{
    public override string Identifier { get; }
    public float Scale = 0.05f;
    public float OffsetX = 0f;
    public float OffsetY = 0f;

    public PerlinNoiseGenerator(string identifier)
    {
        Identifier = identifier;
    }

    public override float Sample(float x, float y)
    {
        return Mathf.PerlinNoise((x + OffsetX) * Scale, (y + OffsetY) * Scale);
    }
}
