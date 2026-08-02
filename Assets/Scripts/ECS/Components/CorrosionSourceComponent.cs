// Granted by CorrosionUpgrade — attached to a permanent modifier entity (ModifierComponent.
// TargetEntityId = the troop it was equipped on, TicksRemaining = int.MaxValue), marking it
// as a source of Corrosion stacks. See CorrosionSystem, which finds this via
// ModifierQuery.ForEachActiveModifierId<CorrosionSourceComponent> every time its target deals
// damage, and applies/refreshes a CorrosionComponent-carrying modifier on whoever was hit.
public struct CorrosionSourceComponent : IComponent
{
    // Hits (including the one that first applies the debuff) before its own armor reduction
    // reaches MaxArmorReduction.
    public int MaxStacks;
    public float MaxArmorReduction;
    public float DurationSeconds;
}
