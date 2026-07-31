// Written by ResourceValueTotalSystem every ResourceValueTotalSystem.PeriodTicks ticks —
// that player's entire current holdings (every live ResourceValueComponent they own,
// battlefield entities plus cards/upgrades) converted into a single "unified value" number
// via ResourceConversionRates. Lives on the same entity as PlayerComponent/
// PlayerResourcesComponent (see NetworkManager.SpawnPlayerEntity).
public struct PlayerTotalResourceValueComponent : IComponent
{
    public float TotalValue;
}
