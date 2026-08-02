// Granted by CripplingStrikesUpgrade — attached to a permanent modifier entity
// (ModifierComponent.TargetEntityId = the troop it was equipped on, TicksRemaining =
// int.MaxValue), marking it as a source of the Crippling Strikes slow. See
// CripplingStrikesSystem, which finds this via
// ModifierQuery.ForEachActiveModifierId<CripplingStrikesSourceComponent> every time its
// target deals direct damage, and applies/refreshes a Chilled-style slow on whoever was hit.
public struct CripplingStrikesSourceComponent : IComponent
{
    public float SlowRatio;
    public float DurationSeconds;
}
