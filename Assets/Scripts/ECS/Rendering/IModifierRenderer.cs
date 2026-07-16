using System.Collections.Generic;

/// <summary>
/// Handles the visual representation of active modifiers (buffs/debuffs) of a specific
/// RenderableModifierType. Register implementations with RenderableModifierManager.
///
/// Unlike IComponentRenderer, every callback here is keyed by the TARGET entity id
/// (ModifierComponent.TargetEntityId — the troop/building the modifier is attached to),
/// never by the modifier entity's own id. A modifier entity is allowed to be spawned
/// client-side and later replaced by the server's own copy under a different entity id (see
/// ModifierComponent's doc comment) — RenderableModifierManager collapses that churn into a
/// single add/remove pair per target via reference counting, so an implementation only ever
/// needs to think in terms of "does this target currently have this kind of modifier."
/// </summary>
public interface IModifierRenderer
{
    /// <summary>Called once, before any other callback, so the renderer can stash the
    /// ECS reference and subscribe to whatever flag events it needs.</summary>
    void Initialize(ECS ecs);

    /// <summary>Called when targetEntityId transitions from having no active modifier of
    /// this type to having at least one (the first modifier entity of this type/target
    /// combination seen "wins" the visual - see RenderableModifierManager).</summary>
    void OnEntityAdded(ulong targetEntityId);

    /// <summary>Called when targetEntityId no longer has any active modifier of this type.</summary>
    void OnEntityRemoved(ulong targetEntityId);

    /// <summary>
    /// Called when a modifier entity of this type targeting targetEntityId activates
    /// (EntityActivatedEvent). Can fire more than once for the same targetEntityId — e.g. a
    /// client-predicted modifier's activation followed shortly by the server-confirmed
    /// replacement's own activation — so implementations should guard the same way
    /// OnEntityAdded implementations typically do (e.g. skip if a visual already exists for
    /// this id).
    /// </summary>
    void OnEntityActivated(ulong targetEntityId);

    /// <summary>
    /// Called every Unity Update with the current list of distinct target entity ids that
    /// have at least one active modifier of this type. targetEntityId can disappear from the
    /// ECS out from under this list (e.g. the target died) before the next OnEntityRemoved —
    /// same as any other IComponentRenderer, check HasComponent before reading its position.
    /// </summary>
    void UpdateRenderable(List<ulong> targetEntityIds);
}
