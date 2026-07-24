public enum RenderableModifierType : byte
{
    SpeedBoost = 0,
    Chilled = 1,
    Frozen = 2,
    Scorched = 3,
    ShadowCloak = 4,
}

// Attached to a modifier entity (alongside its ModifierComponent) to say which visual
// RenderableModifierManager should show on ModifierComponent.TargetEntityId while this
// modifier is active. See RenderableModifierManager for why tracking is keyed by the
// target's entity id rather than this modifier entity's own id.
public struct RenderableModifierComponent : IComponent
{
    public RenderableModifierType Type;
}
