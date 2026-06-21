public abstract class NoiseGenerator : WorldGenResource
{
    public abstract string Identifier { get; }

    /// <summary>
    /// Returns a value in [0, 1] for the given world-space tile coordinates.
    /// </summary>
    public abstract float Sample(float x, float y);
}
