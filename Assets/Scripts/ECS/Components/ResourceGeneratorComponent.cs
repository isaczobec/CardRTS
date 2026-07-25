// Reusable "grant AmountPerProc of Type to this entity's own owner every PeriodTicks ticks"
// effect — attach directly to a building entity (e.g. SawmillCard/QuarryCard/MineCard)
// rather than via a separate modifier entity, since it's an innate trait of the building
// itself, not a temporary externally-applied buff (mirrors OnDeathResourceDropComponent's
// own "plain, always-on component" shape). See ResourceGeneratorSystem. Generation stops
// entirely the moment this entity is deleted — nothing else needs to clean up after it.
public struct ResourceGeneratorComponent : IComponent
{
    public ResourceType Type;
    public float AmountPerProc;
    public int PeriodTicks;
    public int TicksUntilNextProc;
}
