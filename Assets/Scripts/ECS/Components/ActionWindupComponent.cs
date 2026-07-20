// Payload for a ModifierComponent-carrying modifier entity — while active, blocks both
// CanMoveRequest and CanPerformRequest for its target (see ActionWindupSystem), freezing
// it in place and preventing it from starting/finishing any other action. No fields: its
// mere presence on an active modifier is the whole effect. Typically paired with a
// FireProjectileOnExpireComponent on the same modifier entity for a "windup, then fire"
// ability (the same TicksRemaining/expiry drives both — see AbilityManager's skillshot
// ability), but useful standalone for any other windup that shouldn't move or act (e.g. a
// channeled ability).
public struct ActionWindupComponent : IComponent
{
}
