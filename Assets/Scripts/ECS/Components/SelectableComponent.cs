public struct SelectableComponent : IComponent
{
    public ushort OwnerPlayerId;

    // Uniform scale applied to this entity's selection/targeting ring (see
    // SelectionPrefab.SetScale/TargetingPrefab.SetScale) — bigger entities (buildings,
    // trees) want a bigger ring than troops.
    public float Scale;
}