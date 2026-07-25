// Payload for a ModifierComponent-carrying modifier entity — while active, blocks
// CanMoveOnOwnAccountRequest for its target (see RootedSystem). Unlike StunnedComponent/
// ActionWindupComponent (which block CanMoveRequest and CanPerformRequest too — a stunned or
// winding-up troop can't act at all), a rooted troop can still attack/cast abilities and can
// still be knocked around by external forces (e.g. DisplacementSystem) — it just can't move
// itself. No fields: its mere presence on an active modifier is the whole effect.
public struct RootedComponent : IComponent
{
}
