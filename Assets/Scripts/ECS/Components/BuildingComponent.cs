public struct BuildingComponent : IComponent
{
    // Troops that aren't currently moving get instantly relocated outside this radius
    // (from the building's PositionComponent) — see BuildingBlockingSystem.
    public float BlockRadius;
}
