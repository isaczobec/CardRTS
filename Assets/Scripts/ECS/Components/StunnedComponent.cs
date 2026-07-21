// Payload for a ModifierComponent-carrying modifier entity — while active, blocks both
// CanMoveRequest and CanPerformRequest for its target (see StunnedSystem). Same shape as
// ActionWindupComponent, but for a genuine "can't move or act at all" debuff inflicted by
// something else (e.g. AbilityManager's Ice Nova Frozen effect) rather than a self-inflicted
// ability windup. No fields: its mere presence on an active modifier is the whole effect.
public struct StunnedComponent : IComponent
{
}
