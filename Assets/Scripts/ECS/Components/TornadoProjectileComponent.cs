// A ground-level AOE hitbox that travels in a straight line from wherever it was created
// toward Destination(X/Y) at TilesPerSecond — see TornadoProjectileSystem, which is what
// actually moves it and applies its pull. Unlike a pooled SkillshotProjectileComponent,
// this isn't owned by (or its damage/effects derived from) any troop — it's a one-off spell
// effect (TornadoCard), so it carries its own OwnerPlayerId directly rather than looking one
// up off a shooter entity.
public struct TornadoProjectileComponent : IComponent
{
    public float DestinationX;
    public float DestinationY;
    public float TilesPerSecond;

    // Radius (world/tile units) used each tick to find enemy troops to pull along — see
    // TornadoProjectileSystem.PullOverlappingEnemies.
    public float HitRadius;

    public ushort OwnerPlayerId;
}
