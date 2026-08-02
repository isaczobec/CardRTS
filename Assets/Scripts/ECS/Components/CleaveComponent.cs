// Granted by CleaveUpgrade — attached to a permanent modifier entity (ModifierComponent.
// TargetEntityId = the troop it was equipped on, TicksRemaining = int.MaxValue), mirroring
// GiantsbaneComponent's own "payload lives on the modifier entity" shape. See CleaveSystem,
// which finds this via ModifierQuery.ForEachActiveModifierId<CleaveComponent> every time its
// target deals damage.
public struct CleaveComponent : IComponent
{
    // Fraction of EACH damage instance this troop deals that also splashes onto nearby enemy
    // troops (excluding whoever was actually hit) — see CleaveSystem.
    public float SplashRatio;

    // Radius (world/tile units), centered on the ORIGINAL target, within which other enemy
    // troops also take the splash.
    public float Radius;
}
