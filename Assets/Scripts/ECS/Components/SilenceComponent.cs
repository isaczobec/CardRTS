// Marks a modifier entity as silencing whatever entity it targets — see SilenceSystem,
// which vetoes CanPerformRequest (blocking attacks/ability use only; movement and
// activation are untouched — see ActivationQuery.CanPerform's own doc comment, which
// already anticipates exactly this) for that entity while this modifier is active. No
// fields — the modifier's presence/target/duration is all SilenceSystem needs, same shape
// as StunnedComponent/RootedComponent.
public struct SilenceComponent : IComponent
{
}
