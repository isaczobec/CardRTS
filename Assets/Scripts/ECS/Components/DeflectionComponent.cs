// Reduces incoming damage the further away the attacker was when the hit landed — no
// reduction at MinRange or closer, ramping up linearly to MaxReductionRatio at MaxRange or
// further. Attach alongside a ModifierComponent (see DeflectionSystem) on a modifier entity
// targeting whoever should be protected.
public struct DeflectionComponent : IComponent
{
    public float MinRange;
    public float MaxRange;
    public float MaxReductionRatio;
}
