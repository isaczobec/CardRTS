// Attached to a modifier entity (alongside ModifierComponent, ModifierID.Corrosion) targeting
// whoever a CorrosionSourceComponent-carrying troop hits — see CorrosionSystem, which creates/
// refreshes this on every qualifying hit and subscribes to GetArmorRequest to actually apply
// the reduction. Only ONE Corrosion modifier is ever active per target at a time (mirrors
// ProjectileOnHitSystem.ApplyScorch's own "refresh the existing one" shape) — any Corrosion-
// wielding attacker's hit refreshes/stacks the SAME shared debuff rather than creating a
// separate one per attacker.
public struct CorrosionComponent : IComponent
{
    // How many qualifying hits have landed since this debuff was first applied (1 on the hit
    // that creates it, incremented — capped at MaxStacks — on every hit that refreshes it).
    public int Stacks;
    public int MaxStacks;

    // Armor reduction reached once Stacks == MaxStacks — see CorrosionSystem's own
    // GetArmorRequest subscriber, which scales this by Stacks / MaxStacks (i.e. linearly from
    // 0 at Stacks == 0 up to this at MaxStacks).
    public float MaxArmorReduction;
}
